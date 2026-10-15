using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using QuickerPlaces.Models;
using QuickerPlaces.Models.Sessions;

namespace QuickerPlaces.Services.Sessions.Suggestions;

/// <summary>
/// The suggestion switches and levels as the rest of the app reads them.
/// A missing file reads as off (G2); levels are read leniently.
/// </summary>
public sealed record SuggestionSettings(
    bool Enabled,
    bool OpenTogether,
    SuggestionSensitivity Overall,
    SuggestionSensitivity? UsedTogether,
    SuggestionSensitivity? OpenTogetherLevel)
{
    /// <summary>The rule options these settings give: the overall level, with each rule's own level overriding just that rule.</summary>
    public SessionSuggestionOptions Options => SessionSuggestionOptions.For(Overall, UsedTogether, OpenTogetherLevel);
}

/// <summary>
/// Owns session-suggestions.json (session suggestions plan §8): whether
/// suggestions are on, their levels, the suggestions the user dismissed and
/// the open spans the "open together" rule reads. It sits in local, not
/// roaming, application data because the spans describe this machine (G14).
///
/// Built over the same IPlacesStorage seam as sessions.json, so
/// FilePlacesStorage gives the temp-file-and-replace write, a backup and a
/// quarantine. Loading follows <see cref="SessionStore"/> outcome for outcome:
/// - a damaged file is quarantined and the store starts fresh, with one line
///   in <see cref="Notice"/>; if it can't be set aside the store is read-only.
///   The design (§8) says to leave a damaged file untouched; SessionStore's
///   established quarantine pattern is followed instead, so every JSON store
///   in the app behaves alike;
/// - a file that cannot be opened, or that a newer version wrote, is left
///   untouched and every change is refused this session.
///
/// - Settings and dismissals are written at once. A failed write is returned
///   to be shown and the change stays in memory for the next write.
/// - Open spans are buffered by <see cref="SetOpenSpans"/> and written by
///   <see cref="Flush"/> or the next write, as RecentFilesStore buffers opens.
/// - Spans are kept only while the "open together" rule is effectively on,
///   Enabled and OpenTogether (G2); turning either off clears them, so no
///   history is kept for a rule that is off. Dismissals are always kept.
/// - Every write prunes dismissals older than a year and spans older than the
///   rules' history window.
///
/// Every public member takes one lock, because the tracking timer sets spans
/// while the UI thread saves settings. Logs counts and outcomes only, never
/// a file path of the user's. UI-free and linked into the test project.
/// </summary>
public sealed class SessionSuggestionStore
{
    /// <summary>The session-suggestions.json schema version this build writes and reads.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>Days a dismissal is remembered.</summary>
    public const int DismissalDays = 365;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly object _sync = new();
    private readonly IPlacesStorage _storage;
    private readonly TimeProvider _time;
    private SuggestionSettings _settings;
    private readonly List<DismissedSuggestion> _dismissed;
    private List<OpenSpan> _spans;
    private bool _hasUnsavedChanges;

    public SessionSuggestionStore(IPlacesStorage storage, TimeProvider time)
    {
        _storage = storage;
        _time = time;

        var (document, outcome) = Load();
        _settings = ReadSettings(document);
        _dismissed = NormalizeDismissed(document?.Dismissed);
        _spans = IsRuleBOn(_settings) ? NormalizeSpans(document?.OpenSpans) : new List<OpenSpan>();
        LoadOutcome = outcome;

        // Spans found while rule B is off are dropped, and the next Flush
        // removes them from disk (G2, §7.2).
        if (document?.OpenSpans is { Count: > 0 } && !IsRuleBOn(_settings))
            _hasUnsavedChanges = true;

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
                    DiagnosticLog.Warn($"Quarantined damaged session suggestions store to {kept}; suggestions start off.");
                    IsAvailable = true;
                    Notice = $"Your session suggestion settings couldn't be read and were reset. The damaged file was kept as \"{kept}\".";
                }
                catch (Exception ex)
                {
                    // Not set aside, so never written over: suggestions stay read-only.
                    DiagnosticLog.Error($"Failed to quarantine damaged session suggestions store at {_storage.StoreFilePath}; suggestions are off for this session.", ex);
                    Notice = $"Your session suggestion settings couldn't be read, and the damaged file couldn't be set aside, so they can't be saved. The file is \"{_storage.StoreFilePath}\".";
                }

                break;

