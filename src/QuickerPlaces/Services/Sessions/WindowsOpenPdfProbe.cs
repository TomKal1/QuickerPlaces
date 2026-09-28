using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace QuickerPlaces.Services.Sessions;

/// <summary>
/// Gathers <see cref="OpenPdfResolver"/>'s evidence from Windows (sessions
/// plan §4) when the user asks for a scan, and never otherwise: nothing
/// watches in the background.
///
/// - Top-level windows (EnumWindows) whose titles mention ".pdf", with the
///   owning program's description and command line
///   (NtQueryInformationProcess, ProcessCommandLineInformation).
/// - Shortcuts to PDFs in the documented Recent Items folder
///   (FOLDERID_Recent), resolved through IShellLink without searching for
///   moved targets. Explorer's undocumented AutomaticDestinations storage is
///   not read (roadmap §2).
/// - For each candidate that exists, whether a program has it open,
///   through Restart Manager, ignoring Explorer's preview and indexing
///   processes, which hold a file only to show or index it.
///
/// Runs off the UI thread with a time budget: a slow network share cuts the
/// scan short with a warning rather than freezing the window (D8). Logs
/// counts only, never a title or a path (Phase 3 D26).
///
/// App-only: it calls Windows, so it is not linked into the test project.
/// </summary>
public sealed class WindowsOpenPdfProbe
{
    /// <summary>Recent Items older than this are not suggested.</summary>
    private static readonly TimeSpan RecentWindow = TimeSpan.FromDays(14);

    /// <summary>At most this many Recent Items shortcuts are read, newest first, of any file type.</summary>
    private const int MaxRecentShortcuts = 400;

    /// <summary>At most this many candidates are asked about through Restart Manager.</summary>
    private const int MaxInUseChecks = 100;

    /// <summary>How long a single file-existence check may take before its server is treated as slow.</summary>
    private static readonly TimeSpan ExistenceTimeout = TimeSpan.FromSeconds(1);

    /// <summary>The whole scan's budget, after which what was found so far is returned.</summary>
    private static readonly TimeSpan ScanBudget = TimeSpan.FromSeconds(10);

