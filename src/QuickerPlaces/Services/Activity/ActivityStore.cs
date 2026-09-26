using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using QuickerPlaces.Models;
using QuickerPlaces.Models.Activity;

namespace QuickerPlaces.Services.Activity;

/// <summary>
/// Owns activity.json (Phase 9 plan 5.2, 5.3): the tracked roots'
/// configuration and their recorded days, in one machine-local file (D11,
/// D18) at %LocalAppData%, never in places.json and never exported.
///
/// Built over the same IPlacesStorage seam as places.json (D32): pointed at
/// activity.json, FilePlacesStorage gives the same temp-file-and-replace
/// write, an activity.bak.json backup and an activity.corrupt-*.json
/// quarantine, and the tests use the same fake.
///
/// - Configuration changes are written at once, and a failed write is
///   returned to be shown, never swallowed (D18). The change stays in
///   memory and the next write carries it (Phase 1's D1), so a root the
///   user deleted never comes back.
/// - Recorded time goes into the in-memory document at once, so queries see
///   it, and reaches disk only through <see cref="Flush"/> (D10, D36), which
///   the host calls on its timer, on lock and on exit (D26).
/// - Time is held in whole milliseconds, so a day's total is always exactly
///   the sum of its folders (D34).
/// - Folder detail is kept 62 days and day totals 365 (D16, D20), pruned at
///   load and in the first flush of each new local day.
/// - Loading never throws and never asks anything (§7, D33): a damaged file
///   is quarantined and tracking restarts empty, with one line in
///   <see cref="Notice"/>; a file that cannot be opened, or that a newer
///   version wrote, is left untouched and nothing is written this session.
///
/// Every public member takes one lock, so the tracker's thread can record
/// and flush while the Activity window configures and queries (D26).
/// UI-free and linked into the test project.
/// </summary>
public sealed class ActivityStore
{
    /// <summary>The activity.json schema version this build writes and reads.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>Days of per-folder detail kept, today included: the smallest window that always holds a whole previous month.</summary>
    public const int DetailDays = 62;

    /// <summary>Days of per-day totals kept, today included, for the year calendar (D20).</summary>
    public const int TotalDays = 365;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    /// <summary>Duplicate keys are refused at parse time, as places.json's are, so they classify as Damaged.</summary>
    private static readonly JsonDocumentOptions DocumentOptions = new() { AllowDuplicateProperties = false };

    private readonly object _sync = new();
    private readonly IPlacesStorage _storage;
    private readonly TimeProvider _time;
    private readonly List<TrackedRoot> _roots;

    /// <summary>The local date retention was last applied for.</summary>
    private DateOnly _prunedOn;

    public ActivityStore(IPlacesStorage storage, TimeProvider timeProvider)
    {
        _storage = storage;
        _time = timeProvider;

        var (roots, outcome) = Load();
        _roots = roots;
        LoadOutcome = outcome;

        switch (outcome)
        {
            case StoreLoadOutcome.Ok:
            case StoreLoadOutcome.NotPresent:
                IsAvailable = true;
                // In memory only, like places.json's purge: loading never
                // writes, so this reaches disk with the next write.
                Prune(Today());
                break;

            case StoreLoadOutcome.Damaged:
                try
                {
                    var kept = _storage.Quarantine(_time.GetLocalNow());
                    DiagnosticLog.Warn($"Quarantined damaged activity store to {kept}; tracking restarts empty.");
                    IsAvailable = true;
                    _prunedOn = Today();
                    Notice = $"Your tracked folders couldn't be read and were reset. The damaged file was kept as \"{kept}\".";
                }
                catch (Exception ex)
                {
                    // Not set aside, so never written over: tracking stays off.
                    DiagnosticLog.Error($"Failed to quarantine damaged activity store at {_storage.StoreFilePath}; tracking is off for this session.", ex);
                    Notice = $"Your tracked folders couldn't be read, and the damaged file couldn't be set aside, so tracking is off. The file is \"{_storage.StoreFilePath}\".";
                }

                break;

            case StoreLoadOutcome.Unreadable:
                Notice = $"Your tracked folders couldn't be opened, so tracking is off until QuickerPlaces restarts. The file is \"{_storage.StoreFilePath}\".";
                break;

            case StoreLoadOutcome.WrittenByNewerVersion:
                Notice = "Your tracked folders were saved by a newer version of QuickerPlaces, so tracking is off. Update QuickerPlaces to use them.";
                break;
        }
    }

