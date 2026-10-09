using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using QuickerPlaces.Models.Activity;
using QuickerPlaces.Models.History;
using QuickerPlaces.Models.RecentFiles;
using QuickerPlaces.Services.Activity;

namespace QuickerPlaces.Services.History;

/// <summary>What ActivityStore hands its days to before it deletes any (history plan §4).</summary>
public interface IFolderHistory
{
    /// <summary>
    /// Saves every day <paramref name="roots"/> hold into the month files,
    /// replacing those days and leaving every other day as it was. False when
    /// any month couldn't be saved: the caller then keeps its data.
    /// </summary>
    bool SaveFolders(IReadOnlyCollection<TrackedRoot> roots);
}

/// <summary>What RecentFilesStore hands its opens to before it deletes any (history plan §4).</summary>
public interface IFileHistory
{
    /// <summary>Adds every open in <paramref name="files"/> to the month files. False when any month couldn't be saved.</summary>
    bool SaveFiles(IReadOnlyCollection<RecentFileRecord> files);
}

/// <summary>Reads the month files back (history plan §5). ActivityHistory implements it; the views reach it through ActivityStore.HistoryReader.</summary>
public interface IHistoryReader
{
    /// <summary>Every month any PC has a file for, oldest first, and whose files they are. Read from the file names only.</summary>
    IReadOnlyList<HistoryMonthInfo> MonthIndex();

    /// <summary>
    /// A month as every PC recorded it, merged, or null when there is none.
    /// With <paramref name="ownHeldFrom"/>, this PC's own days from those
    /// dates on are left out, because the working stores still hold them and
    /// are the newer copy.
    /// </summary>
    HistoryMonthDocument? ReadMonth(int year, int month, HistoryCutoffs? ownHeldFrom = null);
}

/// <summary>One month in the history folder: whether this PC and other PCs have a file for it.</summary>
public sealed record HistoryMonthInfo(int Year, int Month, bool HasOwn, bool HasOthers)
{
    public DateOnly First => new(Year, Month, 1);

    public DateOnly Last => First.AddMonths(1).AddDays(-1);
}

/// <summary>
/// The first local dates this PC's working stores still hold: folder detail,
/// day totals and file opens. On and after them the stores are read; before
/// them, the month files.
/// </summary>
public sealed record HistoryCutoffs(DateOnly DetailFrom, DateOnly TotalsFrom, DateOnly FilesFrom)
{
    /// <summary>
    /// The cutoffs for <paramref name="today"/> by the stores' own windows.
    /// Recent Files keeps each open as long as Recents keeps folder detail
    /// (RecentFilesStore.DetailDays is ActivityStore.DetailDays), so this file
    /// needs no reference to Recent Files.
    /// </summary>
    public static HistoryCutoffs For(DateOnly today)
        => new(today.AddDays(-(ActivityStore.DetailDays - 1)),
            today.AddDays(-(ActivityStore.TotalDays - 1)),
            today.AddDays(-(ActivityStore.DetailDays - 1)));

    /// <summary>The latest of the three: before it, some of this PC's data is only in the month files.</summary>
    public DateOnly Latest => new[] { DetailFrom, TotalsFrom, FilesFrom }.Max();
}

/// <summary>
/// Keeps folder and file activity forever, one JSON file per month per PC,
/// in Documents\QuickerPlaces\History (history plan). activity.json and
/// recent-files.json stay small: before either deletes a day, and once a day
/// besides, it hands everything it still holds to this class, which writes
/// it into the month files. So a day is in a month file long before it
/// leaves the working store, and a lost PC loses at most a day.
///
/// - Saving only adds or replaces the days it is given; nothing here ever
///   deletes history (history plan H5): stopping tracking, or clearing
///   Recent Files, leaves past months as they are.
/// - A month file is written only when its content changed.
/// - A file this PC wrote that can't be read is set aside, never written
///   over; one from a newer version is left alone and the caller keeps its data.
/// - Messages in the log name counts and file names only, never a folder
///   or a document.
///
/// One lock: the folder tracker's thread and Recent Files' thread both save
/// here. UI-free and linked into the test project.
/// </summary>
public sealed class ActivityHistory : IFolderHistory, IFileHistory, IHistoryReader
{
    public const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly Regex FileNamePattern = new(@"^(\d{4})-(\d{2}) \((.+)\)\.json$", RegexOptions.CultureInvariant);

    private readonly object _sync = new();
    private readonly IHistoryFolder _folder;
    private readonly TimeProvider _time;

    /// <summary>Each of this PC's month files as last read or written, by name: saves compare against it instead of reading again. Only this class writes them.</summary>
    private readonly Dictionary<string, (HistoryMonthDocument Document, string Text)> _cache = new(StringComparer.OrdinalIgnoreCase);