    /// <summary>Processes that open a PDF only to preview, index or scan it.</summary>
    private static readonly HashSet<string> IgnoredHolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "prevhost", "SearchProtocolHost", "SearchIndexer", "SearchFilterHost",
        "MsMpEng", "MpDefenderCoreService", "dllhost", "OneDrive", "Dropbox", "System",
    };

    /// <summary>Scans on a worker thread. Never throws: a failure becomes the scan's Warning.</summary>
    public Task<OpenPdfScan> ScanAsync() => Task.Run(Scan);

    private static OpenPdfScan Scan()
    {
        var clock = Stopwatch.StartNew();
        var warnings = new List<string>();

        IReadOnlyList<ViewerWindow> windows;
        try
        {
            windows = FindViewerWindows();
        }
        catch (Exception ex)
        {
            DiagnosticLog.Warn($"Listing PDF windows failed ({ex.GetType().Name}).");
            windows = Array.Empty<ViewerWindow>();
            warnings.Add("Open windows couldn't be read.");
        }

        IReadOnlyList<RecentDocument> recents;
        try
        {
            recents = ReadRecentDocuments();
        }
        catch (Exception ex)
        {
            DiagnosticLog.Warn($"Reading Recent Items failed ({ex.GetType().Name}).");
            recents = Array.Empty<RecentDocument>();
            warnings.Add("Windows' recent files couldn't be read.");
        }

        var evidence = new OpenPdfEvidence(windows, recents);
        var inUse = new List<string>();
        var slowServers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var existing = new List<string>();
        var missing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var timedOut = false;

        foreach (var path in OpenPdfResolver.CandidatePaths(evidence))
        {
            if (clock.Elapsed > ScanBudget)
            {
                timedOut = true;
                break;
            }

            var server = ServerOf(path);
            if (server is not null && slowServers.Contains(server))
                continue;

            switch (ExistsWithin(path, ExistenceTimeout))
            {
                case true:
                    existing.Add(path);
                    break;
                case false:
                    missing.Add(path);
                    break;
                default:
                    if (server is not null)
                        slowServers.Add(server);
                    break;
            }
        }

        try
        {
            foreach (var path in existing.Take(MaxInUseChecks))
            {
                if (clock.Elapsed > ScanBudget)
                {
                    timedOut = true;
                    break;
                }

                if (IsHeldOpen(path))
                    inUse.Add(path);
            }
        }
        catch (Exception ex)
        {
            DiagnosticLog.Warn($"Checking which PDFs are in use failed ({ex.GetType().Name}).");
            warnings.Add("Files open in the background couldn't be checked, so only the front document of each window was found.");
        }

        if (slowServers.Count > 0)
            warnings.Add($"{(slowServers.Count == 1 ? "A network share was" : "Some network shares were")} slow to answer, so {(slowServers.Count == 1 ? "its" : "their")} files weren't checked.");
        if (timedOut)
            warnings.Add("The scan took too long and stopped early. Add any missing PDFs by hand.");

        // A recent file that no longer exists isn't worth suggesting; one on a slow share is kept, unchecked.
        var kept = new OpenPdfEvidence(windows, recents.Where(r => !missing.Contains(SessionPaths.NormalizePdf(r.Path) ?? r.Path)).ToList());
        var scan = OpenPdfResolver.Resolve(kept, inUse);

        DiagnosticLog.Info($"PDF scan: {windows.Count} window(s), {recents.Count} recent, {inUse.Count} in use, " +
                           $"{scan.Candidates.Count(c => c.IsLikelyOpen)} judged open, {scan.UnmatchedTitles.Count} unmatched, {clock.ElapsedMilliseconds} ms.");

        return warnings.Count == 0 ? scan : scan with { Warning = string.Join(" ", warnings) };
    }

    // ---------------------------------------------------------------
    // Windows
    // ---------------------------------------------------------------

    private static IReadOnlyList<ViewerWindow> FindViewerWindows()
    {
        var found = new List<(string Title, uint ProcessId)>();
        var own = (uint)Environment.ProcessId;

        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd))
                return true;

            var length = GetWindowTextLength(hwnd);
            if (length <= 4)
                return true;

            var text = new StringBuilder(length + 1);
            if (GetWindowText(hwnd, text, text.Capacity) <= 0)
                return true;

            var title = text.ToString();
            if (title.IndexOf(".pdf", StringComparison.OrdinalIgnoreCase) < 0)
                return true;

            GetWindowThreadProcessId(hwnd, out var processId);
            if (processId != own)
                found.Add((title, processId));
            return true;
        }, IntPtr.Zero);

        var programs = new Dictionary<uint, (string AppName, string? CommandLine)>();
        var windows = new List<ViewerWindow>();
        foreach (var (title, processId) in found)
        {
            if (!programs.TryGetValue(processId, out var program))
                programs[processId] = program = DescribeProcess(processId);
            windows.Add(new ViewerWindow(title, program.AppName, program.CommandLine));
        }

        return windows;
    }

    /// <summary>The program's own description ("Adobe Acrobat"), or its process name, and its command line if it can be read.</summary>
    private static (string AppName, string? CommandLine) DescribeProcess(uint processId)
    {
        var appName = "another program";
        string? commandLine = null;

        var handle = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (handle == IntPtr.Zero)
            return (appName, null);

        try
        {
            var imagePath = new StringBuilder(1024);
            var size = (uint)imagePath.Capacity;
            if (QueryFullProcessImageName(handle, 0, imagePath, ref size))
            {
                var image = imagePath.ToString();
                appName = Path.GetFileNameWithoutExtension(image);
                try
                {
                    var description = FileVersionInfo.GetVersionInfo(image).FileDescription;
                    if (!string.IsNullOrWhiteSpace(description))
                        appName = description.Trim();
                }
                catch (Exception)
                {
                    // The file name will do.
                }
            }

            commandLine = ReadCommandLine(handle);
        }
        finally
        {
            CloseHandle(handle);
        }

        return (appName, commandLine);
    }

    /// <summary>A process's command line (Windows 8.1 and later), or null if it can't be read.</summary>
    private static string? ReadCommandLine(IntPtr process)
    {
        // The first call only asks for the size; it fails with STATUS_INFO_LENGTH_MISMATCH by design.
        _ = NtQueryInformationProcess(process, ProcessCommandLineInformation, IntPtr.Zero, 0, out var needed);
        if (needed <= 0 || needed > 1 << 20)
            return null;

        var buffer = Marshal.AllocHGlobal(needed);
        try
        {
            if (NtQueryInformationProcess(process, ProcessCommandLineInformation, buffer, needed, out _) != 0)
                return null;

            var text = Marshal.PtrToStructure<UnicodeString>(buffer);
            return text.Buffer == IntPtr.Zero ? null : Marshal.PtrToStringUni(text.Buffer, text.Length / 2);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    // ---------------------------------------------------------------
    // Recent Items
    // ---------------------------------------------------------------

    private static IReadOnlyList<RecentDocument> ReadRecentDocuments()
    {
        var folder = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            return Array.Empty<RecentDocument>();

        // Every shortcut, not just "*.pdf.lnk": the target decides, whatever the shortcut happens to be called.
        var since = DateTime.UtcNow - RecentWindow;
        var shortcuts = new DirectoryInfo(folder)
            .EnumerateFiles("*.lnk")
            .Where(f => f.LastWriteTimeUtc >= since)
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Take(MaxRecentShortcuts)
            .ToList();

        var documents = new List<RecentDocument>();
        foreach (var shortcut in shortcuts)
        {
            if (ShortcutTarget(shortcut.FullName) is { } target && target.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                documents.Add(new RecentDocument(target, new DateTimeOffset(shortcut.LastWriteTimeUtc, TimeSpan.Zero)));
        }

        return documents;
    }

    /// <summary>The path a shortcut points at, as stored: no search for a moved target, so no network access.</summary>
    private static string? ShortcutTarget(string shortcutPath)
    {
        object? link = null;
        try
        {
            link = new ShellLink();
            ((IPersistFile)link).Load(shortcutPath, 0);
            var target = new StringBuilder(1024);
            ((IShellLinkW)link).GetPath(target, target.Capacity, IntPtr.Zero, SlgpRawPath);
            var path = Environment.ExpandEnvironmentVariables(target.ToString());
            return path.Length == 0 ? null : path;
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            if (link is not null)
                Marshal.FinalReleaseComObject(link);
        }
    }

    // ---------------------------------------------------------------
    // Files in use
    // ---------------------------------------------------------------

    /// <summary>True when a program other than Explorer's helpers has <paramref name="path"/> open, by Restart Manager.</summary>
    private static bool IsHeldOpen(string path)
    {
        var key = new StringBuilder(RmSessionKeyLength + 1);
        if (RmStartSession(out var session, 0, key) != 0)
            throw new InvalidOperationException("Restart Manager could not start a session.");

        try
        {
            if (RmRegisterResources(session, 1, new[] { path }, 0, null, 0, null) != 0)
                return false;

            uint needed = 0, count = 0;
            var result = RmGetList(session, out needed, ref count, null, out _);
            if (result == 0 || needed == 0)
                return false;
            if (result != ErrorMoreData)
                return false;

            var processes = new RmProcessInfo[needed];
            count = needed;
            if (RmGetList(session, out needed, ref count, processes, out _) != 0)
                return false;

            var own = Environment.ProcessId;
            return processes.Take((int)count).Any(p => p.Process.ProcessId != own && !IsIgnoredHolder(p.Process.ProcessId));
        }
        finally
        {
            RmEndSession(session);
        }
    }

    private static bool IsIgnoredHolder(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return IgnoredHolders.Contains(process.ProcessName);
        }
        catch (Exception)
        {
            // Gone already: it no longer holds anything.
            return true;
        }
    }

    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------

    /// <summary>True or false if the check answered in time; null if it didn't, as an offline share may not.</summary>
    private static bool? ExistsWithin(string path, TimeSpan timeout)
    {
        var check = Task.Run(() => File.Exists(path));
        return check.Wait(timeout) ? check.Result : null;
    }

    /// <summary>"server" for "\\server\share\x.pdf"; null for a drive path.</summary>
    private static string? ServerOf(string path)
    {
        if (!path.StartsWith(@"\\", StringComparison.Ordinal))
            return null;
        var end = path.IndexOf('\\', 2);
        return end < 0 ? path[2..] : path[2..end];
    }

    // ---------------------------------------------------------------
    // Interop
    // ---------------------------------------------------------------

    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const int ProcessCommandLineInformation = 60;
    private const uint SlgpRawPath = 0x4;
    private const int RmSessionKeyLength = 32;
    private const int ErrorMoreData = 234;

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder path, ref uint size);

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(IntPtr process, int informationClass, IntPtr information, int length, out int returnLength);

    [StructLayout(LayoutKind.Sequential)]
    private struct UnicodeString
    {
        public ushort Length;
        public ushort MaximumLength;
        public IntPtr Buffer;
    }

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmStartSession(out uint session, int flags, StringBuilder sessionKey);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmEndSession(uint session);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmRegisterResources(uint session, uint fileCount, string[] files,
        uint applicationCount, RmUniqueProcess[]? applications, uint serviceCount, string[]? services);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmGetList(uint session, out uint needed, ref uint count,
        [In, Out] RmProcessInfo[]? processes, out uint rebootReasons);

    [StructLayout(LayoutKind.Sequential)]
    private struct RmUniqueProcess
    {
        public int ProcessId;
        public FILETIME ProcessStartTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RmProcessInfo
    {
        public RmUniqueProcess Process;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string AppName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string ServiceShortName;

        public int ApplicationType;
        public uint AppStatus;
        public uint TsSessionId;

        [MarshalAs(UnmanagedType.Bool)]
        public bool Restartable;
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink
    {
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int maxPath, IntPtr findData, uint flags);
        void GetIDList(out IntPtr idList);
        void SetIDList(IntPtr idList);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int maxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int maxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int maxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int showCmd);
        void SetShowCmd(int showCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int iconPathLength, out int iconIndex);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string relativePath, uint reserved);
        void Resolve(IntPtr hwnd, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }
}
