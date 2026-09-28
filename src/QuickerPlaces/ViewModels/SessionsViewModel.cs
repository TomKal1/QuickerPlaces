using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using QuickerPlaces.Mvvm;
using QuickerPlaces.Services.Documents;
using QuickerPlaces.Services.Sessions;

namespace QuickerPlaces.ViewModels;

/// <summary>
/// Backs the Sessions window (sessions plan §5): the saved sessions, the
/// search box and tag filter over them, the selected session's PDFs, and the
/// open and delete actions. SessionsWindow is a thin view over this, as
/// RecentlyDeletedDialog is over its view model (Phase 2 D21): it binds,
/// asks before deleting, and shows the editor dialog.
///
/// UI-free and linked into the test project.
/// </summary>
public sealed class SessionsViewModel : ObservableObject
{
    /// <summary>Shown in place of the list when nothing has been saved.</summary>
    public const string EmptyMessage = "No sessions yet. Open the files you're working on, then choose Save open files.";

    /// <summary>Shown in place of the list when the search or tag hides every session.</summary>
    public const string NoMatchesMessage = "No sessions match. Clear the search or choose All tags.";

    private readonly SessionStore _store;
    private readonly SessionLauncher _launcher;
    private readonly TimeZoneInfo _localZone;
    private string _searchText = "";
    private string? _selectedTag;
    private SessionRowViewModel? _selectedRow;
    private string? _statusMessage;
    private string? _errorMessage;

    /// <param name="localZone">The zone dates are shown in; null for the machine's own. Tests pass a fixed one.</param>
    public SessionsViewModel(SessionStore store, SessionLauncher launcher, TimeZoneInfo? localZone = null)
    {
        _store = store;
        _launcher = launcher;
        _localZone = localZone ?? TimeZoneInfo.Local;
        Reload(null);
    }

    /// <summary>The sessions the search and tag let through, most recently used first.</summary>
    public ObservableCollection<SessionRowViewModel> Rows { get; } = new();

    /// <summary>"All tags" and then every tag in use, for the filter.</summary>
    public ObservableCollection<TagFilterViewModel> TagFilters { get; } = new();

    /// <summary>The selected session's PDFs, in the session's order.</summary>
    public ObservableCollection<SessionFileViewModel> SelectedFiles { get; } = new();

    /// <summary>The store's one line about loading, if any.</summary>
    public string? Notice => _store.Notice;

    public bool HasNotice => !string.IsNullOrEmpty(Notice);

    /// <summary>False when the store refuses changes, so Save open PDFs, Edit and Delete are off.</summary>
    public bool CanChange => _store.IsAvailable;

    public bool HasAnySessions => _store.Sessions.Count > 0;

    /// <summary>The list's placeholder, or null when it has rows.</summary>
    public string? ListPlaceholder => Rows.Count > 0 ? null : HasAnySessions ? NoMatchesMessage : EmptyMessage;