    public ActivityHistory(IHistoryFolder folder, TimeProvider time, string machine)
    {
        _folder = folder;
        _time = time;
        Machine = MachineLabel(machine);
    }

    /// <summary>The history in Documents\QuickerPlaces\History, written as this PC.</summary>
    public static ActivityHistory CreateDefault()
        => new(new HistoryFolder(AppDataFolders.History), TimeProvider.System, Environment.MachineName);

    /// <summary>This PC's name as it appears in file names.</summary>
    public string Machine { get; }

    public string FolderPath => _folder.FolderPath;

    /// <summary>The file this PC writes for a month: "2026-09 (DESKTOP-ABC).json".</summary>
    public string FileName(int year, int month) => $"{year:D4}-{month:D2} ({Machine}).json";

    // ---------------------------------------------------------------
    // Saving
    // ---------------------------------------------------------------

    public bool SaveFolders(IReadOnlyCollection<TrackedRoot> roots)
    {
        lock (_sync)
        {
            var months = new SortedDictionary<(int Year, int Month), List<(TrackedRoot Root, DateOnly Date)>>();
            foreach (var root in roots)
            {
                foreach (var date in root.Days.Keys.Union(root.DayTotals.Keys))
                {
                    if (!months.TryGetValue((date.Year, date.Month), out var dates))
                        months[(date.Year, date.Month)] = dates = new List<(TrackedRoot, DateOnly)>();
                    dates.Add((root, date));
                }
            }

            var saved = true;
            foreach (var ((year, month), entries) in months)
            {
                saved &= Update(year, month, document =>
                {
                    foreach (var (root, date) in entries)
                    {
                        var target = document.Roots.Find(r => SamePath(r.Path, root.Path));
                        if (target is null)
                            document.Roots.Add(target = new HistoryRoot { Path = root.Path });

                        if (root.Days.TryGetValue(date, out var day))
                            target.Days[date] = Copy(day);
                        if (root.DayTotals.TryGetValue(date, out var total))
                            target.Totals[date] = new DayTotal { Milliseconds = total.Milliseconds, Visits = total.Visits, Folders = total.Folders };
                    }
                });
            }

            return saved;
        }
    }

    public bool SaveFiles(IReadOnlyCollection<RecentFileRecord> files)
    {
        lock (_sync)
        {
            var months = new SortedDictionary<(int Year, int Month), List<(string Path, DateTimeOffset Open)>>();
            foreach (var file in files)
            {
                foreach (var open in file.Opens)
                {
                    var date = LocalDate(open);
                    if (!months.TryGetValue((date.Year, date.Month), out var opens))
                        months[(date.Year, date.Month)] = opens = new List<(string, DateTimeOffset)>();
                    opens.Add((file.Path, open.ToUniversalTime()));
                }
            }

            var saved = true;
            foreach (var ((year, month), opens) in months)
            {
                saved &= Update(year, month, document =>
                {
                    foreach (var group in opens.GroupBy(o => o.Path, StringComparer.OrdinalIgnoreCase))
                    {
                        var target = document.Files.Find(f => SamePath(f.Path, group.Key));
                        if (target is null)
                            document.Files.Add(target = new HistoryFile { Path = group.Key });

                        target.Opens = target.Opens.Concat(group.Select(o => o.Open)).Distinct().OrderBy(o => o).ToList();
                    }

                    document.Files.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Path, b.Path));
                });
            }

