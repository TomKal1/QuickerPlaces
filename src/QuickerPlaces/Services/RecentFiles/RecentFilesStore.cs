using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using QuickerPlaces.Models;
using QuickerPlaces.Models.RecentFiles;
using QuickerPlaces.Services.Activity;
using QuickerPlaces.Services.Documents;
using QuickerPlaces.Services.History;

namespace QuickerPlaces.Services.RecentFiles;

/// <summary>
/// Owns recent-files.json (documents plan §5): Recent Files tracking's
/// settings and the PDF, Word and Excel opens it has recorded. It is to
/// files what ActivityStore is to folders, and separate from saved sessions
/// in the same way Recents is separate from saved places.
///
/// - Off until turned on, and nothing opened while it was off is ever
///   recorded (<see cref="RecentFilesSettings.ResumedAt"/>), as nothing is
///   recorded in Recents before a folder is added.
/// - An open is recorded once: an observation counts only when it is later
///   than the file's last recorded open, so reading the same Recent Items
///   every minute adds nothing new.
/// - Opens are buffered in memory and written by <see cref="Flush"/>, like
///   folder activity; settings and deletions are written at once, and a
///   failed write is returned to be shown.
/// - Kept <see cref="RetentionDays"/> days, pruned at load and in the first
///   flush of each new local day. Before pruning, every open still held is
///   handed to the activity history (history plan §4), which keeps it
///   forever; if that fails, nothing is pruned that day. Forgetting a file,
///   or all of them, clears this list only: the history is kept (history plan H5). Each file keeps at most
///   <see cref="MaxOpensPerFile"/> opens.
/// - Loading goes through <see cref="JsonStoreLoader"/> and is handled as
///   activity.json's is: a damaged file is set aside and tracking restarts
///   empty; an unreadable or newer one is left untouched and tracking stays
///   off this session.
///
/// Every public member takes one lock, so the tracking timer can record and
/// flush while the Library window reads. UI-free and linked into the test project.
/// </summary>
public sealed class RecentFilesStore
{
    public const int CurrentSchemaVersion = 1;

    /// <summary>Days of opens kept, today included: a full year for the year view.</summary>
    public const int RetentionDays = 365;

    public const int MaxOpensPerFile = 500;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly object _sync = new();
    private readonly IPlacesStorage _storage;
    private readonly TimeProvider _time;
    private readonly RecentFilesSettings _settings;
    private readonly Dictionary<string, RecentFileRecord> _files;
    private readonly IFileHistory? _history;
    private DateOnly _prunedOn;
    private bool _hasUnsavedChanges;

    /// <param name="history">Where opens go before they are pruned, or null to keep no history (the tests, and qp, which only reads).</param>
    public RecentFilesStore(IPlacesStorage storage, TimeProvider timeProvider, IFileHistory? history = null)
    {
        _storage = storage;
        _time = timeProvider;
        _history = history;

        var (document, outcome) = JsonStoreLoader.Load<RecentFilesDocument>(storage, CurrentSchemaVersion, "files", "Recent files store", JsonOptions);
        LoadOutcome = outcome;
        _settings = Normalize(document?.Settings);
        _files = Normalize(document?.Files);

        switch (outcome)
        {
            case StoreLoadOutcome.Ok:
            case StoreLoadOutcome.NotPresent:
                IsAvailable = true;
                if (outcome == StoreLoadOutcome.Ok)
                    DiagnosticLog.Info($"Loaded {_files.Count} recent file(s) from {_storage.StoreFilePath}.");
                Prune(Today());
                break;

            case StoreLoadOutcome.Damaged:
                try
                {
                    var kept = _storage.Quarantine(_time.GetLocalNow());
                    DiagnosticLog.Warn($"Quarantined damaged recent files store to {kept}; Recent Files restarts empty and off.");
                    IsAvailable = true;
                    _prunedOn = Today();
                    Notice = $"Your recent files couldn't be read and were reset, and Recent Files tracking is off until you turn it on again. The damaged file was kept as \"{kept}\".";
                }
                catch (Exception ex)
                {
                    DiagnosticLog.Error($"Failed to quarantine damaged recent files store at {_storage.StoreFilePath}; Recent Files is off for this session.", ex);
                    Notice = $"Your recent files couldn't be read, and the damaged file couldn't be set aside, so Recent Files is off. The file is \"{_storage.StoreFilePath}\".";
                }

                break;

            case StoreLoadOutcome.Unreadable:
                Notice = $"Your recent files couldn't be opened, so Recent Files is off until QuickerPlaces restarts. The file is \"{_storage.StoreFilePath}\".";
                break;

            case StoreLoadOutcome.WrittenByNewerVersion:
                Notice = "Your recent files were saved by a newer version of QuickerPlaces, so Recent Files is off. Update QuickerPlaces to use them.";
                break;
        }
    }

