using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using QuickerPlaces.Mvvm;
using QuickerPlaces.Services;
using QuickerPlaces.Services.Documents;
using QuickerPlaces.Services.Revit;
using QuickerPlaces.Services.Revit.Opening;
using QuickerPlaces.Services.Sessions;

namespace QuickerPlaces.ViewModels;

/// <summary>
/// Backs the Sessions window (sessions plan §5): the saved sessions, the
/// search box and tag filter over them, the selected session's files, and the
/// open and delete actions. With a Revit open coordinator, a session's Revit
/// files open the Revit-safe way (a central as a new local, roadmap §4.21),
/// one at a time, after its other files, as the Library opens them. SessionsWindow is a thin view over this, as
/// RecentlyDeletedDialog is over its view model (Phase 2 D21): it binds,
/// asks before deleting, and shows the editor dialog.
///
/// UI-free and linked into the test project.
/// </summary>
public sealed class SessionsViewModel : ObservableObject
{
    /// <summary>Shown in place of the list when nothing has been saved.</summary>
    public const string EmptyMessage = "No sessions yet. Open the files you're working on, then choose Save files.";

    /// <summary>Shown in place of the list when the search or tag hides every session.</summary>
    public const string NoMatchesMessage = "No sessions match. Clear the search or choose All tags.";

    private readonly SessionStore _store;
    private readonly SessionLauncher _launcher;
    private readonly TimeZoneInfo _localZone;
    private readonly IRevitOpenCoordinator? _revitOpener;
    private readonly RevitReleaseCache? _revitReleases;
    private CancellationTokenSource? _revitOpenCancel;
    private string _searchText = "";
    private string? _selectedTag;
    private SessionRowViewModel? _selectedRow;
    private string? _statusMessage;
    private string? _errorMessage;
    private bool _rebuilding;

    /// <param name="localZone">The zone dates are shown in; null for the machine's own. Tests pass a fixed one.</param>
    /// <param name="allowsNoSelection">See <see cref="AllowsNoSelection"/>; given here so the first card isn't selected before it is known.</param>
    /// <param name="revitOpener">Opens Revit files the Revit-safe way; null hands them to Windows like any other file.</param>
    /// <param name="revitReleases">Reads which Revit files are centrals, for <see cref="SessionFileViewModel.IsRevitCentral"/>; null marks none.</param>
    public SessionsViewModel(SessionStore store, SessionLauncher launcher, TimeZoneInfo? localZone = null, bool allowsNoSelection = false,
        IRevitOpenCoordinator? revitOpener = null, RevitReleaseCache? revitReleases = null)
    {
        _store = store;
        _launcher = launcher;
        _revitOpener = revitOpener;
        _revitReleases = revitReleases;
        _localZone = localZone ?? TimeZoneInfo.Local;
        AllowsNoSelection = allowsNoSelection;
        Reload(null);
    }

    /// <summary>The sessions the search and tag let through, in the saved order (dragged on the cards).</summary>
    public ObservableCollection<SessionRowViewModel> Rows { get; } = new();

    /// <summary>"All tags" and then every tag in use, for the filter.</summary>
    public ObservableCollection<TagFilterViewModel> TagFilters { get; } = new();

    /// <summary>Shown when a Revit open is asked for while another is still running.</summary>
    public const string RevitBusyMessage = "A Revit file is already opening. Wait for it, or cancel it first.";

    /// <summary>The selected session's files, in the session's order.</summary>
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

    /// <summary>
    /// True in the workspace, where the selected card filters the File viewer: no
    /// card is selected until one is chosen, and Esc or Delete leaves none selected.
    /// False (the default) keeps the first card selected, as the Sessions window does.
    /// </summary>
    public bool AllowsNoSelection { get; set; }

