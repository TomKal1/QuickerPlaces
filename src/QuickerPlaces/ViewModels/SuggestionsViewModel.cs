using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using QuickerPlaces.Mvvm;
using QuickerPlaces.Services.Documents;
using QuickerPlaces.Services.Sessions;
using QuickerPlaces.Services.Sessions.Suggestions;

namespace QuickerPlaces.ViewModels;

/// <summary>Cards, quick actions and Undo, with no UI dependencies.</summary>
public sealed class SuggestionsViewModel : ObservableObject
{
    private readonly SessionSuggestionSource _source;
    private string? _status;
    private bool _busy;
    private int _refresh;
    public SuggestionsViewModel(SessionSuggestionSource source, SuggestionSettingsViewModel settings)
    {
        _source = source;
        Settings = settings;
    }
    public SuggestionSettingsViewModel Settings { get; }
    public ObservableCollection<SuggestionCardViewModel> Cards { get; } = new();
    public event Action? SessionsChanged;
    public string? SavedSessionId { get; private set; }
    public int Count => _source.Count;
    public bool HasSuggestions => Count > 0;
    public bool CanUndo => _source.CanUndo;
    public bool IsBusy { get => _busy; private set => SetProperty(ref _busy, value); }
    public string? Status { get => _status; private set => SetProperty(ref _status, value); }
    public string? Notice => _source.Store.Notice;
    public bool NeedsRecentFiles => !_source.RecentFilesEnabled;
    public IReadOnlyList<SessionSnapshot> Sessions => _source.Sessions;
    public string EmptyMessage => NeedsRecentFiles ? "Turn on Recent Files to suggest sessions from files you use together."
        : !Settings.Enabled ? "Turn on session suggestions below to find files you use together."
        : "No suggestions yet. Files used together on several days will appear here." + (Settings.IsHigh ? "" : " Try High sensitivity.");
    public string SensitivityHint => Count > 10 && Settings.IsHigh ? "Too many? Try Balanced sensitivity." : "";

    public void Invalidate()
    {
        _refresh++;
        _source.Invalidate();
        Cards.Clear();
        IsBusy = false;
        Notify();
    }

    public async Task RefreshAsync()
    {
        var request = ++_refresh;
        IsBusy = true;
        try
        {
            var cards = await _source.RefreshAsync();
            if (request != _refresh) return;
            Cards.Clear();
            foreach (var card in cards) Cards.Add(new(card, Sessions));
            Notify();
        }
        catch (Exception ex)
        {
            if (request == _refresh) Status = $"Suggestions couldn't be refreshed ({ex.GetType().Name}). Try again.";
        }
        finally { if (request == _refresh) IsBusy = false; }
    }

    public void Create(SuggestionCardViewModel card) => Report(_source.Create(card.Suggestion), "Session created. Undo is available.");
    public void Add(SuggestionCardViewModel card, string sessionId) => Report(_source.Add(card.Suggestion, sessionId), "Files added to session. Undo is available.");
    public void Undo() => Report(_source.Undo(), "Suggestion action undone.");
    public void Dismiss(SuggestionCardViewModel card)
    {
        var result = _source.Dismiss(card.Suggestion);
        Cards.Clear();
        Status = result.Saved ? "Suggestion dismissed." : result.UserMessage;
        Notify();
    }
    public void NoteReviewed(string sessionId, string? persistenceMessage)
    {
        _source.Invalidate();
        Cards.Clear();
        SavedSessionId = sessionId;
        Status = persistenceMessage ?? "Session saved.";
        SessionsChanged?.Invoke();
        Notify();
    }
    private void Report(SuggestionActionResult result, string success)
    {
        Status = result.Success ? result.Message ?? success : result.Message;
        if (result.Success)
        {
            Cards.Clear();
            SavedSessionId = Sessions.Any(s => s.Id == result.SessionId) ? result.SessionId : null;
            SessionsChanged?.Invoke();
        }
        Notify();
    }
    private void Notify()
    {
        foreach (var name in new[] { nameof(Count), nameof(HasSuggestions), nameof(CanUndo), nameof(NeedsRecentFiles), nameof(EmptyMessage), nameof(Sessions), nameof(SensitivityHint) })
            OnPropertyChanged(name);
    }
}

public sealed class SuggestionCardViewModel
{
    public SuggestionCardViewModel(SessionSuggestion suggestion, IReadOnlyList<SessionSnapshot> sessions)
    {
        Suggestion = suggestion;
        TargetName = sessions.FirstOrDefault(s => s.Id == suggestion.TargetSessionId)?.Name;
        AddTargets = sessions.OrderByDescending(s => s.Files.Count(f => suggestion.Files.Any(p => DocumentPaths.Same(f, p)))).ToArray();
    }
    public SessionSuggestion Suggestion { get; }
    public string? TargetName { get; }
    public string Title => Suggestion.IsAddToSession ? $"Add {SessionsViewModel.FileCount(Suggestion.NewFiles.Count)} to {TargetName}" : Suggestion.SuggestedName ?? "Suggested session";
    public string ActionLabel => Suggestion.IsAddToSession ? $"Add to {TargetName}" : "New session";
    public IReadOnlyList<SessionSnapshot> AddTargets { get; }
    public string FilesText => string.Join(Environment.NewLine, Suggestion.Files.Select(DocumentPaths.FileName));
    private IEnumerable<string> DisplayFiles => Suggestion.IsAddToSession ? Suggestion.NewFiles : Suggestion.Files;
    public IReadOnlyList<SuggestionFileViewModel> Files => DisplayFiles.Take(4).Select(p => new SuggestionFileViewModel(p)).ToArray();
    public IReadOnlyList<SuggestionFileViewModel> MoreFiles => DisplayFiles.Skip(4).Select(p => new SuggestionFileViewModel(p)).ToArray();
    public bool HasMoreFiles => DisplayFiles.Count() > 4;
    public string MoreLabel => $"+ {DisplayFiles.Count() - 4} more";
    public string ReasonText => string.Join(" · ", new[]
    {
        Suggestion.Reason.DaysTogether > 0 ? $"Used together on {Suggestion.Reason.DaysTogether} days, last {Suggestion.Reason.LastTogether:ddd}" : null,
        Suggestion.Reason.LongestOpenTogether is { } span ? $"Open together for {(int)span.TotalHours} h {span.Minutes} min on {Suggestion.Reason.LongestOpenOn:ddd}" : null,
    }.Where(s => s is not null));
}

public sealed record SuggestionFileViewModel(string Path)
{
    public string FileName => DocumentPaths.FileName(Path);
}