    /// <summary>recent-files.json in %LocalAppData%, beside activity.json: machine-local, like folder tracking.</summary>
    public static RecentFilesStore CreateDefault(IFileHistory? history = null)
    {
        return new RecentFilesStore(new FilePlacesStorage(AppDataFolders.Local, "recent-files.json"), TimeProvider.System, history);
    }

    public StoreLoadOutcome LoadOutcome { get; }

    /// <summary>False when the file couldn't be opened, came from a newer version, or was damaged and couldn't be set aside: nothing is recorded or written.</summary>
    public bool IsAvailable { get; }

    /// <summary>One line about loading, or null.</summary>
    public string? Notice { get; }

    public bool HasUnsavedChanges
    {
        get { lock (_sync) return _hasUnsavedChanges; }
    }

    /// <summary>True when tracking is on and the store can record.</summary>
    public bool IsTracking
    {
        get { lock (_sync) return IsAvailable && _settings.Enabled; }
    }

    public RecentFilesSettingsSnapshot Settings
    {
        get
        {
            lock (_sync)
                return new RecentFilesSettingsSnapshot(_settings.Enabled, _settings.Kinds.ToArray(), _settings.Scope,
                    _settings.TrackingStartedAt, _settings.ResumedAt);
        }
    }

    /// <summary>Turns tracking on or off, keeping what was recorded. Turning it on starts from now. Saved at once.</summary>
    public PersistenceResult SetEnabled(bool enabled)
    {
        lock (_sync)
        {
            if (!IsAvailable)
                return PersistenceResult.Fail(Notice!);
            if (_settings.Enabled == enabled)
                return PersistenceResult.Ok();

            _settings.Enabled = enabled;
            if (enabled)
            {
                var now = _time.GetUtcNow();
                _settings.TrackingStartedAt ??= now;
                _settings.ResumedAt = now;
            }

            return SaveNow();
        }
    }

    /// <summary>Chooses which kinds are recorded from now on; at least one. Opens already recorded are kept. Saved at once.</summary>
    public ValidationResult TrySetKinds(IEnumerable<DocumentKind> kinds, out PersistenceResult persistence)
    {
        lock (_sync)
        {
            persistence = PersistenceResult.Ok();
            if (!IsAvailable)
                return ValidationResult.Fail(Notice!);

            var chosen = DocumentKinds.All.Where(kinds.Contains).ToList();
            if (chosen.Count == 0)
                return ValidationResult.Fail("Choose at least one kind of file to track, or turn Recent Files off.");

            _settings.Kinds = chosen;
            persistence = SaveNow();
            return ValidationResult.Ok();
        }
    }

    /// <summary>Chooses where files are recorded from from now on. Saved at once.</summary>
    public PersistenceResult SetScope(RecentFilesScope scope)
    {
        lock (_sync)
        {
            if (!IsAvailable)
                return PersistenceResult.Fail(Notice!);

            _settings.Scope = scope;
            return SaveNow();
        }
    }

    /// <summary>
    /// Adds the opens in <paramref name="observations"/> that are new: of a
    /// tracked kind, in scope (<paramref name="inScope"/>, given the path),
    /// since tracking was last turned on, and later than the file's last
    /// recorded open. Buffers; <see cref="Flush"/> writes. Returns how many
    /// opens were added.
    /// </summary>
    public int Record(IEnumerable<RecentDocument> observations, Func<string, bool> inScope)
    {
        lock (_sync)
        {
            if (!IsAvailable || !_settings.Enabled || _settings.ResumedAt is not { } resumedAt)
                return 0;

            var added = 0;
            foreach (var observation in observations)
            {
                var path = DocumentPaths.Normalize(observation.Path);
                if (path is null || DocumentKinds.FromPath(path) is not { } kind || !_settings.Kinds.Contains(kind))
                    continue;
                if (observation.LastOpenedAt < resumedAt || !inScope(path))
                    continue;

                if (!_files.TryGetValue(path, out var record))
                    _files[path] = record = new RecentFileRecord { Path = path };
                if (record.Opens.Count > 0 && observation.LastOpenedAt <= record.Opens[^1])
                    continue;

                record.Opens.Add(observation.LastOpenedAt.ToUniversalTime());
                if (record.Opens.Count > MaxOpensPerFile)
                    record.Opens.RemoveRange(0, record.Opens.Count - MaxOpensPerFile);
                added++;
            }

            if (added > 0)
                _hasUnsavedChanges = true;
            return added;
        }
    }