            case StoreLoadOutcome.Unreadable:
                Notice = $"Your session suggestion settings couldn't be opened, so none can be saved until QuickerPlaces restarts. The file is \"{_storage.StoreFilePath}\".";
                break;

            case StoreLoadOutcome.WrittenByNewerVersion:
                Notice = "Your session suggestion settings were written by a newer version of QuickerPlaces, so they can't be changed here. Update QuickerPlaces to use them.";
                break;
        }
    }

    /// <summary>Builds the store over session-suggestions.json in local application data (G14).</summary>
    public static SessionSuggestionStore CreateDefault()
    {
        return new SessionSuggestionStore(new FilePlacesStorage(AppDataFolders.Local, "session-suggestions.json"), TimeProvider.System);
    }

    /// <summary>What happened when session-suggestions.json was loaded.</summary>
    public StoreLoadOutcome LoadOutcome { get; }

    /// <summary>
    /// False when the file could not be opened, was written by a newer
    /// version, or was damaged and could not be set aside. Then every change
    /// is refused with <see cref="Notice"/>, and nothing is written.
    /// </summary>
    public bool IsAvailable { get; }

    /// <summary>The one line to show about loading, or null when there is nothing to say.</summary>
    public string? Notice { get; }

    /// <summary>Full path to session-suggestions.json, for messages.</summary>
    public string StoreFilePath => _storage.StoreFilePath;

    /// <summary>True while a change in memory has not reached disk.</summary>
    public bool HasUnsavedChanges
    {
        get { lock (_sync) return _hasUnsavedChanges; }
    }

    /// <summary>The switches and levels. Off when the file is missing or can't be used.</summary>
    public SuggestionSettings Settings
    {
        get { lock (_sync) return _settings; }
    }

    /// <summary>A snapshot of the buffered open spans.</summary>
    public IReadOnlyList<OpenSpan> OpenSpans
    {
        get { lock (_sync) return _spans.ToArray(); }
    }

    /// <summary>
    /// Saves the switches and levels at once. While the "open together" rule
    /// is not effectively on (Enabled and OpenTogether), the open spans are
    /// cleared, so none are kept for a rule that is off (G2). Dismissals stay.
    /// </summary>
    public PersistenceResult SaveSettings(SuggestionSettings settings)
    {
        lock (_sync)
        {
            if (!IsAvailable)
                return PersistenceResult.Fail(Notice!);

            _settings = settings;
            if (!IsRuleBOn(settings))
                _spans = new List<OpenSpan>();

            return SaveNow();
        }
    }

    /// <summary>True when the user dismissed the suggestion with this key.</summary>
    public bool IsDismissed(string key)
    {
        lock (_sync)
            return _dismissed.Any(d => string.Equals(d.Key, key, StringComparison.Ordinal));
    }

    /// <summary>
    /// Remembers that the user dismissed a suggestion, written at once.
    /// Dismissing a key again keeps one entry, with the latest time. A null
    /// or blank key changes nothing.
    /// </summary>
    public PersistenceResult Dismiss(string? key, int fileCount)
    {
        lock (_sync)
        {
            if (!IsAvailable)
                return PersistenceResult.Fail(Notice!);
            if (string.IsNullOrWhiteSpace(key))
                return PersistenceResult.Ok();

            _dismissed.RemoveAll(d => string.Equals(d.Key, key, StringComparison.Ordinal));
            _dismissed.Add(new DismissedSuggestion { Key = key, Files = fileCount, DismissedAt = _time.GetUtcNow() });
            return SaveNow();
        }
    }

    /// <summary>
    /// Replaces the buffered open spans and marks them unsaved; <see cref="Flush"/>
    /// or the next write puts them on disk. Ignored, keeping nothing, while the
    /// "open together" rule is off or the store is unavailable (G2).
    /// </summary>
    public void SetOpenSpans(IEnumerable<OpenSpan> spans)
    {
        lock (_sync)
        {
            if (!IsAvailable || !IsRuleBOn(_settings))
                return;

            _spans = NormalizeSpans(spans);
            _hasUnsavedChanges = true;
        }
    }

    /// <summary>Writes buffered changes if there are any. A failure is returned and kept for the next write.</summary>
    public PersistenceResult Flush()
    {
        lock (_sync)
            return IsAvailable && _hasUnsavedChanges ? SaveNow() : PersistenceResult.Ok();
    }

    // ---------------------------------------------------------------
    // Disk I/O
    // ---------------------------------------------------------------

    private (SessionSuggestionsDocument? document, StoreLoadOutcome outcome) Load()
    {
        var (document, outcome) = JsonStoreLoader.Load<SessionSuggestionsDocument>(
            _storage, CurrentSchemaVersion, "dismissed", "Session suggestions", JsonOptions);
        if (document is null)
            return (null, outcome);

        DiagnosticLog.Info($"Loaded session suggestions: {document.Dismissed?.Count ?? 0} dismissal(s), {document.OpenSpans?.Count ?? 0} open span(s).");
        return (document, StoreLoadOutcome.Ok);
    }

    private PersistenceResult SaveNow()
    {
        _hasUnsavedChanges = true;
        Prune();
        try
        {
            var document = new SessionSuggestionsDocument
            {
                SchemaVersion = CurrentSchemaVersion,
                Enabled = _settings.Enabled,
                OpenTogether = _settings.OpenTogether,
                Sensitivity = SuggestionSensitivities.Format(_settings.Overall),
                UsedTogetherSensitivity = _settings.UsedTogether is { } used ? SuggestionSensitivities.Format(used) : null,
                OpenTogetherSensitivity = _settings.OpenTogetherLevel is { } open ? SuggestionSensitivities.Format(open) : null,
                Dismissed = _dismissed,
                OpenSpans = _spans,
            };
            _storage.Write(JsonSerializer.Serialize(document, JsonOptions));
            _hasUnsavedChanges = false;
            return PersistenceResult.Ok();
        }
        catch (Exception ex)
        {
            // Counts only, never a file path of the user's.
            DiagnosticLog.Error($"Failed to save {_dismissed.Count} dismissal(s) and {_spans.Count} open span(s) to {_storage.StoreFilePath}; kept in memory for the next save.", ex);
            return PersistenceResult.Fail(
                $"Couldn't save your session suggestion settings to \"{_storage.StoreFilePath}\". {ex.Message} QuickerPlaces will try again when it next saves.");
        }
    }

    /// <summary>Drops dismissals older than a year and spans that ended before the rules' history window.</summary>
    private void Prune()
    {
        var now = _time.GetUtcNow();
        var dismissedFrom = now.AddDays(-DismissalDays);
        _dismissed.RemoveAll(d => d.DismissedAt < dismissedFrom);

        var spansFrom = now.AddDays(-SessionSuggestionOptions.Balanced.HistoryDays);
        _spans.RemoveAll(s => s.To < spansFrom);
    }

    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------

    /// <summary>Rule B (open together) is effectively on only when suggestions are on and its own switch is.</summary>
    private static bool IsRuleBOn(SuggestionSettings settings) => settings.Enabled && settings.OpenTogether;

    /// <summary>Reads the settings leniently: unknown overall text is balanced, unknown rule text is "follow overall".</summary>
    private static SuggestionSettings ReadSettings(SessionSuggestionsDocument? document)
    {
        if (document is null)
            return new SuggestionSettings(false, true, SuggestionSensitivity.Balanced, null, null);

        return new SuggestionSettings(
            document.Enabled,
            document.OpenTogether,
            SuggestionSensitivities.Parse(document.Sensitivity) ?? SuggestionSensitivity.Balanced,
            SuggestionSensitivities.Parse(document.UsedTogetherSensitivity),
            SuggestionSensitivities.Parse(document.OpenTogetherSensitivity));
    }

    /// <summary>Null entries and blank keys dropped, and each key kept once (the latest dismissal).</summary>
    private static List<DismissedSuggestion> NormalizeDismissed(IEnumerable<DismissedSuggestion?>? loaded)
    {
        return (loaded ?? Array.Empty<DismissedSuggestion?>())
            .Where(d => d is not null && !string.IsNullOrWhiteSpace(d.Key))
            .GroupBy(d => d!.Key, StringComparer.Ordinal)
            .Select(g => g.OrderByDescending(d => d!.DismissedAt).First()!)
            .ToList();
    }

    /// <summary>Null entries, blank paths and spans that end before they start dropped.</summary>
    private static List<OpenSpan> NormalizeSpans(IEnumerable<OpenSpan?>? loaded)
    {
        return (loaded ?? Array.Empty<OpenSpan?>())
            .Where(s => s is not null && !string.IsNullOrWhiteSpace(s.Path) && s.To >= s.From)
            .Select(s => s!)
            .ToList();
    }
}
