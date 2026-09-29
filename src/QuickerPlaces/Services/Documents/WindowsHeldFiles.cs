using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace QuickerPlaces.Services.Documents;

/// <summary>
/// Lists the PDF, Word and Excel files that running programs hold open
/// (held-files plan), by asking Windows for those programs' file handles
/// rather than guessing paths and asking about each: every tab Revu or
/// Acrobat has open and every document Word or Excel has open, with its
/// full path, whether or not it is in Recent Items.
///
/// - NtQuerySystemInformation(SystemExtendedHandleInformation) lists every
///   handle in the system; only the target programs' file handles are
///   kept. The "File" object type's number differs between Windows
///   versions, so it is read from a handle this process opens on its own
///   executable, in the same list.
/// - Each is copied into this process (DuplicateHandle), kept only if it is
///   a disk file (GetFileType, so a pipe is never asked for its name, which
///   can hang), and named by GetFinalPathNameByHandle.
/// - <see cref="HeldFilePaths"/> turns the names into session paths.
///
/// This is how Process Explorer and handle.exe list open files. It needs
/// no elevation for the user's own programs; an elevated program's files
/// are skipped. It runs on its own background thread with a time limit
/// (H4): if a handle stalls, what was found so far is returned and the
/// thread is left to finish on its own.
///
/// App-only: it calls Windows, so it is not linked into the test project.
/// </summary>
public static class WindowsHeldFiles
{
    /// <summary>
    /// The documents held by <paramref name="programs"/> and by every other
    /// process with the same executable name, as Revu and Acrobat split
    /// their work over several. Throws if the handle list can't be read.
    /// </summary>
    public static HeldFilesRead Read(IReadOnlyList<(uint ProcessId, string AppName)> programs, TimeSpan timeout)
    {
        var targets = WithSameNamedProcesses(programs);
        if (targets.Count == 0)
            return new HeldFilesRead(Array.Empty<HeldFile>(), IsComplete: true);

        var found = new ConcurrentQueue<(string FinalPath, string AppName)>();
        Exception? failure = null;
        var worker = new Thread(() =>
        {
            try
            {
                Collect(targets, found);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        })
        {
            IsBackground = true,
            Name = "QuickerPlaces held files",
        };
        worker.Start();

        var finished = worker.Join(timeout);
        if (finished && failure is not null)
            throw new InvalidOperationException("Listing held files failed.", failure);

        var files = HeldFilePaths.Resolve(found.ToArray(), MappedDrives(), ExcludedFolders());
        return new HeldFilesRead(files, finished);
    }

    private static Dictionary<uint, string> WithSameNamedProcesses(IReadOnlyList<(uint ProcessId, string AppName)> programs)
    {
        var targets = new Dictionary<uint, string>();
        foreach (var (processId, appName) in programs)
        {
            targets.TryAdd(processId, appName);

            string name;
            try
            {
                using var process = Process.GetProcessById((int)processId);
                name = process.ProcessName;
            }
            catch (Exception)
            {
                // Gone already: it holds nothing.
                continue;
            }

            foreach (var sibling in Process.GetProcessesByName(name))
            {
                targets.TryAdd((uint)sibling.Id, appName);
                sibling.Dispose();
            }
        }

        targets.Remove((uint)Environment.ProcessId);
        return targets;
    }

    private static void Collect(Dictionary<uint, string> targets, ConcurrentQueue<(string FinalPath, string AppName)> found)
    {
        var (handles, fileType) = SnapshotWithFileType();
        var self = GetCurrentProcess();

        var byProcess = handles
            .Where(h => h.ObjectTypeIndex == fileType && targets.ContainsKey((uint)h.UniqueProcessId))
            .GroupBy(h => (uint)h.UniqueProcessId);

        foreach (var group in byProcess)
        {
            var process = OpenProcess(ProcessDupHandle, false, group.Key);
            if (process == IntPtr.Zero)
                continue; // Elevated, or gone.

            try
            {
                foreach (var entry in group)
                {
                    if (!DuplicateHandle(process, entry.HandleValue, self, out var copy, 0, false, DuplicateSameAccess))
                        continue;

                    try
                    {
                        if (GetFileType(copy) == FileTypeDisk && FinalPath(copy) is { } path)
                            found.Enqueue((path, targets[group.Key]));
                    }
                    finally
                    {
                        CloseHandle(copy);
                    }
                }
            }
            finally
            {
                CloseHandle(process);
            }
        }
    }

    /// <summary>Every handle in the system, and the object type number of a file handle, read from one this process holds on its own executable.</summary>
    private static (SystemHandleEntry[] Handles, ushort FileType) SnapshotWithFileType()
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("This process has no executable path.");
        using var own = File.OpenHandle(executable, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

        var handles = SystemHandles();
        var ownId = (nuint)Environment.ProcessId;
        var ownHandle = own.DangerousGetHandle();
        foreach (var entry in handles)
        {
            if (entry.UniqueProcessId == ownId && entry.HandleValue == ownHandle)
                return (handles, entry.ObjectTypeIndex);
        }

        throw new InvalidOperationException("The file object type could not be found.");
    }

    private static SystemHandleEntry[] SystemHandles()
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

                var entries = new SystemHandleEntry[count];
                for (var i = 0; i < count; i++)
                    entries[i] = Marshal.PtrToStructure<SystemHandleEntry>(buffer + header + checked((int)(i * entrySize)));
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
        var length = GetFinalPathNameByHandle(file, buffer, (uint)buffer.Capacity, FileNameNormalizedDos);
        if (length == 0)
            return null;

        if (length >= buffer.Capacity)
        {
            buffer = new StringBuilder((int)length + 1);
            length = GetFinalPathNameByHandle(file, buffer, (uint)buffer.Capacity, FileNameNormalizedDos);
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
    /// 8.3 one ("THOMAS~1").
    /// </summary>
    private static IReadOnlyList<string> ExcludedFolders() => new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData"),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            LongPath(Path.GetTempPath()),
        }
        .Where(f => !string.IsNullOrEmpty(f))
        .ToList();

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
    private const uint FileNameNormalizedDos = 0x0;

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
/// <param name="IsComplete">False when the time limit ran out first, so some may be missing.</param>
public sealed record HeldFilesRead(IReadOnlyList<HeldFile> Files, bool IsComplete);