    /// <summary>Writes buffered opens, first applying retention if the local day has turned. A failure is returned and kept for the next flush.</summary>
    public PersistenceResult Flush()
    {
        lock (_sync)
        {
            if (!IsAvailable)
                return PersistenceResult.Ok();

            var today = Today();
            if (today != _prunedOn && Prune(today))
                _hasUnsavedChanges = true;

            return _hasUnsavedChanges ? SaveNow() : PersistenceResult.Ok();
        }
    }

    /// <summary>Forgets one file and all its opens. Saved at once.</summary>
    public PersistenceResult Forget(string path)
    {
        lock (_sync)
        {
            if (!IsAvailable)
                return PersistenceResult.Fail(Notice!);

            var key = DocumentPaths.Normalize(path) ?? path;
            return _files.Remove(key) ? SaveNow() : PersistenceResult.Ok();
        }
    }

    /// <summary>Deletes every recorded open, keeping the settings. Saved at once.</summary>
    public PersistenceResult ClearHistory()
    {
        lock (_sync)
        {
            if (!IsAvailable)
                return PersistenceResult.Fail(Notice!);

            _files.Clear();
            return SaveNow();
        }
    }

    /// <summary>
    /// Each file opened between the local dates <paramref name="from"/> and
    /// <paramref name="to"/> inclusive (all kept history when null), of the
    /// given kinds (all when null), most recently opened first.
    /// </summary>
    public IReadOnlyList<RecentFileSummary> QueryFiles(DateOnly? from = null, DateOnly? to = null, IReadOnlyCollection<DocumentKind>? kinds = null)
    {
        lock (_sync)
        {
            var rows = new List<RecentFileSummary>();
            foreach (var record in _files.Values)
            {
                if (DocumentKinds.FromPath(record.Path) is not { } kind || (kinds is not null && !kinds.Contains(kind)))
                    continue;

                var opens = record.Opens.Where(o => InRange(LocalDate(o), from, to)).ToList();
                if (opens.Count > 0)
                    rows.Add(new RecentFileSummary(record.Path, kind, opens.Count, opens[^1]));
            }

            return rows.OrderByDescending(r => r.LastOpenedAt).ThenBy(r => r.Path, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    /// <summary>
    /// Every recorded file with each of its kept opens, oldest first: what
    /// the Library reads to count opens per day for just the files that pass
    /// its filters. Files of a kind that can't be told from the path are left out.
    /// </summary>
    public IReadOnlyList<RecentFileHistory> QueryHistory()
    {
        lock (_sync)
        {
            var rows = new List<RecentFileHistory>();
            foreach (var record in _files.Values)
            {
                if (DocumentKinds.FromPath(record.Path) is { } kind && record.Opens.Count > 0)
                    rows.Add(new RecentFileHistory(record.Path, kind, record.Opens.ToArray()));
            }

            return rows;
        }
    }

    /// <summary>Opens and distinct files per local day, of the given kinds (all when null), for the year view.</summary>
    public IReadOnlyDictionary<DateOnly, RecentFileDayTotal> QueryDayTotals(IReadOnlyCollection<DocumentKind>? kinds = null)
    {
        lock (_sync)
        {
            var opens = new Dictionary<DateOnly, (int Opens, HashSet<string> Files)>();
            foreach (var record in _files.Values)
            {
                if (DocumentKinds.FromPath(record.Path) is not { } kind || (kinds is not null && !kinds.Contains(kind)))
                    continue;

                foreach (var open in record.Opens)
                {
                    var date = LocalDate(open);
                    if (!opens.TryGetValue(date, out var day))
                        opens[date] = day = (0, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                    day.Files.Add(record.Path);
                    opens[date] = (day.Opens + 1, day.Files);
                }
            }

            return opens.ToDictionary(d => d.Key, d => new RecentFileDayTotal(d.Value.Opens, d.Value.Files.Count));
        }
    }

    /// <summary>The local date of <paramref name="instant"/> in the store's zone.</summary>
    public DateOnly LocalDate(DateTimeOffset instant)
        => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, _time.LocalTimeZone).DateTime);

    /// <summary>Today in the store's zone.</summary>
    public DateOnly Today() => LocalDate(_time.GetUtcNow());

    /// <summary>
    /// Whether <paramref name="path"/> may be recorded under <paramref name="scope"/>:
    /// anywhere, or only under one of <paramref name="roots"/> (the folders
    /// tracked in Recents, with their equivalent prefixes).
    /// </summary>
    public static bool IsInScope(string path, RecentFilesScope scope, IEnumerable<TrackedRootConfig> roots)
        => scope == RecentFilesScope.Everywhere || roots.Any(root => RootPathMatcher.Credit(path, root) is not null);

    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------

    private static bool InRange(DateOnly date, DateOnly? from, DateOnly? to)
        => (from is null || date >= from) && (to is null || date <= to);

    private PersistenceResult SaveNow()
    {
        _hasUnsavedChanges = true;
        try
        {
            var document = new RecentFilesDocument
            {
                SchemaVersion = CurrentSchemaVersion,
                Settings = _settings,
                Files = _files.Values.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase).ToList(),
            };
            _storage.Write(JsonSerializer.Serialize(document, JsonOptions));
            _hasUnsavedChanges = false;
            return PersistenceResult.Ok();
        }
        catch (Exception ex)
        {
            // Counts and the file path only, never a document's path.
            DiagnosticLog.Error($"Failed to save {_files.Count} recent file(s) to {_storage.StoreFilePath}; kept in memory for the next save.", ex);
            return PersistenceResult.Fail($"Couldn't save your recent files to \"{_storage.StoreFilePath}\". {ex.Message} QuickerPlaces will try again when it next saves.");
        }
    }

    /// <summary>Drops opens older than the retention window, and files left with none; true if anything went.</summary>
    private bool Prune(DateOnly today)
    {
        _prunedOn = today;
        if (_history is not null && _files.Count > 0 && !_history.SaveFiles(_files.Values))
        {
            DiagnosticLog.Warn("Activity history couldn't be saved, so no recent files were deleted today.");
            return false;
        }

        var keepFrom = today.AddDays(-(RetentionDays - 1));
        var pruned = false;
        foreach (var (path, record) in _files.ToList())
        {
            pruned |= record.Opens.RemoveAll(o => LocalDate(o) < keepFrom) > 0;
            if (record.Opens.Count == 0)
                pruned |= _files.Remove(path);
        }

        return pruned;
    }

    private static RecentFilesSettings Normalize(RecentFilesSettings? loaded)
    {
        var settings = loaded ?? new RecentFilesSettings();
        settings.Kinds = DocumentKinds.All.Where(k => settings.Kinds?.Contains(k) == true).ToList();
        if (settings.Kinds.Count == 0)
            settings.Kinds = new List<DocumentKind>(DocumentKinds.All);
        if (!Enum.IsDefined(settings.Scope))
            settings.Scope = RecentFilesScope.TrackedFolders;
        if (settings.Enabled && settings.ResumedAt is null)
            settings.Enabled = false;
        return settings;
    }

    /// <summary>Drops null and unusable entries, merges repeats of a path ignoring case, and sorts each file's opens.</summary>
    private static Dictionary<string, RecentFileRecord> Normalize(IEnumerable<RecentFileRecord?>? loaded)
    {
        var files = new Dictionary<string, RecentFileRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in loaded ?? Array.Empty<RecentFileRecord?>())
        {
            if (record is null || DocumentPaths.Normalize(record.Path) is not { } path)
                continue;

            if (!files.TryGetValue(path, out var existing))
                files[path] = existing = new RecentFileRecord { Path = path };
            existing.Opens.AddRange(record.Opens ?? new List<DateTimeOffset>());
        }

        foreach (var record in files.Values)
        {
            var opens = record.Opens.Select(o => o.ToUniversalTime()).Distinct().OrderBy(o => o).ToList();
            record.Opens = opens.Count > MaxOpensPerFile ? opens.GetRange(opens.Count - MaxOpensPerFile, MaxOpensPerFile) : opens;
        }

        return files;
    }
}

public sealed record RecentFilesSettingsSnapshot(bool Enabled, IReadOnlyList<DocumentKind> Kinds, RecentFilesScope Scope,
    DateTimeOffset? TrackingStartedAt, DateTimeOffset? ResumedAt);

/// <summary>One file in a period: how many times it was opened in it, and when last.</summary>
public sealed record RecentFileSummary(string Path, DocumentKind Kind, int Opens, DateTimeOffset LastOpenedAt);

/// <summary>One day's opens, for the year view.</summary>
public sealed record RecentFileDayTotal(int Opens, int Files);

/// <summary>One recorded file and every open of it that is kept, oldest first.</summary>
public sealed record RecentFileHistory(string Path, DocumentKind Kind, IReadOnlyList<DateTimeOffset> Opens);
