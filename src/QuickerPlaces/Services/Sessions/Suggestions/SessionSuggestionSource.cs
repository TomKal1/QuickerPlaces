using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using QuickerPlaces.Models;
using QuickerPlaces.Services.Activity;
using QuickerPlaces.Services.Documents;
using QuickerPlaces.Services.RecentFiles;

namespace QuickerPlaces.Services.Sessions.Suggestions;

/// <summary>A quick action can be accepted in memory even if its write fails.</summary>
public sealed record SuggestionActionResult(bool Success, string? SessionId, string? Message, PersistenceResult Persistence);

/// <summary>
/// Coordinates the suggestion stores (G7, G10, G12). Capture snapshots and call
/// actions on the session store's owning thread; only pure calculation and file
/// existence checks run on the worker. The cache lock never covers that work.
/// Recorded opens and external session changes must call Invalidate. Every
/// refresh caller receives only a current accepted result, including late callers
/// whose calculation was invalidated by a dismissal.
/// </summary>
public sealed class SessionSuggestionSource
{
    private readonly RecentFilesStore _recent;
    private readonly SessionSuggestionStore _store;
    private readonly SessionStore _sessions;
    private readonly IShell _shell;
    private readonly TimeProvider _time;
    private readonly Func<IReadOnlyList<TrackedRootConfig>> _trackedRoots;
    private readonly object _sync = new();
    private long _generation;
    private IReadOnlyList<SessionSuggestion> _cached = Array.Empty<SessionSuggestion>();
    private bool _valid;
    private long _cachedRevision;
    private DateOnly _cachedDay;
    private IReadOnlyList<SessionSnapshot> _cachedSessions = Array.Empty<SessionSnapshot>();
    private RecentFilesSettingsSnapshot? _cachedTracking;
    private IReadOnlyList<TrackedRootConfig> _cachedRoots = Array.Empty<TrackedRootConfig>();
    private UndoState? _undo;

    public SessionSuggestionSource(RecentFilesStore recent, SessionSuggestionStore store,
        SessionStore sessions, IShell shell, TimeProvider time, Func<IReadOnlyList<TrackedRootConfig>>? trackedRoots = null)
    {
        _recent = recent;
        _store = store;
        _sessions = sessions;
        _shell = shell;
        _time = time;
        _trackedRoots = trackedRoots ?? (() => Array.Empty<TrackedRootConfig>());
    }

    public SessionSuggestionStore Store => _store;
    public bool RecentFilesEnabled => _recent.IsTracking;
    public bool CanUndo => _undo is not null;
    public IReadOnlyList<SessionSnapshot> Sessions => _sessions.Sessions;

    /// <summary>Reads the last result only. Never checks files or calculates suggestions.</summary>
    public int Count { get { lock (_sync) return _cached.Count; } }

    public void Invalidate()
    {
        lock (_sync)
        {
            _generation++;
            _valid = false;
            _cached = Array.Empty<SessionSuggestion>();
        }
    }

    public async Task<IReadOnlyList<SessionSuggestion>> RefreshAsync()
    {
        var settings = _store.Settings;
        var revision = _store.Revision;
        var sessions = _sessions.Sessions;
        var tracking = _recent.Settings;
        var roots = _trackedRoots().ToArray();
        var now = _time.GetUtcNow();
        var day = DateOnly.FromDateTime(_time.GetLocalNow().DateTime);
        long generation;
        lock (_sync)
        {
            if (!settings.Enabled || !_store.IsAvailable || !_recent.IsTracking)
            {
                _generation++;
                _valid = false;
                return _cached = Array.Empty<SessionSuggestion>();
            }
            if (_valid && revision == _cachedRevision && day == _cachedDay && SameSessions(sessions, _cachedSessions)
                && SameTracking(tracking, _cachedTracking) && SameRoots(roots, _cachedRoots))
                return _cached;
            generation = ++_generation;
            _valid = false;
            _cached = Array.Empty<SessionSuggestion>();
        }

        var history = _recent.QueryHistory();
        var spans = _store.OpenSpans;
        var dismissed = _store.DismissalSnapshot();
        var zone = _time.LocalTimeZone;
        var cards = await Task.Run(() =>
        {
            // Missing files are removed before grouping, matching and ranking (G12).
            var exists = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            bool Exists(string path)
            {
                if (!exists.TryGetValue(path, out var found))
                {
                    try { found = _shell.FileExists(path); }
                    catch { found = false; }
                    exists[path] = found;
                }
                return found;
            }
            var options = settings.Options;
            // Retained history must also follow today's file-type and folder consent.
            var kinds = tracking.Kinds.ToHashSet();
            bool Allowed(string path) => DocumentKinds.FromPath(path) is { } kind && kinds.Contains(kind)
                && RecentFilesStore.IsInScope(path, tracking.Scope, roots) && Exists(path);
            var pairs = OpenedTogetherRule.Find(history.Where(h => Allowed(h.Path)), options, zone, now)
                .Concat(settings.OpenTogether
                    ? OpenTogetherRule.Find(spans.Where(s => Allowed(s.Path)), options, zone, now)
                    : Array.Empty<StrongPair>());
            return SessionSuggester.Suggest(pairs, sessions, _ => false, options, day, dismissed);
        });

        // Await resumes on the caller's context: SessionStore is UI-owned.
        var currentSessions = _sessions.Sessions;
        var currentRevision = _store.Revision;
        var currentDay = DateOnly.FromDateTime(_time.GetLocalNow().DateTime);
        var currentTracking = _recent.Settings;
        var currentRoots = _trackedRoots();
        lock (_sync)
        {
            if (generation != _generation) return _cached;
            if (revision != currentRevision || currentDay != day || !_recent.IsTracking || !SameSessions(sessions, currentSessions)
                || !SameTracking(tracking, currentTracking) || !SameRoots(roots, currentRoots))
                return _cached; // Invalid results must not escape through the returned task either.
            _cached = Array.AsReadOnly(cards.Select(c => c with
            {
                Files = Array.AsReadOnly(c.Files.ToArray()),
                NewFiles = Array.AsReadOnly(c.NewFiles.ToArray()),
            }).ToArray());
            _cachedRevision = revision;
            _cachedDay = day;
            _cachedSessions = sessions;
            _cachedTracking = tracking;
            _cachedRoots = roots;
            _valid = true;
            return _cached;
        }
    }