            return saved;
        }
    }

    /// <summary>
    /// Reads this PC's file for the month, applies <paramref name="change"/>,
    /// and writes it if the text changed. False when it couldn't be read as
    /// history of this version, or couldn't be written.
    /// </summary>
    private bool Update(int year, int month, Action<HistoryMonthDocument> change)
    {
        var name = FileName(year, month);
        if (!TryLoadOwn(name, year, month, out var document, out var text))
            return false;

        change(document);
        var updated = JsonSerializer.Serialize(document, JsonOptions);
        if (updated == text)
            return true;

        try
        {
            _folder.Write(name, updated);
            _cache[name] = (document, updated);
            return true;
        }
        catch (Exception ex)
        {
            // The file name only: it names a month and this PC, never a folder or a document.
            DiagnosticLog.Error($"Couldn't save activity history to {name}; the data is kept for the next try.", ex);
            _cache.Remove(name);
            return false;
        }
    }

    /// <summary>This PC's month file from the cache or the folder, or a new one; false when it must not be written over.</summary>
    private bool TryLoadOwn(string name, int year, int month, out HistoryMonthDocument document, out string text)
    {
        if (_cache.TryGetValue(name, out var cached))
        {
            document = cached.Document;
            text = cached.Text;
            return true;
        }

        document = new HistoryMonthDocument { SchemaVersion = CurrentSchemaVersion, Month = $"{year:D4}-{month:D2}", Machine = Machine };
        text = "";

        string? existing;
        try
        {
            existing = _folder.Read(name);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Error($"Couldn't read activity history {name}; it is left as it is and the data kept for the next try.", ex);
            return false;
        }

        if (existing is null)
            return true;

        switch (Parse(existing, out var loaded))
        {
            case ParseOutcome.Ok:
                document = loaded!;
                document.Month = $"{year:D4}-{month:D2}";
                document.Machine = Machine;
                text = existing;
                _cache[name] = (document, text);
                return true;

            case ParseOutcome.Newer:
                DiagnosticLog.Warn($"Activity history {name} was written by a newer version of {AppInfo.Name}; it is left as it is.");
                return false;

            default:
                try
                {
                    var kept = _folder.SetAside(name, _time.GetLocalNow());
                    DiagnosticLog.Warn($"Activity history {name} couldn't be read; it was kept as {kept} and the month starts again.");
                    return true;
                }
                catch (Exception ex)
                {
                    DiagnosticLog.Error($"Activity history {name} couldn't be read or set aside; it is left as it is.", ex);
                    return false;
                }
        }
    }

    // ---------------------------------------------------------------
    // Reading
    // ---------------------------------------------------------------

    /// <summary>
    /// A month as every PC recorded it, merged: a folder's time and visits on
    /// a day are added across PCs, and a file's opens are joined. Null when
    /// no PC has a readable file for the month. Read fresh each call; the
    /// caller decides how long to keep it (HistoryMonthCache).
    /// </summary>
    public HistoryMonthDocument? ReadMonth(int year, int month, HistoryCutoffs? ownHeldFrom = null)
    {
        var prefix = $"{year:D4}-{month:D2} (";
        HistoryMonthDocument? merged = null;
        foreach (var name in MonthFiles().Where(n => n.StartsWith(prefix, StringComparison.Ordinal)))
        {
            HistoryMonthDocument? document;
            try
            {
                if (_folder.Read(name) is not { } text || Parse(text, out document) != ParseOutcome.Ok)
                    continue;
            }
            catch (Exception ex)
            {
                DiagnosticLog.Warn($"Couldn't read activity history {name} ({ex.GetType().Name}).");
                continue;
            }

            if (ownHeldFrom is not null && IsOwn(name))
                LeaveOutHeld(document!, ownHeldFrom);

            merged ??= new HistoryMonthDocument { SchemaVersion = CurrentSchemaVersion, Month = $"{year:D4}-{month:D2}" };
            MergeInto(merged, document!);
        }

        return merged;
    }

    /// <summary>The months any PC has history for, oldest first.</summary>
    public IReadOnlyList<(int Year, int Month)> Months() => MonthIndex().Select(m => (m.Year, m.Month)).ToList();

    public IReadOnlyList<HistoryMonthInfo> MonthIndex()
    {
        var months = new SortedDictionary<(int Year, int Month), (bool Own, bool Others)>();
        foreach (var name in MonthFiles())
        {
            var match = FileNamePattern.Match(name);
            var key = (int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture));
            if (key.Item2 is < 1 or > 12 || key.Item1 < 1)
                continue;

            var own = IsOwn(name);
            months.TryGetValue(key, out var seen);
            months[key] = (seen.Own || own, seen.Others || !own);
        }

        return months.Select(m => new HistoryMonthInfo(m.Key.Year, m.Key.Month, m.Value.Own, m.Value.Others)).ToList();
    }

    private bool IsOwn(string name)
        => string.Equals(FileNamePattern.Match(name).Groups[3].Value, Machine, StringComparison.OrdinalIgnoreCase);

    /// <summary>Drops this PC's days the working stores still hold, so they are never counted twice.</summary>
    private void LeaveOutHeld(HistoryMonthDocument document, HistoryCutoffs held)
    {
        foreach (var root in document.Roots)
        {
            foreach (var date in root.Days.Keys.Where(d => d >= held.DetailFrom).ToList())
                root.Days.Remove(date);
            foreach (var date in root.Totals.Keys.Where(d => d >= held.TotalsFrom).ToList())
                root.Totals.Remove(date);
        }

        foreach (var file in document.Files)
            file.Opens.RemoveAll(o => LocalDate(o) >= held.FilesFrom);
        document.Files.RemoveAll(f => f.Opens.Count == 0);
    }

    private static void MergeInto(HistoryMonthDocument into, HistoryMonthDocument from)
    {
        foreach (var root in from.Roots)
        {
            var target = into.Roots.Find(r => SamePath(r.Path, root.Path));
            if (target is null)
                into.Roots.Add(target = new HistoryRoot { Path = root.Path });

            foreach (var (date, day) in root.Days)
            {
                if (!target.Days.TryGetValue(date, out var existing))
                    target.Days[date] = existing = new DayActivity();
                foreach (var (folder, total) in day.Folders)
                    Add(existing.Folders, folder, total);
            }

            foreach (var (date, total) in root.Totals)
            {
                if (!target.Totals.TryGetValue(date, out var existing))
                    target.Totals[date] = existing = new DayTotal();
                existing.Milliseconds += total.Milliseconds;
                existing.Visits += total.Visits;
                // Two PCs can count the same folder, so the merged count comes from the merged folders when they exist.
                existing.Folders = target.Days.TryGetValue(date, out var detail) && detail.Folders.Count > 0
                    ? detail.Folders.Count
                    : Math.Max(existing.Folders, total.Folders);
            }
        }

        foreach (var file in from.Files)
        {
            var target = into.Files.Find(f => SamePath(f.Path, file.Path));
            if (target is null)
                into.Files.Add(target = new HistoryFile { Path = file.Path });
            target.Opens = target.Opens.Concat(file.Opens).Distinct().OrderBy(o => o).ToList();
        }
    }

    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------

    private enum ParseOutcome { Ok, Newer, Unreadable }

    /// <summary>Reads a month file as untrusted text: anything that isn't history of a known version is refused.</summary>
    private static ParseOutcome Parse(string text, out HistoryMonthDocument? document)
    {
        document = null;
        try
        {
            document = JsonSerializer.Deserialize<HistoryMonthDocument>(text, JsonOptions);
        }
        catch (JsonException)
        {
            return ParseOutcome.Unreadable;
        }

        if (document is null || document.SchemaVersion < 1)
            return ParseOutcome.Unreadable;
        if (document.SchemaVersion > CurrentSchemaVersion)
            return ParseOutcome.Newer;

        document.Roots = (document.Roots ?? new List<HistoryRoot>()).Where(r => r is not null && !string.IsNullOrWhiteSpace(r.Path)).ToList();
        foreach (var root in document.Roots)
        {
            // Deserializing gives a case-sensitive map; folders are compared ignoring case, as in activity.json.
            root.Days = (root.Days ?? new Dictionary<DateOnly, DayActivity>())
                .Where(d => d.Value is not null)
                .ToDictionary(d => d.Key, d => Copy(d.Value));
            root.Totals = (root.Totals ?? new Dictionary<DateOnly, DayTotal>())
                .Where(d => d.Value is not null)
                .ToDictionary(d => d.Key, d => d.Value);
        }

        document.Files = (document.Files ?? new List<HistoryFile>()).Where(f => f is not null && !string.IsNullOrWhiteSpace(f.Path)).ToList();
        foreach (var file in document.Files)
            file.Opens = (file.Opens ?? new List<DateTimeOffset>()).Select(o => o.ToUniversalTime()).Distinct().OrderBy(o => o).ToList();

        return ParseOutcome.Ok;
    }

    private IEnumerable<string> MonthFiles()
    {
        IReadOnlyList<string> names;
        try
        {
            names = _folder.List();
        }
        catch (Exception ex)
        {
            DiagnosticLog.Warn($"Couldn't list the activity history folder ({ex.GetType().Name}).");
            return Array.Empty<string>();
        }

        return names.Where(n => FileNamePattern.IsMatch(n)).OrderBy(n => n, StringComparer.Ordinal).ToList();
    }

    /// <summary>A copy of a day with a case-insensitive folder map, so a saved day never shares objects with the store.</summary>
    private static DayActivity Copy(DayActivity day)
    {
        var copy = new DayActivity();
        foreach (var (folder, total) in day.Folders ?? new Dictionary<string, FolderTotal>())
        {
            if (total is not null)
                Add(copy.Folders, folder, total);
        }

        return copy;
    }

    private static void Add(Dictionary<string, FolderTotal> into, string folder, FolderTotal total)
    {
        if (!into.TryGetValue(folder, out var existing))
        {
            into[folder] = new FolderTotal { Milliseconds = total.Milliseconds, Visits = total.Visits, LastSeenAt = total.LastSeenAt };
            return;
        }

        existing.Milliseconds += total.Milliseconds;
        existing.Visits += total.Visits;
        if (total.LastSeenAt > existing.LastSeenAt)
            existing.LastSeenAt = total.LastSeenAt;
    }

    private DateOnly LocalDate(DateTimeOffset instant)
        => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, _time.LocalTimeZone).DateTime);

    private static bool SamePath(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>A PC name safe in a file name and unambiguous in the pattern: anything but letters, digits, '-' and '_' becomes '-'.</summary>
    private static string MachineLabel(string machine)
    {
        var label = new string((machine ?? "").Trim().Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? c : '-').ToArray());
        return label.Length == 0 ? "PC" : label;
    }
}