    /// <summary>What happened when activity.json was loaded (the same classification as places.json, Phase 1 D6).</summary>
    public StoreLoadOutcome LoadOutcome { get; }

    /// <summary>
    /// False when activity.json could not be opened, was written by a newer
    /// version, or was damaged and could not be set aside. Then nothing is
    /// recorded or written for the rest of the session, and every change is
    /// refused with <see cref="Notice"/>.
    /// </summary>
    public bool IsAvailable { get; }

    /// <summary>The one line the Activity window shows about loading (D18), or null when there is nothing to say.</summary>
    public string? Notice { get; }

    /// <summary>True while something in memory has not reached disk.</summary>
    public bool HasUnsavedChanges
    {
        get { lock (_sync) return _hasUnsavedChanges; }
    }

    private bool _hasUnsavedChanges;

    /// <summary>Every tracked root, enabled or not, in the order added.</summary>
    public IReadOnlyList<ActivityRootSnapshot> Roots
    {
        get { lock (_sync) return _roots.Select(Snapshot).ToList(); }
    }

    /// <summary>The enabled roots' configurations, for FolderActivityTracker.SetRoots.</summary>
    public IReadOnlyList<TrackedRootConfig> EnabledRoots()
    {
        lock (_sync)
            return _roots.Where(r => r.Enabled).Select(r => Snapshot(r).Config).ToList();
    }

    /// <summary>
    /// Starts tracking <paramref name="path"/>, with the default settings and
    /// the given equivalent prefixes (D22), from now. Refuses a path that is
    /// not a folder on a drive or a share, one already tracked, or an unusable
    /// prefix. An accepted root is saved at once; <paramref name="persistence"/>
    /// says whether that reached disk.
    /// </summary>
    public ValidationResult TryAddRoot(string? path, IReadOnlyList<string>? equivalentPrefixes, out ActivityRootSnapshot? added, out PersistenceResult persistence)
    {
        lock (_sync)
        {
            added = null;
            persistence = PersistenceResult.Ok();
            if (!IsAvailable)
                return ValidationResult.Fail(Notice!);

            var normalized = RootPathMatcher.Normalize(path);
            if (normalized is null)
                return ValidationResult.Fail("Choose a folder on a drive or a network share.");

            if (_roots.Any(r => SamePath(r.Path, normalized)))
                return ValidationResult.Fail("That folder is already tracked.");

            if (!TryNormalizePrefixes(equivalentPrefixes, out var prefixes, out var error))
                return ValidationResult.Fail(error);

            var root = new TrackedRoot
            {
                RootId = NewRootId(),
                Path = normalized,
                EquivalentPrefixes = prefixes,
                TrackingStartedAt = _time.GetUtcNow(),
            };
            _roots.Add(root);

            persistence = SaveNow();
            added = Snapshot(root);
            return ValidationResult.Ok();
        }
    }

    /// <summary>
    /// Changes a root's rollup, depth, thresholds and equivalent prefixes to
    /// those in <paramref name="settings"/>, found by its RootId. Its path
    /// cannot change: every recorded folder is spelled with it (5.2).
    /// Saved at once.
    /// </summary>
    public ValidationResult TryUpdateRoot(TrackedRootConfig settings, out PersistenceResult persistence)
    {
        lock (_sync)
        {
            persistence = PersistenceResult.Ok();
            if (!IsAvailable)
                return ValidationResult.Fail(Notice!);

            var root = Find(settings.RootId);
            if (root is null)
                return ValidationResult.Fail("That folder is no longer tracked.");
            if (!SamePath(root.Path, settings.Path))
                return ValidationResult.Fail("A tracked folder can't be moved. Add the other folder as a root of its own.");
            if (settings.Depth < 1)
                return ValidationResult.Fail("The depth must be at least 1.");
            if (settings.DwellThreshold < TimeSpan.Zero)
                return ValidationResult.Fail("The time before a visit counts can't be negative.");
            if (settings.IdleTimeout < TimeSpan.FromMinutes(1))
                return ValidationResult.Fail("The idle timeout must be at least a minute.");
            if (!TryNormalizePrefixes(settings.EquivalentPrefixes, out var prefixes, out var error))
                return ValidationResult.Fail(error);

            root.Rollup = settings.Rollup;
            root.Depth = settings.Depth;
            root.DwellThresholdSeconds = (int)Math.Round(settings.DwellThreshold.TotalSeconds);
            root.IdleTimeoutMinutes = (int)Math.Round(settings.IdleTimeout.TotalMinutes);
            root.EquivalentPrefixes = prefixes;

            persistence = SaveNow();
            return ValidationResult.Ok();
        }
    }

