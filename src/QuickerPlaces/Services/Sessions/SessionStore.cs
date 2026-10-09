using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using QuickerPlaces.Models;
using QuickerPlaces.Models.Sessions;
using QuickerPlaces.Services.Documents;

namespace QuickerPlaces.Services.Sessions;

/// <summary>
/// Owns sessions.json (sessions plan §3): the saved project sessions, each a
/// named, tagged set of PDF paths. The file sits beside places.json in
/// roaming application data, in a file of its own with its own schema
/// version, so places.json and its migrations are untouched.
///
/// Built over the same IPlacesStorage seam as places.json and activity.json
/// (Phase 9 D32): FilePlacesStorage gives the temp-file-and-replace write, a
/// sessions.bak.json backup and a sessions.corrupt-*.json quarantine.
///
/// - Every change is written at once. A failed write is returned to be
///   shown, never swallowed, and the change stays in memory for the next
///   write or <see cref="RetrySave"/> (Phase 1 D1).
/// - Loading never throws and never asks anything, as activity.json's does
///   (D5): a damaged file is quarantined and the list starts empty, with one
///   line in <see cref="Notice"/>; a file that cannot be opened, or that a
///   newer version wrote, is left untouched and every change is refused
///   this session, so it can never be written over.
/// - Nothing here touches places.json, and a session's files are not places.
///
/// Used from the UI thread only. UI-free and linked into the test project.
/// </summary>
public sealed class SessionStore
{
    /// <summary>The sessions.json schema version this build writes and reads.</summary>
    public const int CurrentSchemaVersion = 1;

    public const int MaxNameLength = 100;
    public const int MaxTagLength = 40;
    public const int MaxTags = 20;

    /// <summary>How many sessions have a shortcut: the first nine, Ctrl+Shift+1 to 9, as favourites have Ctrl+1 to 9.</summary>
    public const int ShortcutCount = 9;

    /// <summary>Days of reopen history kept per session, for the year view.</summary>
    public const int HistoryDays = 365;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IPlacesStorage _storage;
    private readonly TimeProvider _time;
    private readonly List<ProjectSession> _sessions;

    public SessionStore(IPlacesStorage storage, TimeProvider timeProvider)
    {
        _storage = storage;
        _time = timeProvider;

        var (sessions, outcome) = Load();
        _sessions = sessions;
        LoadOutcome = outcome;

        switch (outcome)
        {
            case StoreLoadOutcome.Ok:
            case StoreLoadOutcome.NotPresent:
                IsAvailable = true;
                break;

            case StoreLoadOutcome.Damaged:
                try
                {
                    var kept = _storage.Quarantine(_time.GetLocalNow());
                    DiagnosticLog.Warn($"Quarantined damaged sessions store to {kept}; sessions start empty.");
                    IsAvailable = true;
                    Notice = $"Your saved sessions couldn't be read and were reset. The damaged file was kept as \"{kept}\".";
                }
                catch (Exception ex)
                {
                    // Not set aside, so never written over: sessions stay read-only.
                    DiagnosticLog.Error($"Failed to quarantine damaged sessions store at {_storage.StoreFilePath}; sessions are off for this session.", ex);
                    Notice = $"Your saved sessions couldn't be read, and the damaged file couldn't be set aside, so sessions can't be saved. The file is \"{_storage.StoreFilePath}\".";
                }

                break;

            case StoreLoadOutcome.Unreadable:
                Notice = $"Your saved sessions couldn't be opened, so none can be saved until QuickerPlaces restarts. The file is \"{_storage.StoreFilePath}\".";
                break;

            case StoreLoadOutcome.WrittenByNewerVersion:
                Notice = "Your saved sessions were written by a newer version of QuickerPlaces, so they can't be changed here. Update QuickerPlaces to use them.";
                break;
        }
    }

    /// <summary>Builds the store over sessions.json beside places.json, in roaming application data.</summary>
    public static SessionStore CreateDefault()
    {
        return new SessionStore(new FilePlacesStorage(AppDataFolders.Roaming, "sessions.json"), TimeProvider.System);
    }

