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
using System.Runtime.Versioning;
using QuickerPlaces.Services.Revit;
using QuickerPlaces.Services.Sessions;

namespace QuickerPlaces.Services.Documents;

/// <summary>
/// Gathers <see cref="OpenDocumentResolver"/>'s evidence from Windows
/// (sessions plan §4) when the user asks for a scan, and never otherwise:
/// nothing watches in the background.
///
/// - Top-level windows (EnumWindows) whose titles mention a PDF, Word or
///   Excel file, and Word's and Excel's document windows (window classes
///   OpusApp and XLMAIN, whatever their titles say), with the owning
///   program's description and command line (NtQueryInformationProcess,
///   ProcessCommandLineInformation), through <see cref="DocumentWindows"/>.
/// - The PDF, Office, text, Revit and AutoCAD files those windows' programs hold open, with
///   full paths, through <see cref="WindowsHeldFiles"/>: every Revu or
///   Acrobat tab, whether or not it is in Recent Items.
/// - Recent Items, through the shared <see cref="WindowsRecentItems"/>.
/// - For each other candidate that exists, whether a program has it open,
///   through Restart Manager, ignoring Explorer's preview and indexing
///   processes, which hold a file only to show or index it.
///
/// Every clue is spelled with the user's mapped drive letters (H3), so one
/// file found several ways is listed once.
///
/// Runs off the UI thread with a time budget: a slow network share cuts the
/// scan short with a warning rather than freezing the window (D8). Logs
/// counts only, never a title or a path (Phase 3 D26).
///
/// App-only: it calls Windows, so it is not linked into the test project.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsOpenDocumentProbe
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

    /// <summary>How long listing the files programs hold open may take, within the scan's budget (H4).</summary>
    private static readonly TimeSpan HeldFilesBudget = TimeSpan.FromSeconds(3);

    private readonly WindowsRecentItems _recentItems;
    private readonly RevitReleaseCache? _revitReleases;

    /// <param name="revitReleases">When given, an open Revit local is listed as its central (<see cref="SessionRevitPaths"/>).</param>
    public WindowsOpenDocumentProbe(WindowsRecentItems recentItems, RevitReleaseCache? revitReleases = null)
    {
        _recentItems = recentItems;
        _revitReleases = revitReleases;
    }

    /// <summary>Processes that open a document only to preview, index or scan it.</summary>
    private static readonly HashSet<string> IgnoredHolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "prevhost", "SearchProtocolHost", "SearchIndexer", "SearchFilterHost",
        "MsMpEng", "MpDefenderCoreService", "dllhost", "OneDrive", "Dropbox", "System",
    };

    /// <summary>Scans on a worker thread. Never throws: a failure becomes the scan's Warning.</summary>
    public Task<OpenDocumentScan> ScanAsync() => Task.Run(() => PreferCentral(Scan()));

    private OpenDocumentScan PreferCentral(OpenDocumentScan scan)
    {
        if (_revitReleases is null)
            return scan;
        try
        {
            return SessionRevitPaths.PreferCentral(scan, _revitReleases, File.Exists);
        }
        catch (Exception ex)
        {
            // The locals stay listed; only the swap to their centrals is lost.
            DiagnosticLog.Warn($"Listing open Revit locals as their centrals failed ({ex.GetType().Name}).");
            return scan;
        }
    }

    private OpenDocumentScan Scan()
    {
        var clock = Stopwatch.StartNew();
        var warnings = new List<string>();

        IReadOnlyList<ViewerWindow> windows;
        IReadOnlyList<(uint ProcessId, string AppName)> programs;
        try
        {
            (windows, programs) = DocumentWindows.Find();
        }
        catch (Exception ex)
        {
            DiagnosticLog.Warn($"Listing document windows failed ({ex.GetType().Name}).");
            windows = Array.Empty<ViewerWindow>();
            programs = Array.Empty<(uint, string)>();
            warnings.Add("Open windows couldn't be read.");
        }

        IReadOnlyList<HeldFile> held;
        IReadOnlyDictionary<string, string> drives;
        try
        {
            var read = WindowsHeldFiles.Read(programs, HeldFilesBudget);
            held = read.Files;
            drives = read.MappedDrives;
            if (!read.IsComplete)
                warnings.Add("Not every file open in document programs could be listed, so some may be missing.");
        }
        catch (Exception ex)
        {
            DiagnosticLog.Warn($"Listing held documents failed ({ex.GetType().Name}).");
            held = Array.Empty<HeldFile>();
            drives = new Dictionary<string, string>();
            warnings.Add("The files open in document programs couldn't be listed, so some may be missing.");
        }

        IReadOnlyList<RecentDocument> recents;
        try
        {
            recents = _recentItems.Read(DateTimeOffset.UtcNow - RecentWindow, MaxRecentShortcuts);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Warn($"Reading Recent Items failed ({ex.GetType().Name}).");
            recents = Array.Empty<RecentDocument>();
            warnings.Add("Windows' recent files couldn't be read.");
        }

        var evidence = new OpenDocumentEvidence(windows, recents) { HeldFiles = held, MappedDrives = drives };
        var heldPaths = new HashSet<string>(held.Select(h => OpenDocumentResolver.Spell(h.Path, evidence) ?? h.Path), StringComparer.OrdinalIgnoreCase);
        var inUse = new List<string>();
        var slowVolumes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var existing = new List<string>();
        var missing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var timedOut = false;

        foreach (var path in OpenDocumentResolver.CandidatePaths(evidence))
        {
            // A held file exists and is open: the program holding it said so.
            if (heldPaths.Contains(path))
                continue;

            if (clock.Elapsed > ScanBudget)
            {
                timedOut = true;
                break;
            }

            var volume = SlowKeyOf(path, drives);
            if (volume is not null && slowVolumes.Contains(volume))
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
                    if (volume is not null)
                        slowVolumes.Add(volume);
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
            DiagnosticLog.Warn($"Checking which documents are in use failed ({ex.GetType().Name}).");
            warnings.Add(held.Count > 0
                ? "Some files open in the background couldn't be checked."
                : "Files open in the background couldn't be checked, so only the front document of each window was found.");
        }

        // A key ending in ':' is a local drive: its files are skipped too, but it isn't called a network share.
        var slowShares = slowVolumes.Count(v => !v.EndsWith(':'));
        if (slowShares > 0)
            warnings.Add($"{(slowShares == 1 ? "A network share was" : "Some network shares were")} slow to answer, so {(slowShares == 1 ? "its" : "their")} files weren't checked.");
        else if (slowVolumes.Count > 0)
            warnings.Add("A drive was slow to answer, so its files weren't checked.");
        if (timedOut)
            warnings.Add("The scan took too long and stopped early. Add any missing files by hand.");

        // A recent file that no longer exists isn't worth suggesting; one on a slow share is kept, unchecked.
        var kept = new OpenDocumentEvidence(windows, recents.Where(r => !missing.Contains(OpenDocumentResolver.Spell(r.Path, evidence) ?? r.Path)).ToList())
        {
            HeldFiles = held,
            MappedDrives = drives,
        };
        var scan = OpenDocumentResolver.Resolve(kept, inUse);

        DiagnosticLog.Info($"Document scan: {windows.Count} window(s), {held.Count} held, {recents.Count} recent, {inUse.Count} in use, " +
                           $"{scan.Candidates.Count(c => c.IsLikelyOpen)} judged open, {scan.UnmatchedTitles.Count} unmatched, {clock.ElapsedMilliseconds} ms.");

        return warnings.Count == 0 ? scan : scan with { Warning = string.Join(" ", warnings) };
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

    /// <summary>
    /// What a slow answer is blamed on, so the volume's other files are skipped:
    /// the server for a UNC path or a mapped drive (candidates are spelled with
    /// mapped drive letters, which hide the server), else the drive ("E:") if
    /// it is a network or removable one that isn't mapped. Null for anything
    /// else: a fixed local drive is fast and one slow file there says nothing
    /// about the rest (a locked or damaged file), so only that file is skipped.
    /// </summary>
    private static string? SlowKeyOf(string path, IReadOnlyDictionary<string, string> drives)
    {
        var share = path;
        if (!path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            if (path.Length < 2 || path[1] != ':')
                return null; // Not a path we know how to group.

            var drive = path[..2];
            if (!drives.TryGetValue(drive, out share!))
            {
                try
                {
                    var type = new DriveInfo(drive).DriveType;
                    return type is DriveType.Network or DriveType.Removable ? drive : null;
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }

        var end = share.IndexOf('\\', 2);
        return end < 0 ? share[2..] : share[2..end];
    }

    // ---------------------------------------------------------------
    // Interop
    // ---------------------------------------------------------------

    private const int RmSessionKeyLength = 32;
    private const int ErrorMoreData = 234;

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
}