    /// <summary>Stops or resumes tracking a root, keeping its data (5.5). Saved at once. An unknown root changes nothing.</summary>
    public PersistenceResult SetEnabled(string rootId, bool enabled)
    {
        lock (_sync)
        {
            if (!IsAvailable)
                return PersistenceResult.Fail(Notice!);

            var root = Find(rootId);
            if (root is null)
                return PersistenceResult.Ok();

            root.Enabled = enabled;
            return SaveNow();
        }
    }

    /// <summary>Deletes a root's configuration and every day recorded under it, in one write (§3, D18). An unknown root changes nothing.</summary>
    public PersistenceResult DeleteRoot(string rootId)
    {
        lock (_sync)
        {
            if (!IsAvailable)
                return PersistenceResult.Fail(Notice!);

            var root = Find(rootId);
            if (root is null)
                return PersistenceResult.Ok();

            _roots.Remove(root);
            return SaveNow();
        }
    }

    /// <summary>
    /// Adds the tracker's intervals to the in-memory document, without
    /// writing (D10). Intervals for a root deleted or disabled since the tick
    /// are dropped.
    /// </summary>
    public void Record(IEnumerable<ActivityInterval> intervals)
    {
        lock (_sync)
        {
            if (!IsAvailable)
                return;

            foreach (var interval in intervals)
            {
                var root = Find(interval.RootId);
                if (root is null || !root.Enabled)
                    continue;

                var milliseconds = (long)Math.Round(interval.Duration.TotalMilliseconds, MidpointRounding.AwayFromZero);
                var visits = interval.StartsVisit ? 1 : 0;

                if (!root.Days.TryGetValue(interval.Date, out var day))
                    root.Days[interval.Date] = day = new DayActivity();
                if (!day.Folders.TryGetValue(interval.Folder, out var folder))
                    day.Folders[interval.Folder] = folder = new FolderTotal { LastSeenAt = interval.LastSeenAt };

                folder.Milliseconds += milliseconds;
                folder.Visits += visits;
                if (interval.LastSeenAt > folder.LastSeenAt)
                    folder.LastSeenAt = interval.LastSeenAt;

                if (!root.DayTotals.TryGetValue(interval.Date, out var total))
                    root.DayTotals[interval.Date] = total = new DayTotal();

                total.Milliseconds += milliseconds;
                total.Visits += visits;
                total.Folders = day.Folders.Count;

                _hasUnsavedChanges = true;
            }
        }
    }

    /// <summary>
    /// Writes the document if anything has changed, first applying retention
    /// if the local day has turned since it last was. A failed write is
    /// logged and returned; the data stays in memory for the next flush
    /// (D11, D12). Writes nothing while the store is unavailable.
    /// </summary>
    public PersistenceResult Flush()
    {
        lock (_sync)
        {
            if (!IsAvailable)
                return PersistenceResult.Ok();

            var today = Today();
            if (today != _prunedOn && Prune(today))
                _hasUnsavedChanges = true;

            if (!_hasUnsavedChanges)
                return PersistenceResult.Ok();

            try
            {
                Write();
                return PersistenceResult.Ok();
            }
            catch (Exception ex)
            {
                // D12: counts and the file path only, never a folder.
                DiagnosticLog.Error($"Failed to save folder activity for {_roots.Count} root(s) to {_storage.StoreFilePath}; kept in memory for the next flush.", ex);
                return PersistenceResult.Fail($"Couldn't save folder activity to \"{_storage.StoreFilePath}\". {ex.Message}");
            }
        }
    }

    /// <summary>
    /// A root's folders summed over the local dates <paramref name="from"/> to
    /// <paramref name="to"/> inclusive, most time first, or null for an unknown
    /// root. Only what is still stored is summed; the result says where
    /// tracking and the kept detail begin.
    /// </summary>
    public ActivityPeriod? QueryPeriod(string rootId, DateOnly from, DateOnly to)
    {
        lock (_sync)
        {
            var root = Find(rootId);
            if (root is null)
                return null;

            var rows = new Dictionary<string, FolderTotal>(StringComparer.OrdinalIgnoreCase);
            var spellings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (date, day) in root.Days.OrderBy(d => d.Key))
            {
                if (date < from || date > to)
                    continue;

                foreach (var (folder, total) in day.Folders)
                {
                    spellings.TryAdd(folder, folder);
                    Merge(rows, folder, total);
                }
            }

            var folders = rows
                .Select(r => new FolderActivity(spellings[r.Key], TimeSpan.FromMilliseconds(r.Value.Milliseconds), r.Value.Visits, r.Value.LastSeenAt))
                .OrderByDescending(f => f.Time)
                .ThenBy(f => f.Folder, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new ActivityPeriod(from, to, folders, LocalDate(root.TrackingStartedAt), Today().AddDays(-(DetailDays - 1)));
        }
    }