    /// <summary>What happened when sessions.json was loaded (the same classification as places.json, Phase 1 D6).</summary>
    public StoreLoadOutcome LoadOutcome { get; }

    /// <summary>
    /// False when sessions.json could not be opened, was written by a newer
    /// version, or was damaged and could not be set aside. Then every change
    /// is refused with <see cref="Notice"/>, and nothing is written.
    /// </summary>
    public bool IsAvailable { get; }

    /// <summary>The one line the Sessions window shows about loading, or null when there is nothing to say.</summary>
    public string? Notice { get; }

    /// <summary>Full path to sessions.json, for messages.</summary>
    public string StoreFilePath => _storage.StoreFilePath;

    /// <summary>True while a change in memory has not reached disk.</summary>
    public bool HasUnsavedChanges { get; private set; }

    /// <summary>
    /// Every saved session in the order the user keeps them (dragged on the cards;
    /// a new session goes last). The order is the order in sessions.json, and the
    /// first nine sessions are the ones Ctrl+Shift+1 to 9 open.
    /// </summary>
    public IReadOnlyList<SessionSnapshot> Sessions => _sessions.Select(Snapshot).ToList();

    /// <summary>A saved session by id, or null.</summary>
    public SessionSnapshot? Find(string id)
    {
        var index = _sessions.FindIndex(s => s.Id == id);
        return index < 0 ? null : Snapshot(_sessions[index], index);
    }