    public PersistenceResult Dismiss(SessionSuggestion suggestion)
    {
        var result = _store.Dismiss(suggestion.Files);
        Invalidate();
        return result;
    }

    public SuggestionActionResult Create(SessionSuggestion suggestion)
    {
        var validation = _sessions.TryCreate(suggestion.SuggestedName, [], suggestion.Files, out var created, out var persistence);
        if (!validation.Success) return Refused(validation.ErrorMessage!);
        _undo = new(null, created!);
        Invalidate();
        return Accepted(created!.Id, persistence);
    }

    public SuggestionActionResult Add(SessionSuggestion suggestion, string sessionId)
    {
        var before = _sessions.Find(sessionId);
        if (before is null) return Refused("That session no longer exists.");
        var files = before.Files.Concat(suggestion.Files).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (files.Length == before.Files.Count) return Refused("Those files are already in that session.");
        var validation = _sessions.TryUpdate(sessionId, before.Name, before.Tags, files, out var persistence);
        if (!validation.Success) return Refused(validation.ErrorMessage!);
        _undo = new(before, _sessions.Find(sessionId)!);
        Invalidate();
        return Accepted(sessionId, persistence);
    }

    public SuggestionActionResult Undo()
    {
        if (_undo is not { } undo) return Refused("There is no suggestion action to undo.");
        var current = _sessions.Find(undo.After.Id);
        if (current is null) return Refused("That session has been deleted, so this action can't be undone.");
        if (!SameContent(current, undo.After)) return Refused("That session has been edited since, so this action can't be undone.");
        PersistenceResult persistence;
        if (undo.Before is null) persistence = _sessions.Delete(current.Id);
        else
        {
            var validation = _sessions.TryUpdate(current.Id, undo.Before.Name, undo.Before.Tags, undo.Before.Files, out persistence);
            if (!validation.Success) return Refused(validation.ErrorMessage!);
        }
        _undo = null;
        Invalidate();
        return Accepted(current.Id, persistence);
    }

    private static bool SameContent(SessionSnapshot a, SessionSnapshot b) =>
        a.Id == b.Id && a.Name == b.Name && a.UpdatedAt == b.UpdatedAt && a.Tags.SequenceEqual(b.Tags) && a.Files.SequenceEqual(b.Files);

    private static bool SameSessions(IReadOnlyList<SessionSnapshot> a, IReadOnlyList<SessionSnapshot> b) =>
        a.Count == b.Count && a.Zip(b).All(p => SameContent(p.First, p.Second));

    private static bool SameTracking(RecentFilesSettingsSnapshot a, RecentFilesSettingsSnapshot? b) =>
        b is not null && a.Enabled == b.Enabled && a.Scope == b.Scope && a.Kinds.ToHashSet().SetEquals(b.Kinds);

    private static bool SameRoots(IReadOnlyList<TrackedRootConfig> a, IReadOnlyList<TrackedRootConfig> b) =>
        a.Count == b.Count && a.Zip(b).All(p =>
            (p.First with { EquivalentPrefixes = Array.Empty<string>() }) == (p.Second with { EquivalentPrefixes = Array.Empty<string>() })
            && p.First.EquivalentPrefixes.SequenceEqual(p.Second.EquivalentPrefixes, StringComparer.OrdinalIgnoreCase));

    private static SuggestionActionResult Refused(string message) => new(false, null, message, PersistenceResult.Ok());
    private static SuggestionActionResult Accepted(string id, PersistenceResult persistence) => new(true, id, persistence.UserMessage, persistence);
    private sealed record UndoState(SessionSnapshot? Before, SessionSnapshot After);
}