    /// <summary>A root's stored day totals (up to 365 days), for the calendar (D20), or null for an unknown root.</summary>
    public IReadOnlyDictionary<DateOnly, ActivityDayTotal>? QueryDayTotals(string rootId)
    {
        lock (_sync)
        {
            return Find(rootId)?.DayTotals.ToDictionary(
                d => d.Key,
                d => new ActivityDayTotal(TimeSpan.FromMilliseconds(d.Value.Milliseconds), d.Value.Visits, d.Value.Folders));
        }
    }

    // ---------------------------------------------------------------
    // Disk I/O
    // ---------------------------------------------------------------

    /// <summary>
    /// Loads and classifies activity.json as PlacesService does places.json
    /// (Phase 1 D6): a failure to read is Unreadable, never Damaged, and
    /// only content that was read and makes no sense is Damaged.
    /// </summary>
    private (List<TrackedRoot> roots, StoreLoadOutcome outcome) Load()
    {
        if (!_storage.Exists)
            return (new List<TrackedRoot>(), StoreLoadOutcome.NotPresent);

        string json;
        try
        {
            json = _storage.Read();
        }
        catch (Exception ex)
        {
            DiagnosticLog.Error($"Activity store at {_storage.StoreFilePath} could not be opened; tracking is off for this session.", ex);
            return (new List<TrackedRoot>(), StoreLoadOutcome.Unreadable);
        }

        try
        {
            var root = JsonNode.Parse(json, documentOptions: DocumentOptions) as JsonObject;
            if (root?["schemaVersion"] is not JsonValue versionValue ||
                versionValue.GetValueKind() != JsonValueKind.Number ||
                !versionValue.TryGetValue<int>(out var version) ||
                version < 1)
            {
                DiagnosticLog.Warn($"Activity store at {_storage.StoreFilePath} has no usable schemaVersion; treating as damaged.");
                return (new List<TrackedRoot>(), StoreLoadOutcome.Damaged);
            }

            if (version > CurrentSchemaVersion)
            {
                DiagnosticLog.Warn($"Activity store at {_storage.StoreFilePath} has schemaVersion {version}, newer than this build's {CurrentSchemaVersion}.");
                return (new List<TrackedRoot>(), StoreLoadOutcome.WrittenByNewerVersion);
            }

            var document = root.Deserialize<ActivityDocument>(JsonOptions);
            if (document?.Roots is null)
            {
                DiagnosticLog.Warn($"Activity store at {_storage.StoreFilePath} holds no usable root list; treating as damaged.");
                return (new List<TrackedRoot>(), StoreLoadOutcome.Damaged);
            }

            var roots = Normalize(document.Roots);
            DiagnosticLog.Info($"Loaded {roots.Count} tracked root(s) from {_storage.StoreFilePath}.");
            return (roots, StoreLoadOutcome.Ok);
        }
        catch (Exception ex)
        {
            // Read, but not understood: whatever the exception (a malformed
            // date key or number can surface as more than JsonException),
            // the bytes are intact on disk and quarantine only renames them.
            DiagnosticLog.Error($"Activity store at {_storage.StoreFilePath} is not a valid activity document.", ex);
            return (new List<TrackedRoot>(), StoreLoadOutcome.Damaged);
        }
    }

    /// <summary>
    /// Makes a loaded document safe to use: null entries dropped, missing
    /// collections empty, a missing or repeated root id replaced, and folder
    /// keys that differ only in case merged under a case-insensitive map.
    /// </summary>
    private static List<TrackedRoot> Normalize(IEnumerable<TrackedRoot?> loaded)
    {
        var roots = new List<TrackedRoot>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var root in loaded)
        {
            if (root is null)
                continue;

            if (string.IsNullOrEmpty(root.RootId) || !ids.Add(root.RootId))
                root.RootId = NewRootId(ids);

            root.Path ??= "";
            root.EquivalentPrefixes = (root.EquivalentPrefixes ?? new List<string>()).Where(p => p is not null).ToList();
            root.Days = (root.Days ?? new Dictionary<DateOnly, DayActivity>())
                .Where(d => d.Value is not null)
                .ToDictionary(d => d.Key, d => MergeFolders(d.Value));
            root.DayTotals = (root.DayTotals ?? new Dictionary<DateOnly, DayTotal>())
                .Where(d => d.Value is not null)
                .ToDictionary(d => d.Key, d => d.Value);
            roots.Add(root);
        }