    public SessionRowViewModel? SelectedRow
    {
        get => _selectedRow;
        set
        {
            // The list clears its selection while the cards are rebuilt: not a choice, so not kept.
            if (_rebuilding && value is null)
                return;

            if (!SetProperty(ref _selectedRow, value))
                return;

            SelectedFiles.Clear();
            foreach (var file in value?.Session.Files ?? Array.Empty<string>())
                SelectedFiles.Add(new SessionFileViewModel(file));
            RevitKindsRead = ReadRevitKindsAsync(SelectedFiles.ToArray());

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

    /// <summary>Opens every file in the selected session: Revit files last, one at a time, when a coordinator is given.</summary>
    public void OpenSelected()
    {
        if (_selectedRow is not { } row)
            return;

        Open(row, null);
    }

    /// <summary>Opens one file of the selected session.</summary>
    public void OpenFile(SessionFileViewModel? file)
    {
        if (_selectedRow is not { } row || file is null)
            return;

        Open(row, new[] { file.Path });
    }

    /// <summary>True while a Revit file is opening (starting Revit, waiting for the handler); Cancel shows then.</summary>
    public bool IsRevitOpening => _revitOpenCancel is not null;

    /// <summary>The Revit opens that are running or ran last; tests await this.</summary>
    public Task RevitOpen { get; private set; } = Task.CompletedTask;

    /// <summary>The read of the selected files' Revit kinds that is running or ran last; tests await this.</summary>
    public Task RevitKindsRead { get; private set; } = Task.CompletedTask;

    /// <summary>Asks the running Revit opens to stop. A request the handler has already claimed carries on in Revit.</summary>
    public void CancelRevitOpen()
    {
        if (_revitOpenCancel is not { } cancel)
            return;
        StatusMessage = "Cancelling…";
        try
        {
            cancel.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // It finished at that moment.
        }
    }

    private void Open(SessionRowViewModel row, IEnumerable<string>? files)
    {
        var outcome = _launcher.Open(row.Session, files, holdRevit: _revitOpener is not null);
        Report(row, outcome);
        if (outcome.HeldRevit.Count == 0)
            return;

        if (IsRevitOpening)
        {
            ErrorMessage = Join(outcome.Summary, RevitBusyMessage);
            return;
        }

        RevitOpen = OpenRevitAsync(outcome.HeldRevit, StatusMessage, ErrorMessage);
    }

    /// <summary>
    /// Opens each held Revit file through the coordinator in turn. A failure is
    /// reported and the next still opens; a cancel stops the rest. The status
    /// and error lines the other files left are kept in front of the Revit result.
    /// </summary>
    private async Task OpenRevitAsync(IReadOnlyList<string> files, string? earlierStatus, string? earlierError)
    {
        var cancel = new CancellationTokenSource();
        _revitOpenCancel = cancel;
        OnPropertyChanged(nameof(IsRevitOpening));

        // Status text arrives from the opener's thread; the context captured here is the UI thread's.
        var progress = new ContextProgress(text =>
        {
            if (ReferenceEquals(_revitOpenCancel, cancel))
                StatusMessage = text;
        });

        var opened = new List<RevitOpenReport>();
        var errors = new List<string>();
        RevitOpenReport? last = null;
        foreach (var file in files)
        {
            RevitOpenReport report;
            try
            {
                report = await _revitOpener!.OpenAsync(file, progress, cancel.Token);
            }
            catch (Exception ex)
            {
                // The coordinator reports its own problems; this is for one that escaped.
                DiagnosticLog.Error("Opening a Revit file from a session failed.", ex);
                report = new RevitOpenReport(new RevitOpenOutcome(RevitOpenResult.Failed, ex.Message),
                    $"Couldn't open \"{DocumentPaths.FileName(file)}\": {ex.Message}", true);
            }

            last = report;
            if (report.IsError)
                errors.Add(report.Message);
            else if (report.Outcome.Result != RevitOpenResult.Cancelled)
                opened.Add(report);
            if (report.Outcome.Result == RevitOpenResult.Cancelled || cancel.IsCancellationRequested)
                break;
        }

        _revitOpenCancel = null;
        cancel.Dispose();
        OnPropertyChanged(nameof(IsRevitOpening));

        string? revitStatus;
        if (last?.Outcome.Result == RevitOpenResult.Cancelled)
            revitStatus = last.Message;
        else if (files.Count == 1)
            revitStatus = opened.Count == 1 ? opened[0].Message : null;
        else
            revitStatus = opened.Count == 0 ? null
                : opened.Count == files.Count ? $"Opened {files.Count} Revit files."
                : $"Opened {opened.Count} of {files.Count} Revit files.";

        StatusMessage = Join(earlierStatus, revitStatus);
        ErrorMessage = Join(earlierError, errors.Count == 0 ? null : string.Join(" ", errors));
    }

    private static string? Join(string? first, string? second) =>
        first is null ? second : second is null ? first : $"{first} {second}";

    /// <summary>Marks which of <paramref name="files"/> are Revit centrals, read off the calling thread.</summary>
    private async Task ReadRevitKindsAsync(IReadOnlyList<SessionFileViewModel> files)
    {
        var revit = files.Where(f => DocumentKinds.FromPath(f.Path) == DocumentKind.Revit).ToArray();
        if (_revitReleases is not { } cache || revit.Length == 0)
            return;

        try
        {
            var infos = await Task.Run(() => cache.Refresh(revit.Select(f => f.Path)));
            foreach (var file in revit)
                file.IsRevitCentral = infos.TryGetValue(file.Path, out var info) &&
                    info is { Problem: RevitFileProblem.None, Worksharing: RevitWorksharing.Central };
        }
        catch (Exception ex)
        {
            // Only the badge is lost; opening still reads the file itself.
            DiagnosticLog.Warn($"Reading a session's Revit files failed ({ex.GetType().Name}).");
        }
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
        if (Rows.Count > 0 && !AllowsNoSelection)
            SelectedRow = Rows[Math.Clamp(index, 0, Rows.Count - 1)];
    }

    /// <summary>
    /// A card dragged onto another (or onto empty space, with a null target, for the
    /// end): it takes that card's place in the saved order, so its Ctrl+Shift number
    /// changes with it. An explicit before/after choice follows the drop preview.
    /// Reloads the cards, keeping the selection.
    /// </summary>
    public void Move(SessionRowViewModel dragged, SessionRowViewModel? target, bool? after = null)
    {
        if (ReferenceEquals(dragged, target) || dragged.Id == target?.Id)
            return;

        var result = _store.Move(dragged.Id, target?.Id, after);
        ClearMessages();
        if (!result.Saved)
            ErrorMessage = result.UserMessage;
        else if (_store.Find(dragged.Id)?.ShortcutDigit is { } digit)
            StatusMessage = $"\"{dragged.Name}\" opens with Ctrl+Shift+{digit}.";

        Reload(_selectedRow?.Id);
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

    /// <summary>
    /// Selects the session with <paramref name="sessionId"/>, for an action asked
    /// for elsewhere (the File viewer's Sessions tab). A search or tag filter that
    /// hides it is cleared first. False when no such session exists.
    /// </summary>
    public bool Select(string sessionId)
    {
        if (_store.Sessions.All(s => s.Id != sessionId))
            return false;

        if (Rows.All(r => r.Id != sessionId))
        {
            _searchText = "";
            OnPropertyChanged(nameof(SearchText));
            _selectedTag = null;
            Reload(sessionId);
        }
        else
        {
            SelectedRow = Rows.First(r => r.Id == sessionId);
        }

        return SelectedRow?.Id == sessionId;
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

    /// <summary>Called by the view after a session was shared: says where the file went, to send on.</summary>
    public void NoteShared(string sessionName, string filePath)
    {
        ClearMessages();
        StatusMessage = $"Shared \"{sessionName}\" as \"{filePath}\". Send that file to anyone with QuickerPlaces; they choose Open shared to use it.";
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
        _rebuilding = true;
        try
        {
            Rows.Clear();
            foreach (var session in _store.Sessions.Where(s => Matches(s, _searchText, _selectedTag)))
                Rows.Add(new SessionRowViewModel(session, _localZone));
        }
        finally
        {
            _rebuilding = false;
        }

        SelectedRow = Rows.FirstOrDefault(r => r.Id == selectId) ?? (AllowsNoSelection ? null : Rows.FirstOrDefault());
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

        // Last opened changed, so the cards are read again.
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

    /// <summary>"4 files · saved 28/09/2026 · opened 29/09/2026", dates only, in the user's own date format.</summary>
    public string DetailText
    {
        get
        {
            var text = $"{SessionsViewModel.FileCount(Session.Files.Count)} · saved {Local(Session.UpdatedAt)}";
            return Session.LastOpenedAt is { } opened ? $"{text} · opened {Local(opened)}" : text;
        }
    }

    /// <summary>Session details shown when hovering its compact card.</summary>
    public string ToolTipText
    {
        get
        {
            var details = DetailText;
            return HasTags ? $"{details}{Environment.NewLine}Tags: {string.Join(", ", Tags)}" : details;
        }
    }

    /// <summary>The number on the card's badge ("3" for Ctrl+Shift+3), or null past the ninth card.</summary>
    public string? ShortcutText => Session.ShortcutDigit?.ToString(CultureInfo.InvariantCulture);

    public bool HasShortcut => Session.ShortcutDigit is not null;

    /// <summary>"Ctrl+Shift+3 opens this session", or null past the ninth card.</summary>
    public string? ShortcutToolTip => Session.ShortcutDigit is { } n ? $"Ctrl+Shift+{n} opens this session" : null;

    private string Local(DateTimeOffset instant)
        => TimeZoneInfo.ConvertTime(instant, _localZone).DateTime.ToString("d", CultureInfo.CurrentCulture);
}

/// <summary>One PDF of the selected session.</summary>
public sealed class SessionFileViewModel : ObservableObject
{
    private bool _isRevitCentral;

    public SessionFileViewModel(string path) => Path = path;

    /// <summary>True once the file has been read as a Revit central: it opens as a new local.</summary>
    public bool IsRevitCentral
    {
        get => _isRevitCentral;
        set => SetProperty(ref _isRevitCentral, value);
    }

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