    /// <summary>Every tag in use, each once (first spelling wins), with how many sessions carry it, alphabetical.</summary>
    public IReadOnlyList<TagCount> Tags
    {
        get
        {
            var counts = new Dictionary<string, TagCount>(StringComparer.OrdinalIgnoreCase);
            foreach (var tag in _sessions.SelectMany(s => s.Tags))
                counts[tag] = counts.TryGetValue(tag, out var existing) ? existing with { Sessions = existing.Sessions + 1 } : new TagCount(tag, 1);

            return counts.Values.OrderBy(t => t.Tag, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
    }

    /// <summary>
    /// Saves a new session. Refuses a blank, overlong or already-used name,
    /// unusable tags, no files, or a path that isn't a full path to a PDF.
    /// Repeated tags and files are kept once. An accepted session is written
    /// at once; <paramref name="persistence"/> says whether that reached disk.
    /// </summary>
    public ValidationResult TryCreate(string? name, IEnumerable<string>? tags, IEnumerable<string>? files,
        out SessionSnapshot? created, out PersistenceResult persistence)
    {
        created = null;
        persistence = PersistenceResult.Ok();
        if (!IsAvailable)
            return ValidationResult.Fail(Notice!);

        var validation = Validate(null, name, tags, files, out var cleanName, out var cleanTags, out var cleanFiles);
        if (!validation.Success)
            return validation;

        var now = _time.GetUtcNow();
        var session = new ProjectSession
        {
            Id = NewId(),
            Name = cleanName,
            Tags = cleanTags,
            Files = cleanFiles,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _sessions.Add(session);

        persistence = SaveNow();
        created = Snapshot(session, _sessions.Count - 1);
        return ValidationResult.Ok();
    }

    /// <summary>Replaces a session's name, tags and files, by the same rules as <see cref="TryCreate"/>. Written at once.</summary>
    public ValidationResult TryUpdate(string id, string? name, IEnumerable<string>? tags, IEnumerable<string>? files,
        out PersistenceResult persistence)
    {
        persistence = PersistenceResult.Ok();
        if (!IsAvailable)
            return ValidationResult.Fail(Notice!);

        var session = FindSession(id);
        if (session is null)
            return ValidationResult.Fail("That session no longer exists.");

        var validation = Validate(session, name, tags, files, out var cleanName, out var cleanTags, out var cleanFiles);
        if (!validation.Success)
            return validation;

        if (session.Name == cleanName && session.Tags.SequenceEqual(cleanTags) && session.Files.SequenceEqual(cleanFiles))
            return ValidationResult.Ok();

        session.Name = cleanName;
        session.Tags = cleanTags;
        session.Files = cleanFiles;
        session.UpdatedAt = _time.GetUtcNow();

        persistence = SaveNow();
        return ValidationResult.Ok();
    }

    /// <summary>Deletes a session. Its PDFs are not touched: a session only ever held their paths. An unknown id changes nothing.</summary>
    public PersistenceResult Delete(string id)
    {
        if (!IsAvailable)
            return PersistenceResult.Fail(Notice!);

        var session = FindSession(id);
        if (session is null)
            return PersistenceResult.Ok();

        _sessions.Remove(session);
        return SaveNow();
    }

    /// <summary>
    /// Moves a session to the place of <paramref name="targetId"/>, as a dragged
    /// favourite takes the place it is dropped on: dragged up, it lands before that
    /// session; dragged down, after it. A null or unknown target means the end.
    /// When <paramref name="after"/> is supplied, insert explicitly before or
    /// after the target to match the card's drop preview. This sets which session
    /// each Ctrl+Shift number opens, and is not an edit of the session. Written at
    /// once; an unknown id, or a move that changes nothing, writes nothing.
    /// </summary>
    public PersistenceResult Move(string id, string? targetId, bool? after = null)
    {
        if (!IsAvailable)
            return PersistenceResult.Fail(Notice!);

        var from = _sessions.FindIndex(s => s.Id == id);
        if (from < 0)
            return PersistenceResult.Ok();

        var to = targetId is null ? _sessions.Count - 1 : _sessions.FindIndex(s => s.Id == targetId);
        if (to >= 0 && targetId is not null && after is { } insertAfter)
        {
            // Translate the preview's before/after slot to the index after
            // removing the dragged card, including filtered session lists.
            var insertion = to + (insertAfter ? 1 : 0);
            to = insertion - (from < insertion ? 1 : 0);
        }
        if (to < 0)
            to = _sessions.Count - 1;
        if (to == from)
            return PersistenceResult.Ok();

        var session = _sessions[from];
        _sessions.RemoveAt(from);
        _sessions.Insert(to, session);
        return SaveNow();
    }

    /// <summary>Records that a session was just reopened, for its Last opened line. An unknown id changes nothing.</summary>
    public PersistenceResult MarkOpened(string id)
    {
        if (!IsAvailable)
            return PersistenceResult.Ok();

        var session = FindSession(id);
        if (session is null)
            return PersistenceResult.Ok();

        var now = _time.GetUtcNow();
        session.LastOpenedAt = now;
        session.OpenedAt.Add(now);
        var keepFrom = now.AddDays(-HistoryDays);
        session.OpenedAt.RemoveAll(o => o < keepFrom);
        return SaveNow();
    }

    /// <summary>
    /// Per local day, how many sessions were saved and how many reopens there
    /// were, and which sessions, for the year view. Only sessions with
    /// <paramref name="tag"/> when one is given.
    /// </summary>
    public IReadOnlyDictionary<DateOnly, SessionDayTotal> QueryDays(string? tag = null)
    {
        var days = new Dictionary<DateOnly, (int Saved, int Reopens, List<string> Names)>();
        void Add(DateTimeOffset instant, string name, bool saved)
        {
            var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, _time.LocalTimeZone).DateTime);
            if (!days.TryGetValue(date, out var day))
                day = (0, 0, new List<string>());
            if (!day.Names.Contains(name, StringComparer.OrdinalIgnoreCase))
                day.Names.Add(name);
            days[date] = saved ? (day.Saved + 1, day.Reopens, day.Names) : (day.Saved, day.Reopens + 1, day.Names);
        }

        foreach (var session in _sessions)
        {
            if (tag is not null && !session.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
                continue;

            Add(session.CreatedAt, session.Name, saved: true);
            foreach (var opened in session.OpenedAt)
                Add(opened, session.Name, saved: false);
        }

        return days.ToDictionary(d => d.Key, d => new SessionDayTotal(d.Value.Saved, d.Value.Reopens, d.Value.Names));
    }

    /// <summary>Rewrites sessions.json if a change is still waiting to reach disk.</summary>
    public PersistenceResult RetrySave()
        => IsAvailable && HasUnsavedChanges ? SaveNow() : PersistenceResult.Ok();

    /// <summary>
    /// Splits what the user typed in a tags box into tags: separated by
    /// commas or semicolons, trimmed, inner runs of whitespace made single
    /// spaces, a leading "#" dropped, blanks dropped, and each kept once
    /// ignoring case (first spelling wins).
    /// </summary>
    public static IReadOnlyList<string> ParseTags(string? text)
    {
        var tags = new List<string>();
        foreach (var part in (text ?? "").Split(new[] { ',', ';' }))
        {
            var tag = CleanTag(part);
            if (tag.Length > 0 && !tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
                tags.Add(tag);
        }

        return tags;
    }

    /// <summary>The tags as they are typed back into a tags box.</summary>
    public static string FormatTags(IEnumerable<string> tags) => string.Join(", ", tags);

    // ---------------------------------------------------------------
    // Validation
    // ---------------------------------------------------------------

    private ValidationResult Validate(ProjectSession? existing, string? name, IEnumerable<string>? tags, IEnumerable<string>? files,
        out string cleanName, out List<string> cleanTags, out List<string> cleanFiles)
    {
        cleanName = (name ?? "").Trim();
        cleanTags = new List<string>();
        cleanFiles = new List<string>();

        if (cleanName.Length == 0)
            return ValidationResult.Fail("Give the session a name.");
        if (cleanName.Length > MaxNameLength)
            return ValidationResult.Fail($"Keep the name to {MaxNameLength} characters or fewer.");

        var taken = cleanName;
        if (_sessions.Any(s => !ReferenceEquals(s, existing) && string.Equals(s.Name, taken, StringComparison.OrdinalIgnoreCase)))
            return ValidationResult.Fail($"There's already a session called \"{cleanName}\".");

        foreach (var raw in tags ?? Array.Empty<string>())
        {
            var tag = CleanTag(raw);
            if (tag.Length == 0 || cleanTags.Contains(tag, StringComparer.OrdinalIgnoreCase))
                continue;
            if (tag.IndexOfAny(new[] { ',', ';' }) >= 0)
                return ValidationResult.Fail($"A tag can't contain a comma or semicolon: \"{tag}\".");
            if (tag.Length > MaxTagLength)
                return ValidationResult.Fail($"Keep each tag to {MaxTagLength} characters or fewer.");
            cleanTags.Add(tag);
        }

        if (cleanTags.Count > MaxTags)
            return ValidationResult.Fail($"A session can have up to {MaxTags} tags.");

        foreach (var raw in files ?? Array.Empty<string>())
        {
            var file = DocumentPaths.Normalize(raw);
            if (file is null)
                return ValidationResult.Fail($"\"{raw}\" isn't a full path to a PDF, Office, text, Revit or AutoCAD file, so it can't be saved in a session.");
            if (!cleanFiles.Any(f => string.Equals(f, file, StringComparison.OrdinalIgnoreCase)))
                cleanFiles.Add(file);
        }

        if (cleanFiles.Count == 0)
            return ValidationResult.Fail("Choose at least one file for the session.");

        return ValidationResult.Ok();
    }

    private static string CleanTag(string? raw)
    {
        var tag = string.Join(' ', (raw ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return tag.StartsWith('#') ? tag.TrimStart('#').Trim() : tag;
    }

    // ---------------------------------------------------------------
    // Disk I/O
    // ---------------------------------------------------------------

    /// <summary>
    /// Loads and classifies sessions.json as PlacesService does places.json
    /// (Phase 1 D6): a failure to read is Unreadable, never Damaged, and
    /// only content that was read and makes no sense is Damaged.
    /// </summary>
    private (List<ProjectSession> sessions, StoreLoadOutcome outcome) Load()
    {
        var (document, outcome) = JsonStoreLoader.Load<SessionsDocument>(_storage, CurrentSchemaVersion, "sessions", "Sessions store", JsonOptions);
        if (document is null)
            return (new List<ProjectSession>(), outcome);

        var loaded = Normalize(document.Sessions);
        DiagnosticLog.Info($"Loaded {loaded.Count} session(s) from {_storage.StoreFilePath}.");
        return (loaded, StoreLoadOutcome.Ok);
    }

    /// <summary>
    /// Makes a loaded document safe to use: null entries dropped, missing
    /// lists empty, a missing or repeated id replaced, blank tags and files
    /// dropped, and repeats kept once. A hand-edited name that is blank or
    /// repeated is kept as it is: the user can see and rename it.
    /// </summary>
    private static List<ProjectSession> Normalize(IEnumerable<ProjectSession?> loaded)
    {
        var sessions = new List<ProjectSession>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var session in loaded)
        {
            if (session is null)
                continue;

            if (string.IsNullOrEmpty(session.Id) || !ids.Add(session.Id))
                session.Id = NewId(ids);

            session.Name ??= "";
            session.Tags = (session.Tags ?? new List<string>())
                .Select(CleanTag)
                .Where(t => t.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            session.OpenedAt = (session.OpenedAt ?? new List<DateTimeOffset>()).OrderBy(o => o).ToList();
            session.Files = (session.Files ?? new List<string>())
                .Where(f => !string.IsNullOrWhiteSpace(f))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            sessions.Add(session);
        }

        return sessions;
    }

    private PersistenceResult SaveNow()
    {
        HasUnsavedChanges = true;
        try
        {
            var document = new SessionsDocument { SchemaVersion = CurrentSchemaVersion, Sessions = _sessions };
            _storage.Write(JsonSerializer.Serialize(document, JsonOptions));
            HasUnsavedChanges = false;
            return PersistenceResult.Ok();
        }
        catch (Exception ex)
        {
            // Counts and the file path only, never a session name or a file path.
            DiagnosticLog.Error($"Failed to save {_sessions.Count} session(s) to {_storage.StoreFilePath}; kept in memory for the next save.", ex);
            return PersistenceResult.Fail(
                $"Couldn't save your sessions to \"{_storage.StoreFilePath}\". {ex.Message} QuickerPlaces will try again when it next saves.");
        }
    }

    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------

    private ProjectSession? FindSession(string id) => _sessions.Find(s => s.Id == id);

    private string NewId() => NewId(_sessions.Select(s => s.Id).ToHashSet(StringComparer.Ordinal));

    /// <summary>A fresh id not in <paramref name="taken"/>, which it is then added to.</summary>
    private static string NewId(HashSet<string> taken)
    {
        string id;
        do
            id = Guid.NewGuid().ToString("N");
        while (!taken.Add(id));
        return id;
    }

    private static SessionSnapshot Snapshot(ProjectSession session, int order)
        => new(session.Id, session.Name, session.Tags.ToArray(), session.Files.ToArray(),
            session.CreatedAt, session.UpdatedAt, session.LastOpenedAt, session.OpenedAt.ToArray(), order);
}

/// <summary>An immutable copy of one saved session, for the UI.</summary>
public sealed record SessionSnapshot(
    string Id,
    string Name,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Files,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? LastOpenedAt,
    IReadOnlyList<DateTimeOffset> OpenedAt,
    int Order = 0)
{
    /// <summary>The later of when it was last reopened and last changed.</summary>
    public DateTimeOffset LastUsedAt => LastOpenedAt is { } opened && opened > UpdatedAt ? opened : UpdatedAt;

    /// <summary>
    /// The digit of the Ctrl+Shift+digit that opens this session: 1 to 9 for the first
    /// nine, null for the rest. Where it sits in <see cref="Order"/>, as
    /// a favourite's number is where it sits in the bubbles.
    /// </summary>
    public int? ShortcutDigit => Order < SessionStore.ShortcutCount ? Order + 1 : null;
}

/// <summary>One day of sessions, for the year view: how many were saved, how many reopens, and their names.</summary>
public sealed record SessionDayTotal(int Saved, int Reopens, IReadOnlyList<string> Names);

/// <summary>A tag in use and how many sessions carry it.</summary>
public sealed record TagCount(string Tag, int Sessions);