        return roots;
    }

    private static DayActivity MergeFolders(DayActivity day)
    {
        var merged = new DayActivity();
        foreach (var (folder, total) in day.Folders ?? new Dictionary<string, FolderTotal>())
        {
            if (total is not null)
                Merge(merged.Folders, folder, total);
        }

        return merged;
    }

    private static void Merge(Dictionary<string, FolderTotal> into, string folder, FolderTotal total)
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

    /// <summary>Writes a configuration change at once; a failure is returned for the Activity window to show (D18).</summary>
    private PersistenceResult SaveNow()
    {
        _hasUnsavedChanges = true;
        try
        {
            Write();
            return PersistenceResult.Ok();
        }
        catch (Exception ex)
        {
            DiagnosticLog.Error($"Failed to save the configuration of {_roots.Count} tracked root(s) to {_storage.StoreFilePath}; kept in memory for the next save.", ex);
            return PersistenceResult.Fail(
                $"Couldn't save your tracked folders to \"{_storage.StoreFilePath}\". {ex.Message} QuickerPlaces will try again when it next saves.");
        }
    }

    /// <summary>Serializes the whole document and replaces activity.json with it; throws on failure.</summary>
    private void Write()
    {
        var document = new ActivityDocument { SchemaVersion = CurrentSchemaVersion, Roots = _roots };
        _storage.Write(JsonSerializer.Serialize(document, JsonOptions));
        _hasUnsavedChanges = false;
    }

    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------

    /// <summary>Deletes detail and totals older than their windows; true if anything went.</summary>
    private bool Prune(DateOnly today)
    {
        _prunedOn = today;
        var detailFrom = today.AddDays(-(DetailDays - 1));
        var totalsFrom = today.AddDays(-(TotalDays - 1));
        var pruned = false;

        foreach (var root in _roots)
        {
            foreach (var date in root.Days.Keys.Where(d => d < detailFrom).ToList())
                pruned |= root.Days.Remove(date);
            foreach (var date in root.DayTotals.Keys.Where(d => d < totalsFrom).ToList())
                pruned |= root.DayTotals.Remove(date);
        }

        return pruned;
    }

    private bool TryNormalizePrefixes(IReadOnlyList<string>? prefixes, out List<string> normalized, out string error)
    {
        normalized = new List<string>();
        error = "";
        foreach (var prefix in prefixes ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(prefix))
                continue;

            var spelling = RootPathMatcher.Normalize(prefix);
            if (spelling is null)
            {
                error = $"\"{prefix}\" isn't a folder on a drive or a network share, so it can't count as the same folder.";
                return false;
            }

            if (!normalized.Any(p => SamePath(p, spelling)))
                normalized.Add(spelling);
        }

        return true;
    }

    private TrackedRoot? Find(string rootId) => _roots.Find(r => r.RootId == rootId);

    private string NewRootId() => NewRootId(_roots.Select(r => r.RootId).ToHashSet(StringComparer.Ordinal));

    /// <summary>A fresh id not in <paramref name="taken"/>, which it is then added to.</summary>
    private static string NewRootId(HashSet<string> taken)
    {
        string id;
        do
            id = Guid.NewGuid().ToString("N");
        while (!taken.Add(id));
        return id;
    }

    private static bool SamePath(string a, string b)
        => string.Equals(RootPathMatcher.Normalize(a) ?? a, RootPathMatcher.Normalize(b) ?? b, StringComparison.OrdinalIgnoreCase);

    private static ActivityRootSnapshot Snapshot(TrackedRoot root)
        => new(
            new TrackedRootConfig(root.RootId, root.Path)
            {
                EquivalentPrefixes = root.EquivalentPrefixes.ToArray(),
                Rollup = root.Rollup,
                Depth = root.Depth,
                DwellThreshold = TimeSpan.FromSeconds(root.DwellThresholdSeconds),
                IdleTimeout = TimeSpan.FromMinutes(root.IdleTimeoutMinutes),
            },
            root.Enabled,
            root.TrackingStartedAt);

    private DateOnly Today() => LocalDate(_time.GetUtcNow());

    private DateOnly LocalDate(DateTimeOffset instant)
        => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, _time.LocalTimeZone).DateTime);
}