    public bool IsListEmpty => Rows.Count == 0;

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? ""))
                ApplyFilter(_selectedRow?.Id);
        }
    }

    /// <summary>The tag the list is filtered to, or null for all.</summary>
    public string? SelectedTag
    {
        get => _selectedTag;
        set
        {
            if (!SetProperty(ref _selectedTag, string.IsNullOrEmpty(value) ? null : value))
                return;

            foreach (var filter in TagFilters)
                filter.IsSelected = string.Equals(filter.Tag, _selectedTag, StringComparison.OrdinalIgnoreCase);
            ApplyFilter(_selectedRow?.Id);
        }
    }

    public SessionRowViewModel? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (!SetProperty(ref _selectedRow, value))
                return;

            SelectedFiles.Clear();
            foreach (var file in value?.Session.Files ?? Array.Empty<string>())
                SelectedFiles.Add(new SessionFileViewModel(file));

            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(CanEditSelection));
        }
    }

    public bool HasSelection => _selectedRow is not null;

    public bool CanEditSelection => HasSelection && CanChange;

    /// <summary>What the last action did, when it went well ("Opened 4 files from Tower B").</summary>
    public string? StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    /// <summary>What went wrong with the last action, or null.</summary>
    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetProperty(ref _errorMessage, value))
                OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    /// <summary>The confirmation Delete asks, naming the session and saying the PDFs are untouched.</summary>
    public string DeleteConfirmation => _selectedRow is null
        ? ""
        : $"Delete the session \"{_selectedRow.Name}\"?\n\nOnly the session is deleted. Its files stay where they are.";

    /// <summary>Opens every PDF in the selected session.</summary>
    public void OpenSelected()
    {
        if (_selectedRow is not { } row)
            return;

        Report(row, _launcher.Open(row.Session));
    }

    /// <summary>Opens one PDF of the selected session.</summary>
    public void OpenFile(SessionFileViewModel? file)
    {
        if (_selectedRow is not { } row || file is null)
            return;

        Report(row, _launcher.Open(row.Session, new[] { file.Path }));
    }

    /// <summary>Deletes the selected session (the view asks first) and selects the next one.</summary>
    public void DeleteSelected()
    {
        if (_selectedRow is not { } row)
            return;

        var index = Rows.IndexOf(row);
        var result = _store.Delete(row.Id);
        ClearMessages();
        if (!result.Saved)
            ErrorMessage = result.UserMessage;
        else
            StatusMessage = $"Deleted \"{row.Name}\".";

        Reload(null);
        if (Rows.Count > 0)
            SelectedRow = Rows[Math.Clamp(index, 0, Rows.Count - 1)];
    }

    /// <summary>Rebuilds everything from the store, selecting <paramref name="selectId"/> if it is still listed.</summary>
    public void Reload(string? selectId)
    {
        var tags = _store.Tags;
        if (_selectedTag is not null && !tags.Any(t => string.Equals(t.Tag, _selectedTag, StringComparison.OrdinalIgnoreCase)))
            _selectedTag = null;

        TagFilters.Clear();
        TagFilters.Add(new TagFilterViewModel(null, "All tags", _selectedTag is null));
        foreach (var tag in tags)
            TagFilters.Add(new TagFilterViewModel(tag.Tag, $"{tag.Tag} ({tag.Sessions})",
                string.Equals(tag.Tag, _selectedTag, StringComparison.OrdinalIgnoreCase)));

        OnPropertyChanged(nameof(SelectedTag));
        OnPropertyChanged(nameof(HasAnySessions));
        ApplyFilter(selectId);
    }

    /// <summary>Called by the view after the editor saved: shows the session and any save failure.</summary>
    public void NoteSaved(string sessionId, string? persistenceMessage)
    {
        // A new session could be hidden by the current filter; show everything so it can be seen.
        _searchText = "";
        OnPropertyChanged(nameof(SearchText));
        _selectedTag = null;
        Reload(sessionId);
        ClearMessages();
        if (persistenceMessage is not null)
            ErrorMessage = persistenceMessage;
        else if (_selectedRow is { } row)
            StatusMessage = $"Saved \"{row.Name}\" with {FileCount(row.Session.Files.Count)}.";
    }

    /// <summary>True when <paramref name="session"/> passes <paramref name="search"/> and <paramref name="tag"/>: every word of the search in its name, a tag or a file name, and the tag among its tags.</summary>
    public static bool Matches(SessionSnapshot session, string? search, string? tag)
    {
        if (tag is not null && !session.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
            return false;

        var words = (search ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return words.All(word =>
            session.Name.Contains(word, StringComparison.CurrentCultureIgnoreCase) ||
            session.Tags.Any(t => t.Contains(word, StringComparison.CurrentCultureIgnoreCase)) ||
            session.Files.Any(f => DocumentPaths.FileName(f).Contains(word, StringComparison.CurrentCultureIgnoreCase)));
    }

    internal static string FileCount(int n) => n == 1 ? "1 file" : $"{n} files";

    private void ApplyFilter(string? selectId)
    {
        Rows.Clear();
        foreach (var session in _store.Sessions.Where(s => Matches(s, _searchText, _selectedTag)))
            Rows.Add(new SessionRowViewModel(session, _localZone));

        SelectedRow = Rows.FirstOrDefault(r => r.Id == selectId) ?? Rows.FirstOrDefault();
        OnPropertyChanged(nameof(ListPlaceholder));
        OnPropertyChanged(nameof(IsListEmpty));
    }

    private void Report(SessionRowViewModel row, SessionOpenOutcome outcome)
    {
        var selectedId = row.Id;
        ClearMessages();
        if (outcome.Launched.Count > 0)
            StatusMessage = $"Opened {FileCount(outcome.Launched.Count)} from \"{row.Name}\".";
        ErrorMessage = outcome.Summary;

        // Last opened changed, and with it the order.
        ApplyFilter(selectedId);
    }

    private void ClearMessages()
    {
        StatusMessage = null;
        ErrorMessage = null;
    }
}

/// <summary>One saved session in the Sessions list.</summary>
public sealed class SessionRowViewModel
{
    private readonly TimeZoneInfo _localZone;

    public SessionRowViewModel(SessionSnapshot session, TimeZoneInfo localZone)
    {
        Session = session;
        _localZone = localZone;
    }

    public SessionSnapshot Session { get; }

    public string Id => Session.Id;

    public string Name => Session.Name;

    public IReadOnlyList<string> Tags => Session.Tags;

    public bool HasTags => Session.Tags.Count > 0;

    /// <summary>"4 files · saved 28/09/2026 14:05 · opened 29/09/2026 09:12", in the user's own date format.</summary>
    public string DetailText
    {
        get
        {
            var text = $"{SessionsViewModel.FileCount(Session.Files.Count)} · saved {Local(Session.UpdatedAt)}";
            return Session.LastOpenedAt is { } opened ? $"{text} · opened {Local(opened)}" : text;
        }
    }

    private string Local(DateTimeOffset instant)
        => TimeZoneInfo.ConvertTime(instant, _localZone).DateTime.ToString("g", CultureInfo.CurrentCulture);
}

/// <summary>One PDF of the selected session.</summary>
public sealed class SessionFileViewModel
{
    public SessionFileViewModel(string path) => Path = path;

    public string Path { get; }

    public string FileName => DocumentPaths.FileName(Path);

    public string Folder => DocumentPaths.Folder(Path);
}

/// <summary>One choice in the tag filter.</summary>
public sealed class TagFilterViewModel : ObservableObject
{
    private bool _isSelected;

    public TagFilterViewModel(string? tag, string label, bool isSelected)
    {
        Tag = tag;
        Label = label;
        _isSelected = isSelected;
    }

    /// <summary>The tag, or null for All tags.</summary>
    public string? Tag { get; }

    public string Label { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
