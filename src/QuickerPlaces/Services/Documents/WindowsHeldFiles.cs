using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using QuickerPlaces.Services;

namespace QuickerPlaces.Services.Documents;

/// <summary>
/// Lists the PDF, Word and Excel files that running programs hold open
/// (held-files plan), by asking Windows for those programs' file handles
/// rather than guessing paths and asking about each: every tab Revu or
/// Acrobat has open and every document Word or Excel has open, with its
/// full path, whether or not it is in Recent Items.
///
/// - Targets are the given programs first, then every other process with the
///   same executable name (Revu and Acrobat split their work over several),
///   in this user's session only. Explorer and browsers are never targets
///   (<see cref="SkippedPrograms"/>).
/// - NtQuerySystemInformation(SystemExtendedHandleInformation) lists every
///   handle in the system; only the targets' file handles are kept. The
///   "File" object type's number differs between Windows versions, so it is
///   read from a handle this process opens on its own executable, in the
///   same list.
/// - Each is copied into this process (DuplicateHandle), kept only if it is
///   a disk file (GetFileType), and named by GetFinalPathNameByHandle with
///   FILE_NAME_OPENED, which needs no round trip to a file server, unlike
///   FILE_NAME_NORMALIZED, so a slow share can't stall it.
/// - <see cref="HeldFilePaths"/> turns the names into session paths.
///
/// This is how Process Explorer and handle.exe list open files. It needs
/// no elevation for the user's own programs; an elevated program's files
/// are skipped.
///
/// A few handles can block whatever asks about them: GetFileType itself
/// stalls on a synchronous pipe with a pending read, and a name from a dead
/// share can too. So the handles are walked on a background thread that a
/// watchdog watches (H4): a handle that takes longer than
/// <see cref="HandleTimeout"/> is abandoned with its thread, remembered in
/// <see cref="StuckHandles"/> so no later scan asks about it (unless the
/// abandoned thread comes back, showing it was only slow, and forgets it), and
/// the walk carries on from the next handle on a fresh thread. The whole read
/// also has the caller's time limit; what was found so far is returned when
/// it runs out.
///
/// App-only: it calls Windows, so it is not linked into the test project.
/// </summary>
public static class WindowsHeldFiles
{
    /// <summary>
    /// Programs never read, even when a window of theirs is given or a
    /// sibling of a given program: Explorer and the shell helpers hold files
    /// only to show them, and browsers don't keep a PDF open and run dozens
    /// of processes full of pipes. Kept in step with
    /// WindowsOpenDocumentProbe.IgnoredHolders, which names the same helpers
    /// for Restart Manager.
    /// </summary>
    private static readonly HashSet<string> SkippedPrograms = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "prevhost", "dllhost", "SearchHost", "SearchProtocolHost", "SearchIndexer", "SearchFilterHost",
        "MsMpEng", "MpDefenderCoreService", "OneDrive", "Dropbox",
        "msedge", "msedgewebview2", "chrome", "firefox", "brave", "opera", "iexplore",
    };

    /// <summary>
    /// Handles that stalled a read before, so are skipped from then on: a stuck
    /// pipe then costs one thread once, not one every scan. An entry is removed
    /// if its worker ever returns (the handle was only slow), which also covers
    /// a handle value that is later reused for another file after the stuck
    /// one closed. (The kernel object address that would tell them apart is
    /// zero for callers without SeDebugPrivilege.)
    /// </summary>
    private static readonly ConcurrentDictionary<(uint ProcessId, nint Handle), byte> StuckHandles = new();

    private static readonly TimeSpan HandleTimeout = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(25);
    private static readonly TimeSpan MinimumDriveWait = TimeSpan.FromMilliseconds(250);
    private const int MaxAbandonedWorkers = 3;

    /// <summary>
    /// The documents held by <paramref name="programs"/> and by every other
    /// process with the same executable name. Throws only if the handle list
    /// can't be read; a stalled or failed walk returns what was found, with
    /// <see cref="HeldFilesRead.IsComplete"/> false.
    /// </summary>
    public static HeldFilesRead Read(IReadOnlyList<(uint ProcessId, string AppName)> programs, TimeSpan timeout)
    {
        var clock = Stopwatch.StartNew();

        // Mapped drives are asked for in parallel: WNetGetConnection can be slow for a disconnected drive.
        var drivesTask = Task.Run(MappedDrives);

        var targets = SelectTargets(programs);
        if (targets.Count == 0)
            return new HeldFilesRead(Array.Empty<HeldFile>(), IsComplete: true);

        var handles = ListFileHandles(targets);
        var found = new ConcurrentQueue<(string FinalPath, string AppName)>();
        var isComplete = WalkAll(handles, targets, found, clock, timeout);

        IReadOnlyDictionary<string, string> drives;
        try
        {
            // A minimum wait, so the drive spelling of a path doesn't depend on how long the walk took.
            var remaining = timeout - clock.Elapsed;
            drives = drivesTask.Wait(remaining > MinimumDriveWait ? remaining : MinimumDriveWait)
                ? drivesTask.Result
                : new Dictionary<string, string>();
        }
        catch (Exception)
        {
            // An unmapped \\server\share path is still usable.
            drives = new Dictionary<string, string>();
        }

        return new HeldFilesRead(HeldFilePaths.Resolve(found.ToArray(), drives, ExcludedFolders()), isComplete);
    }

    private readonly record struct Target(string AppName, int Order);

    private readonly record struct HeldHandle(uint ProcessId, nint Handle);

    /// <summary>
    /// The given programs in the order given, then their same-named siblings,
    /// each with its program's name and its place in that order. Left out:
    /// this process, other sessions' processes, and <see cref="SkippedPrograms"/>.
    /// </summary>
    private static Dictionary<uint, Target> SelectTargets(IReadOnlyList<(uint ProcessId, string AppName)> programs)
    {
        var running = new List<(uint Id, string Name, int Session)>();
        var all = Process.GetProcesses();
        try
        {
            foreach (var process in all)
            {
                try
                {
                    running.Add(((uint)process.Id, process.ProcessName, process.SessionId));
                }
                catch (Exception)
                {
                    // Gone already: it holds nothing.
                }
            }
        }
        finally
        {
            foreach (var process in all)
                process.Dispose();
        }

        var ownId = (uint)Environment.ProcessId;
        int ownSession;
        using (var current = Process.GetCurrentProcess())
            ownSession = current.SessionId;
        bool Eligible((uint Id, string Name, int Session) p) =>
            p.Id != ownId && p.Session == ownSession && !SkippedPrograms.Contains(p.Name);

        var targets = new Dictionary<uint, Target>();
        var order = 0;
        foreach (var (processId, appName) in programs)
        {
            if (running.FirstOrDefault(p => p.Id == processId) is { Name: not null } given && Eligible(given))
                targets.TryAdd(processId, new Target(appName, order++));
        }

        foreach (var (processId, appName) in programs)
        {
            if (running.FirstOrDefault(p => p.Id == processId) is not { Name: not null } given || !Eligible(given))
                continue;

            foreach (var sibling in running.Where(p => string.Equals(p.Name, given.Name, StringComparison.OrdinalIgnoreCase) && Eligible(p)))
                targets.TryAdd(sibling.Id, new Target(appName, order++));
        }

        return targets;
    }

    /// <summary>
    /// The targets' file handles, not yet asked about, in target order and
    /// within a process in handle-list order, without the known stuck ones.
    /// </summary>
    private static HeldHandle[] ListFileHandles(Dictionary<uint, Target> targets)
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("This process has no executable path.");
        using var own = File.OpenHandle(executable, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

        var ownId = (nuint)Environment.ProcessId;
        var entries = SystemHandles(id => id == ownId || targets.ContainsKey((uint)id));

        var ownHandle = own.DangerousGetHandle();
        ushort? fileType = null;
        foreach (var entry in entries)
        {
            if (entry.UniqueProcessId == ownId && entry.HandleValue == ownHandle)
            {
                fileType = entry.ObjectTypeIndex;
                break;
            }
        }

        if (fileType is null)
            throw new InvalidOperationException("The file object type could not be found.");

        // OrderBy is stable, so the handle order within a process is kept.
        return entries
            .Where(e => e.ObjectTypeIndex == fileType && targets.ContainsKey((uint)e.UniqueProcessId))
            .Select(e => new HeldHandle((uint)e.UniqueProcessId, e.HandleValue))
            .Where(h => !StuckHandles.ContainsKey((h.ProcessId, h.Handle)))
            .OrderBy(h => targets[h.ProcessId].Order)
            .ToArray();
    }

    /// <summary>
    /// Asks about every handle on a watched background thread. False when the
    /// time limit ran out, a walk failed, or too many threads were abandoned.
    /// </summary>
    private static bool WalkAll(HeldHandle[] handles, Dictionary<uint, Target> targets,
        ConcurrentQueue<(string FinalPath, string AppName)> found, Stopwatch clock, TimeSpan timeout)
    {
        var next = 0;
        var abandoned = 0;
        while (next < handles.Length)
        {
            if (clock.Elapsed >= timeout)
                return false;

            var progress = new WalkProgress(next);
            var worker = new Thread(() => Walk(handles, progress, targets, found))
            {
                IsBackground = true,
                Name = "QuickerPlaces held files",
            };
            worker.Start();

            while (true)
            {
                Thread.Sleep(PollInterval);

                if (progress.Done)
                {
                    if (progress.Failure is { } failure)
                    {
                        DiagnosticLog.Warn($"Listing held documents stopped ({failure.GetType().Name}).");
                        return false;
                    }

                    next = handles.Length;
                    break;
                }

                if (clock.Elapsed >= timeout)
                {
                    progress.Cancelled = true;
                    return false;
                }

                // Current is read before StepStarted: the walk writes them the other way round.
                // Below zero the thread hasn't taken its first step yet, so there is no handle to blame.
                var current = progress.Current;
                if (current >= 0 && Stopwatch.GetElapsedTime(progress.StepStarted) > HandleTimeout)
                {
                    // Recorded before cancelling: the worker removes it again if it comes back and finds itself cancelled.
                    StuckHandles.TryAdd((handles[current].ProcessId, handles[current].Handle), 0);
                    progress.Cancelled = true;
                    next = current + 1;
                    if (++abandoned >= MaxAbandonedWorkers)
                        return false;

                    break;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Asks about handles[progress.Current...] one at a time, publishing which
    /// it is on and when it began so the watchdog can tell when one stalls.
    /// Checks for cancellation before each, so a thread that un-sticks after
    /// being abandoned stops instead of carrying on.
    /// </summary>
    private static void Walk(HeldHandle[] handles, WalkProgress progress, Dictionary<uint, Target> targets,
        ConcurrentQueue<(string FinalPath, string AppName)> found)
    {
        var self = GetCurrentProcess();
        var processes = new Dictionary<uint, IntPtr>();
        try
        {
            for (var i = progress.Start; i < handles.Length; i++)
            {
                if (progress.Cancelled)
                    return;

                progress.StepStarted = Stopwatch.GetTimestamp();
                progress.Current = i;

                Ask(handles[i], self, processes, targets, found);

                if (progress.Cancelled)
                {
                    // The watchdog gave up on this handle, but it came back: it was only slow
                    // (a big PDF on a slow share, say), so later scans should ask about it again.
                    StuckHandles.TryRemove((handles[i].ProcessId, handles[i].Handle), out _);
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            progress.Failure = ex;
        }
        finally
        {
            foreach (var process in processes.Values)
            {
                if (process != IntPtr.Zero)
                    CloseHandle(process);
            }

            progress.Done = true;
        }
    }

    /// <summary>Copies one handle into this process and, if it is a disk file, queues its path.</summary>
    private static void Ask(HeldHandle held, IntPtr self, Dictionary<uint, IntPtr> processes, Dictionary<uint, Target> targets,
        ConcurrentQueue<(string FinalPath, string AppName)> found)
    {
        if (!processes.TryGetValue(held.ProcessId, out var process))
            processes[held.ProcessId] = process = OpenProcess(ProcessDupHandle, false, held.ProcessId);
        if (process == IntPtr.Zero)
            return; // Elevated, or gone.

        if (!DuplicateHandle(process, held.Handle, self, out var copy, 0, false, DuplicateSameAccess))
            return;

        try
        {
            if (GetFileType(copy) == FileTypeDisk && FinalPath(copy) is { } path)
                found.Enqueue((path, targets[held.ProcessId].AppName));
        }
        finally
        {
            CloseHandle(copy);
        }
    }

    /// <summary>What one walk thread reports to the watchdog, and the watchdog to it.</summary>
    private sealed class WalkProgress
    {
        private long stepStarted = Stopwatch.GetTimestamp();

        public WalkProgress(int start)
        {
            Start = start;
        }

        public int Start { get; }

        /// <summary>The index of the handle being asked about; -1 until the thread takes its first step.</summary>
        public volatile int Current = -1;

        /// <summary>When that began (a Stopwatch timestamp). A long can't be volatile, so it is read and written through Volatile.</summary>
        public long StepStarted
        {
            get => Volatile.Read(ref stepStarted);
            set => Volatile.Write(ref stepStarted, value);
        }

        /// <summary>Set by the watchdog: stop before the next handle.</summary>
        public volatile bool Cancelled;

        public volatile bool Done;

        public volatile Exception? Failure;
    }

    /// <summary>
    /// The entries of every handle in the system whose process id
    /// <paramref name="keep"/> accepts. The id is read from the raw bytes
    /// first, so only the wanted entries are ever converted.
    /// </summary>
    private static List<SystemHandleEntry> SystemHandles(Func<nuint, bool> keep)
    {
        var size = 4 << 20;
        while (true)
        {
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                var status = NtQuerySystemInformation(SystemExtendedHandleInformation, buffer, size, out var needed);
                if (status == StatusInfoLengthMismatch)
                {
                    // Handles come and go between calls, so leave room.
                    size = Math.Max(size * 2, needed + (1 << 20));
                    if (size > MaxHandleBuffer)
                        throw new InvalidOperationException("The system handle list is too large.");
                    continue;
                }

                if (status != 0)
                    throw new InvalidOperationException($"NtQuerySystemInformation returned 0x{status:X8}.");

                var count = (long)Marshal.ReadIntPtr(buffer);
                var entrySize = Marshal.SizeOf<SystemHandleEntry>();
                var header = 2 * IntPtr.Size;
                if (count < 0 || header + count * entrySize > size)
                    throw new InvalidOperationException("The system handle list is malformed.");

                var entries = new List<SystemHandleEntry>();
                for (var i = 0; i < count; i++)
                {
                    var offset = checked((int)(header + i * entrySize));
                    if (keep((nuint)Marshal.ReadIntPtr(buffer, offset + IntPtr.Size)))
                        entries.Add(Marshal.PtrToStructure<SystemHandleEntry>(buffer + offset));
                }

                return entries;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }

    /// <summary>The handle's path as "\\?\C:\…" or "\\?\UNC\server\share\…", or null.</summary>
    private static string? FinalPath(IntPtr file)
    {
        var buffer = new StringBuilder(1024);
        var length = GetFinalPathNameByHandle(file, buffer, (uint)buffer.Capacity, FileNameOpenedDos);
        if (length == 0)
            return null;

        if (length >= buffer.Capacity)
        {
            buffer = new StringBuilder((int)length + 1);
            length = GetFinalPathNameByHandle(file, buffer, (uint)buffer.Capacity, FileNameOpenedDos);
            if (length == 0 || length >= buffer.Capacity)
                return null;
        }

        return buffer.ToString();
    }

    /// <summary>Each mapped network drive ("P:") and the share it points at ("\\files\projects").</summary>
    private static IReadOnlyDictionary<string, string> MappedDrives()
    {
        var drives = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Network))
        {
            var letter = drive.Name.TrimEnd('\\');
            var remote = new StringBuilder(1024);
            var length = remote.Capacity;
            if (WNetGetConnection(letter, remote, ref length) == 0)
                drives[letter] = remote.ToString();
        }

        return drives;
    }

    /// <summary>
    /// Folders whose files are programs' own, never the user's documents (H2).
    /// The profile's whole AppData folder covers LocalLow, where Acrobat
    /// Reader's protected mode keeps its data; Roaming and Local are listed
    /// too in case either is redirected elsewhere. TEMP is expanded to its
    /// long name, since handles report long names and TEMP may be set to an
    /// 8.3 one ("THOMAS~1"). Folders are not checked to exist.
    /// </summary>
    private static IReadOnlyList<string> ExcludedFolders() => new[]
        {
            Path.Combine(SpecialFolder(Environment.SpecialFolder.UserProfile), "AppData"),
            SpecialFolder(Environment.SpecialFolder.ApplicationData),
            SpecialFolder(Environment.SpecialFolder.LocalApplicationData),
            SpecialFolder(Environment.SpecialFolder.CommonApplicationData),
            SpecialFolder(Environment.SpecialFolder.ProgramFiles),
            SpecialFolder(Environment.SpecialFolder.ProgramFilesX86),
            SpecialFolder(Environment.SpecialFolder.Windows),
            LongPath(Path.GetTempPath()),
        }
        .Where(f => !string.IsNullOrEmpty(f))
        .ToList();

    private static string SpecialFolder(Environment.SpecialFolder folder) =>
        Environment.GetFolderPath(folder, Environment.SpecialFolderOption.DoNotVerify);

    /// <summary>The long form of a folder path ("C:\Users\Thomas\…" for "C:\Users\THOMAS~1\…"), or the path as given.</summary>
    private static string LongPath(string path)
    {
        var buffer = new StringBuilder(1024);
        var length = GetLongPathName(path, buffer, buffer.Capacity);
        return length > 0 && length < buffer.Capacity ? buffer.ToString() : path;
    }

    // ---------------------------------------------------------------
    // Interop
    // ---------------------------------------------------------------

    private const int SystemExtendedHandleInformation = 64;
    private const int StatusInfoLengthMismatch = unchecked((int)0xC0000004);
    private const int MaxHandleBuffer = 256 << 20;
    private const uint ProcessDupHandle = 0x0040;
    private const uint DuplicateSameAccess = 0x2;
    private const uint FileTypeDisk = 0x1;

    /// <summary>FILE_NAME_OPENED | VOLUME_NAME_DOS: the name the file was opened by, which needs no round trip to a file server.</summary>
    private const uint FileNameOpenedDos = 0x8;

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemHandleEntry
    {
        public nint Object;
        public nuint UniqueProcessId;
        public nint HandleValue;
        public uint GrantedAccess;
        public ushort CreatorBackTraceIndex;
        public ushort ObjectTypeIndex;
        public uint HandleAttributes;
        public uint Reserved;
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQuerySystemInformation(int informationClass, IntPtr information, int length, out int returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inheritHandle, uint processId);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DuplicateHandle(IntPtr sourceProcess, IntPtr sourceHandle, IntPtr targetProcess,
        out IntPtr targetHandle, uint access, bool inheritHandle, uint options);

    [DllImport("kernel32.dll")]
    private static extern uint GetFileType(IntPtr file);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(IntPtr file, StringBuilder path, uint length, uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetGetConnection(string localName, StringBuilder remoteName, ref int length);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetLongPathName(string shortPath, StringBuilder longPath, int length);
}

/// <summary>What <see cref="WindowsHeldFiles.Read"/> found.</summary>
/// <param name="Files">The held documents, as session paths.</param>
/// <param name="IsComplete">False when the time limit ran out or a walk failed or stalled too often, so some may be missing.</param>
public sealed record HeldFilesRead(IReadOnlyList<HeldFile> Files, bool IsComplete);
