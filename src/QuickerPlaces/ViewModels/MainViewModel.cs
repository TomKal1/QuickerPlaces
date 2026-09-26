using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using Microsoft.Win32;
using QuickerPlaces.Models;
using QuickerPlaces.Mvvm;
using QuickerPlaces.Services;
using QuickerPlaces.Views;

namespace QuickerPlaces.ViewModels;

/// <summary>
/// Backs MainWindow. Owns the Places grid collection, the favourite-bubble
/// projection of it, and every command the UI exposes. Dialog views
/// (PlaceFormDialog, ExportDialog, ImportDialog, RecentlyDeletedDialog,
/// MessageForm) are invoked
/// directly from here rather than through an IDialogService abstraction —
/// the same "isn't strict MVVM, but nothing here needs the extra layer"
/// tradeoff the template's MessageForm already made.
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly PlacesService _placesService;

    // Every launch goes through this (Phase 3 D23): it alone decides that
    // an open counts, and records it (D24).
    private readonly PlaceLauncher _launcher;

    private bool _isGridExpanded;
    private string _searchText = string.Empty;
    private string? _globalHotkeyText;
    private string? _statusMessage;
    private bool _statusOffersUndo;
    private PlaceSort? _currentSort;

    // Most recent removal on top. Session-only: undo history isn't saved;
    // after a restart, Recently Deleted is the way back (D19). Holds the
    // removed records themselves: their list slot and bubble slot are in
    // the record now (D7, D9), so a copy of either here could only go
    // stale (D10).
    private readonly Stack<Place> _undoStack = new();

    // Hides the status bar a few seconds after its last message: 10 s when
    // it offers Undo, 8 s otherwise (D19). Ctrl+Z still works after it's
    // gone, and Recently Deleted after that; the bar is just a reminder.
    private static readonly TimeSpan StatusWithUndoDuration = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan StatusDuration = TimeSpan.FromSeconds(8);
    private readonly DispatcherTimer _statusTimer = new() { Interval = StatusDuration };

    // True while the pointer is over the status bar or the keyboard focus
    // is inside it (D19), so reaching for Undo never races the timer.
    private bool _statusPaused;
    private bool _hasUnsavedChanges;
    private string? _persistenceMessage;

    public MainViewModel(AppSettings settings, PlacesService placesService)
    {
        _settings = settings;
        _placesService = placesService;
        _launcher = new PlaceLauncher(placesService, new WindowsShell());
        _isGridExpanded = settings.IsGridExpanded;

        Places = new ObservableCollection<PlaceViewModel>(_placesService.Places.Select(p => new PlaceViewModel(p)));
        FavouritePlaces = new ObservableCollection<PlaceViewModel>();

        // The grid binds to this filtered, sorted view rather than to Places
        // directly. Sorting is the view's too (Phase 3 D29), never the
        // collection's: Places must stay in stored order for
        // InsertRestored.
        PlacesView = CollectionViewSource.GetDefaultView(Places);
        PlacesView.Filter = item => item is PlaceViewModel place && PlaceSearch.Matches(place.Model, SearchText);
        // Listening on the view (not on Places) means the view has already
        // re-filtered by the time the header/empty-state text is recomputed.
        PlacesView.CollectionChanged += (_, _) => RaiseGridStatusChanged();

        // The sort remembered in settings.json (D30); anything it doesn't
        // recognise is the stored order.
        _currentSort = PlaceSort.Parse(settings.PlacesSortKey, settings.PlacesSortDirection);
        ApplySort();

        // Commands must exist before RebuildFavourites() runs below — it
        // calls ExportCommand.RaiseCanExecuteChanged(), and on a fresh
        // install (no saved places, so this constructor is the very first
        // thing that runs) there's nothing to mask that ordering mistake:
        // every launch hit it, not just first-run ones. Assigning every
        // command up front, then doing any command-dependent setup
        // afterward, keeps this class of bug from coming back.
        AddFolderCommand = new RelayCommand(() => AddPlace(PlaceType.Folder));
        AddUrlCommand = new RelayCommand(() => AddPlace(PlaceType.Url));
        OpenCommand = new RelayCommand(parameter => Open(parameter as PlaceViewModel));
        RenameAliasCommand = new RelayCommand(parameter => RenameAlias(parameter as PlaceViewModel));
        EditResourceCommand = new RelayCommand(parameter => EditResource(parameter as PlaceViewModel));
        ToggleFavouriteCommand = new RelayCommand(parameter => ToggleFavourite(parameter as PlaceViewModel));
        RemoveCommand = new RelayCommand(parameter => Remove(parameter as PlaceViewModel));
        ExportCommand = new RelayCommand(Export, () => Places.Count > 0);
        ImportCommand = new RelayCommand(Import);
        ToggleGridCommand = new RelayCommand(() => IsGridExpanded = !IsGridExpanded);
        OpenFavouriteAtCommand = new RelayCommand(parameter => OpenFavouriteAt(parameter));
        ClearSearchCommand = new RelayCommand(() => SearchText = string.Empty);
        OpenDataFolderCommand = new RelayCommand(() => ExplorerReveal.Reveal(_placesService.PlacesFilePath));
        CopyResourceCommand = new RelayCommand(parameter => CopyResource(parameter as PlaceViewModel));
        UndoRemoveCommand = new RelayCommand(UndoRemove, () => _undoStack.Count > 0);
        ShowRecentlyDeletedCommand = new RelayCommand(ShowRecentlyDeleted);
        DismissStatusCommand = new RelayCommand(() => ShowStatus(null));
        RetrySaveCommand = new RelayCommand(RetrySave);
        ShowLogCommand = new RelayCommand(ShowLog);

        _statusTimer.Tick += (_, _) => ShowStatus(null);

        RebuildFavourites();

        // Establishes the banner's initial state from whatever
        // PlacesService already knows (normally nothing — the constructor
        // above only loads and never persists — but this keeps
        // RefreshPersistenceState as the single place that ever sets
        // HasUnsavedChanges/PersistenceMessage, rather than leaving their
        // initial false/null values as an unstated special case).
        RefreshPersistenceState();
    }

    public string AppName => AppInfo.Name;

    public string Monogram => AppInfo.Monogram;

    public string SubHeaderText => GlobalHotkeyText is null
        ? "Your saved folders and links, one click away."
        : $"Your saved folders and links, one click away. Press {GlobalHotkeyText} from anywhere to search them.";

    /// <summary>The registered global hotkey ("Ctrl+Alt+Space"), or null if none is active. Set by MainWindow once registration succeeds.</summary>
    public string? GlobalHotkeyText
    {
        get => _globalHotkeyText;
        set
        {
            if (SetProperty(ref _globalHotkeyText, value))
                OnPropertyChanged(nameof(SubHeaderText));
        }
    }

    /// <summary>All stored places, in stored order. Never sorted: <see cref="PlacesView"/> is (D29).</summary>
    public ObservableCollection<PlaceViewModel> Places { get; }

    /// <summary>Favourited places only, ordered by FavouriteOrder — backs the bubble row above the grid.</summary>
    public ObservableCollection<PlaceViewModel> FavouritePlaces { get; }

    /// <summary>Places filtered by <see cref="SearchText"/> — what the DataGrid actually shows.</summary>
    public ICollectionView PlacesView { get; }

    /// <summary>The grid's sort, or null for stored order. Changed by <see cref="SortBy"/>; remembered in settings.json (D30).</summary>
    public PlaceSort? CurrentSort
    {
        get => _currentSort;
        private set => SetProperty(ref _currentSort, value);
    }

    /// <summary>
    /// A click on the column header for <paramref name="key"/>: its first
    /// direction, then flipping up and down (PlaceSort.Next).
    /// MainWindow calls this in place of the DataGrid's own sorting.
    /// </summary>
    public void SortBy(PlaceSortKey key)
    {
        CurrentSort = PlaceSort.Next(CurrentSort, key);
        ApplySort();
    }

    /// <summary>Returns the grid to stored order. MainWindow calls this for a remembered sort that no column shows any more, so a sort is never in force without its arrow.</summary>
    public void ClearSort()
    {
        CurrentSort = null;
        ApplySort();
    }

    /// <summary>
    /// Sorts the view by <see cref="CurrentSort"/>, or returns it to stored
    /// order. A CustomSort rather than SortDescriptions: it compares without
    /// reflection, and only a comparer can say "never opened is oldest" and
    /// break ties by alias (D29). Setting it refreshes the view.
    /// </summary>
    private void ApplySort()
    {
        if (PlacesView is not ListCollectionView view)
            return;

        view.CustomSort = CurrentSort is { } sort
            ? Comparer<object>.Create((a, b) => sort.Comparer.Compare(((PlaceViewModel)a).Model, ((PlaceViewModel)b).Model))
            : null;
    }

    /// <summary>The grid's search box text. Every whitespace-separated term must appear in the alias or path/URL (see <see cref="PlaceSearch"/>).</summary>
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? string.Empty))
            {
                PlacesView.Refresh();
                OnPropertyChanged(nameof(IsSearching));

                // Searching a hidden list would be pointless — bring it back.
                if (IsSearching && !IsGridExpanded)
                    IsGridExpanded = true;
            }
        }
    }

    public bool IsSearching => !string.IsNullOrWhiteSpace(SearchText);

    private int VisiblePlaceCount => PlacesView.Cast<object>().Count();

    /// <summary>"All Places (12)", or "All Places (3 of 12)" while a search is narrowing the grid.</summary>
    public string PlacesHeader => IsSearching
        ? $"All Places ({VisiblePlaceCount} of {Places.Count})"
        : $"All Places ({Places.Count})";

    /// <summary>Shown over the grid when it has no rows to show, explaining why; null when the grid has rows.</summary>
    public string? EmptyGridMessage
    {
        get
        {
            // Pointing at Recently Deleted when the list is empty only
            // because everything was removed (plan 5.3 row 24).
            if (Places.Count == 0)
                return _placesService.RecentlyDeleted.Count > 0
                    ? "No places yet. Use Add Folder (Ctrl+N) or Add URL (Ctrl+U) to save your first one. Places you removed are in Recently Deleted."
                    : "No places yet. Use Add Folder (Ctrl+N) or Add URL (Ctrl+U) to save your first one.";
            if (VisiblePlaceCount == 0)
                return $"No places match \"{SearchText.Trim()}\". Press Esc to clear the search.";
            return null;
        }
    }

    public bool IsGridExpanded
    {
        get => _isGridExpanded;
        set => SetProperty(ref _isGridExpanded, value);
    }

    /// <summary>
    /// Backs the unsaved-changes banner's visibility. Set only from
    /// RefreshPersistenceState, which reads it straight from
    /// PlacesService.HasUnsavedChanges rather than from any individual
    /// command's returned PersistenceResult — see RefreshPersistenceState's
    /// remarks for why that distinction matters.
    /// </summary>
    public bool HasUnsavedChanges
    {
        get => _hasUnsavedChanges;
        private set => SetProperty(ref _hasUnsavedChanges, value);
    }

    /// <summary>The banner's message text. Null exactly when HasUnsavedChanges is false — see RefreshPersistenceState.</summary>
    public string? PersistenceMessage
    {
        get => _persistenceMessage;
        private set => SetProperty(ref _persistenceMessage, value);
    }

    public RelayCommand AddFolderCommand { get; }
    public RelayCommand AddUrlCommand { get; }
    public RelayCommand OpenCommand { get; }
    public RelayCommand RenameAliasCommand { get; }
    public RelayCommand EditResourceCommand { get; }
    public RelayCommand ToggleFavouriteCommand { get; }
    public RelayCommand RemoveCommand { get; }
    public RelayCommand ExportCommand { get; }
    public RelayCommand ImportCommand { get; }
    public RelayCommand ToggleGridCommand { get; }

    /// <summary>Opens the favourite at a 0-based position in the bubble row (Ctrl+1 → "0"). Out-of-range positions do nothing.</summary>
    public RelayCommand OpenFavouriteAtCommand { get; }

    public RelayCommand ClearSearchCommand { get; }

    /// <summary>Copies a place's path/URL to the clipboard.</summary>
    public RelayCommand CopyResourceCommand { get; }

    /// <summary>Restores the most recently removed place (Ctrl+Z, or the status bar's Undo). Repeatable, most recent first.</summary>
    public RelayCommand UndoRemoveCommand { get; }

    public RelayCommand DismissStatusCommand { get; }

    /// <summary>Opens Recently Deleted (the header's bin button, D20).</summary>
    public RelayCommand ShowRecentlyDeletedCommand { get; }

    /// <summary>A short note shown in the bar under the grid ("Moved "Docs" to Recently Deleted."), or null to hide the bar.</summary>
    public string? StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    /// <summary>Whether the status bar shows an Undo button for its message.</summary>
    public bool StatusOffersUndo
    {
        get => _statusOffersUndo;
        private set => SetProperty(ref _statusOffersUndo, value);
    }

    /// <summary>Reveals places.json in Explorer — the header's folder button and the unsaved-changes banner's "Show Data Folder". Quarantined places.corrupt-*.json files and places.bak.json sit beside it.</summary>
    public RelayCommand OpenDataFolderCommand { get; }

    public RelayCommand RetrySaveCommand { get; }
    public RelayCommand ShowLogCommand { get; }

    public string PlacesFilePath => _placesService.PlacesFilePath;

    private void AddPlace(PlaceType type)
    {
        // PlaceFormDialog discards the PersistenceResult from TryAdd
        // internally (it only needs the ValidationResult to decide whether
        // to close), so the banner state has to be re-read from
        // PlacesService afterward rather than from a result passed back
        // here — there isn't one.
        var created = PlaceFormDialog.ShowAdd(type, _placesService);
        RefreshPersistenceState();

        if (created is null)
            return;

        Places.Add(new PlaceViewModel(created));
        ClearSearchIfHidden(created);
        // A brand-new place is never a favourite yet, but rebuilding here
        // costs nothing at this scale and keeps this method simple.
        RebuildFavourites();
    }

    private void Open(PlaceViewModel? place)
    {
        if (place is null)
            return;

        var outcome = _launcher.Open(place.Model);
        switch (outcome.Status)
        {
            case OpenStatus.Missing:
                MessageForm.Show(
                    $"This folder no longer exists:\n{place.Resource}",
                    AppName, MessageFormButtons.OK, MessageFormIcon.Warning);
                return;

            case OpenStatus.Failed:
                // Fail gracefully (SI §6.3) — a malformed or no-longer-openable
                // resource should never crash the app.
                MessageForm.Show(
                    $"Couldn't open \"{place.Alias}\":\n{outcome.ErrorMessage}",
                    AppName, MessageFormButtons.OK, MessageFormIcon.Error);
                return;
        }

        // Launched, so the open was recorded (D24) unless recovery blocked
        // it. A failed save of that record shows the banner, never undoes the
        // launch (D26). The row and its bubble share this view model, so one
        // Refresh updates both.
        RefreshPersistenceState(outcome.Persistence);
        place.Refresh();

        // Only a usage sort can change because of an open, so only it pays
        // for re-sorting the view; the row moves to its new place at once
        // (D32). No other sort sees a reset.
        if (CurrentSort?.Key is PlaceSortKey.LastOpened or PlaceSortKey.Opens)
            PlacesView.Refresh();
    }

    private void RenameAlias(PlaceViewModel? place)
    {
        if (place is null)
            return;

        var renamed = PlaceFormDialog.ShowRenameAlias(place.Model, _placesService);
        RefreshPersistenceState();
        if (renamed)
        {
            place.Refresh();
            // The new alias may no longer match (or may now match) the search.
            PlacesView.Refresh();
        }
    }

    private void EditResource(PlaceViewModel? place)
    {
        if (place is null)
            return;

        var edited = PlaceFormDialog.ShowEditResource(place.Model, _placesService);
        RefreshPersistenceState();
        if (edited)
        {
            place.Refresh();
            PlacesView.Refresh();
        }
    }

    private void ToggleFavourite(PlaceViewModel? place)
    {
        if (place is null)
            return;

        var persistence = _placesService.ToggleFavourite(place.Model);
        RefreshPersistenceState(persistence);
        place.Refresh();
        RebuildFavourites();
    }

    /// <summary>
    /// Moves a place to Recently Deleted, without asking (roadmap §4.8,
    /// D18): nothing is lost, since Undo (Ctrl+Z, all session) and Recently
    /// Deleted (seven days) both bring it back exactly.
    /// </summary>
    private void Remove(PlaceViewModel? place)
    {
        if (place is null)
            return;

        var persistence = _placesService.Remove(place.Model, out var removed);
        RefreshPersistenceState(persistence);

        // Blocked by an unresolved recovery (not normally reachable: the
        // window only opens once recovery is resolved), or already removed
        // — nothing changed, so there is nothing to offer Undo for.
        if (!removed)
            return;

        Places.Remove(place);
        RebuildFavourites();

        _undoStack.Push(place.Model);
        UndoRemoveCommand.RaiseCanExecuteChanged();
        ShowStatus($"Moved \"{place.Alias}\" to Recently Deleted.", offerUndo: true);
    }

    private void UndoRemove()
    {
        if (_undoStack.Count == 0)
            return;

        var place = _undoStack.Pop();
        UndoRemoveCommand.RaiseCanExecuteChanged();

        // The entry is dropped from the stack whatever happens below, not
        // kept: leaving it would jam Ctrl+Z on this one entry and make every
        // older removal unreachable. Each outcome says so, so the next
        // Ctrl+Z moving on isn't a surprise.
        var next = _undoStack.Count > 0 ? " Ctrl+Z restores the one removed before it." : string.Empty;

        var result = _placesService.TryRestore(place, out var persistence);
        RefreshPersistenceState(persistence);
        if (!result.Success)
        {
            // Its alias or path/URL is now used by an active place: the
            // D15 flow explains which, and lets the user change them.
            if (_placesService.GetRestoreConflict(place) is { } conflict)
            {
                var restored = PlaceFormDialog.ShowRestore(conflict, _placesService);
                // The dialog discards its save result, as for every edit.
                RefreshPersistenceState();
                if (!restored)
                {
                    // Cancelled: the reason was on screen a moment ago, and
                    // the place is not lost.
                    ShowStatus($"\"{place.Alias}\" is still in Recently Deleted.{next}");
                    return;
                }
            }
            else
            {
                // Purged after seven days, deleted permanently, or already
                // restored from Recently Deleted: nothing to edit.
                MessageForm.Show((result.ErrorMessage ?? "That place can't be restored.") +
                    (next.Length > 0 ? "\n\n" + next.TrimStart() : string.Empty),
                    AppName, MessageFormButtons.OK, MessageFormIcon.Warning);
                return;
            }
        }

        InsertRestored(place);
        foreach (var favourite in Places.Where(p => p.IsFavourite))
            favourite.Refresh();

        ClearSearchIfHidden(place);
        RebuildFavourites();

        ShowStatus($"Restored \"{place.Alias}\".{next}");
    }

    private void ShowRecentlyDeleted() => RecentlyDeletedDialog.Show(_placesService, SyncWithRecentlyDeleted);

    /// <summary>
    /// Runs after each Recently Deleted action, while the dialog is still
    /// open, and brings the main window up to date with it: a row for each
    /// place it restored (at its stored position), favourites and their
    /// numbering (a restored favourite shifts the bubbles after it), the
    /// banner — read from PlacesService.HasUnsavedChanges, since the action
    /// saved or failed to — and the Undo stack, which must no longer offer
    /// anything the dialog restored or deleted.
    /// </summary>
    private void SyncWithRecentlyDeleted(IReadOnlyList<Place> restored)
    {
        RefreshPersistenceState();

        foreach (var place in restored)
            InsertRestored(place);

        foreach (var favourite in Places.Where(p => p.IsFavourite))
            favourite.Refresh();

        RebuildFavourites();
        if (restored.Any(p => !PlaceSearch.Matches(p, SearchText)))
            SearchText = string.Empty;

        var undoChanged = PruneUndoStack();

        // Recently Deleted may have emptied, which changes the empty-grid hint.
        RaiseGridStatusChanged();

        if (restored.Count > 0)
            ShowStatus(restored.Count == 1 ? $"Restored \"{restored[0].Alias}\"." : $"Restored {restored.Count} places.");
        else if (undoChanged && StatusOffersUndo)
            // Its Undo would now restore a different place than the one it names.
            ShowStatus(null);
    }

    /// <summary>
    /// Keeps only the Undo entries still in Recently Deleted, in their
    /// order, so Ctrl+Z never tries to restore a place the dialog already
    /// restored or deleted for good (plan 5.4). Returns whether anything
    /// was dropped.
    /// </summary>
    private bool PruneUndoStack()
    {
        var stillDeleted = new HashSet<Place>(_placesService.RecentlyDeleted, ReferenceEqualityComparer.Instance);
        // A Stack enumerates top first; push the survivors back bottom first.
        var kept = _undoStack.Where(stillDeleted.Contains).Reverse().ToList();
        if (kept.Count == _undoStack.Count)
            return false;

        _undoStack.Clear();
        foreach (var place in kept)
            _undoStack.Push(place);

        UndoRemoveCommand.RaiseCanExecuteChanged();
        return true;
    }

    /// <summary>
    /// Adds a row for a place PlacesService has just restored, at its
    /// position among the active places (plan 5.3 row 23), so Places stays
    /// in the same order as the stored list. The record never left its
    /// stored slot (D7), and Places mirrors the active records in stored
    /// order, so the row goes after every row whose place comes before it
    /// there. Counting only places that already have a row keeps this right
    /// when a batch restore (D22) hands back several places at once, in an
    /// order that is not the list's.
    /// </summary>
    private void InsertRestored(Place place)
    {
        var shown = new HashSet<Place>(Places.Select(vm => vm.Model), ReferenceEqualityComparer.Instance);
        // Places is a fresh snapshot per call (D8): read it once.
        var index = _placesService.Places.TakeWhile(p => !ReferenceEquals(p, place)).Count(shown.Contains);
        Places.Insert(index, new PlaceViewModel(place));
    }

    private void CopyResource(PlaceViewModel? place)
    {
        if (place is null)
            return;

        try
        {
            Clipboard.SetText(place.Resource);
            ShowStatus($"Copied {(place.Type == PlaceType.Folder ? "the path" : "the URL")} of \"{place.Alias}\".");
        }
        catch (ExternalException ex)
        {
            // Another app can hold the clipboard open for a moment
            // (clipboard managers, remote desktop); report it, don't crash.
            MessageForm.Show(
                $"Couldn't copy to the clipboard, because another app is using it. Try again in a moment.\n\n{ex.Message}",
                AppName, MessageFormButtons.OK, MessageFormIcon.Warning);
        }
    }

    /// <summary>
    /// Shows <paramref name="message"/> in the status bar, or hides the bar
    /// when null. It stays 10 seconds when it offers Undo and 8 otherwise,
    /// counted afresh for each new message and not at all while paused
    /// (D19). Never moves the keyboard focus.
    /// </summary>
    private void ShowStatus(string? message, bool offerUndo = false)
    {
        _statusTimer.Stop();

        // A hidden bar can't be hovered or hold the focus. Clearing the
        // pause whenever the bar hides or reappears means a MouseLeave that
        // never arrives (the bar collapsed under a click on its own
        // Dismiss button) can't leave the next message stuck on screen.
        if (message is null || StatusMessage is null)
            _statusPaused = false;

        StatusMessage = message;
        StatusOffersUndo = message is not null && offerUndo;
        _statusTimer.Interval = StatusOffersUndo ? StatusWithUndoDuration : StatusDuration;
        if (message is not null && !_statusPaused)
            _statusTimer.Start();
    }

    /// <summary>Holds the status bar open: the pointer is over it or the keyboard focus is inside it (D19). Called by MainWindow.</summary>
    public void PauseStatusTimer()
    {
        _statusPaused = true;
        _statusTimer.Stop();
    }

    /// <summary>Lets the status bar time out again, with its full interval, once neither the pointer nor the focus is on it (D19). Called by MainWindow.</summary>
    public void ResumeStatusTimer()
    {
        if (!_statusPaused)
            return;

        _statusPaused = false;
        if (StatusMessage is not null)
            _statusTimer.Start();
    }

    private void Export()
    {
        if (Places.Count == 0)
        {
            MessageForm.Show("There's nothing to export yet.", AppName);
            return;
        }

        ExportDialog.Show(Places.Select(p => p.Model).ToList(), _placesService, AppName);
    }

    private void Import()
    {
        var openDialog = new OpenFileDialog
        {
            Title = "Import Places",
            Filter = "QuickerPlaces export (*.json)|*.json|All files (*.*)|*.*"
        };

        if (openDialog.ShowDialog() != true)
            return;

        var (candidates, errorMessage) = _placesService.GetImportCandidates(openDialog.FileName);
        if (errorMessage is not null)
        {
            MessageForm.Show(errorMessage, AppName, MessageFormButtons.OK, MessageFormIcon.Error);
            return;
        }

        if (candidates.Count == 0)
        {
            MessageForm.Show(
                "Nothing to import — every place in that file already exists here (or the file is empty).",
                AppName);
            return;
        }

        // ImportDialog discards CommitImport's PersistenceResult the same
        // way PlaceFormDialog does, so this refreshes from PlacesService
        // afterward rather than from a returned result.
        var imported = ImportDialog.Show(candidates, _placesService);
        RefreshPersistenceState();
        if (imported.Count == 0)
            return;

        foreach (var place in imported)
            Places.Add(new PlaceViewModel(place));

        if (imported.Any(p => !PlaceSearch.Matches(p, SearchText)))
            SearchText = string.Empty;

        RebuildFavourites();
        MessageForm.Show($"{imported.Count} imported.", AppName);
    }

    private void OpenFavouriteAt(object? parameter)
    {
        var index = parameter switch
        {
            int i => i,
            string text when int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => -1
        };

        if (index >= 0 && index < FavouritePlaces.Count)
            Open(FavouritePlaces[index]);
    }

    /// <summary>
    /// A place the user just added should never be invisible because of a
    /// leftover search, so drop the search if it would hide the new row.
    /// </summary>
    private void ClearSearchIfHidden(Place added)
    {
        if (!PlaceSearch.Matches(added, SearchText))
            SearchText = string.Empty;
    }

    private void RaiseGridStatusChanged()
    {
        OnPropertyChanged(nameof(PlacesHeader));
        OnPropertyChanged(nameof(EmptyGridMessage));
    }

    /// <summary>Reprojects FavouritePlaces from Places, ordered by FavouriteOrder. Cheap at this app's scale (SI §9 — hundreds of rows at most), so every mutating command just calls this rather than patching the projection incrementally.</summary>
    private void RebuildFavourites()
    {
        var ordered = Places
            .Where(p => p.IsFavourite)
            .OrderBy(p => p.FavouriteOrder ?? int.MaxValue)
            .ToList();

        FavouritePlaces.Clear();
        foreach (var place in ordered)
            FavouritePlaces.Add(place);

        ExportCommand.RaiseCanExecuteChanged();
    }

    /// <summary>
    /// Applies a favourite-bubble drag-reorder: moves <paramref
    /// name="dragged"/> to <paramref name="targetIndex"/> within the
    /// favourites, persists the new FavouriteOrder for all favourites, and
    /// refreshes the projection. Called from MainWindow's drag/drop
    /// code-behind, which owns the drop-position geometry.
    /// </summary>
    public void MoveFavourite(PlaceViewModel dragged, int targetIndex)
    {
        var ordered = FavouritePlaces.ToList();
        if (!ordered.Remove(dragged))
            return;

        targetIndex = Math.Clamp(targetIndex, 0, ordered.Count);
        ordered.Insert(targetIndex, dragged);

        var persistence = _placesService.SetFavouriteOrder(ordered.Select(vm => vm.Model).ToList());
        RefreshPersistenceState(persistence);

        FavouritePlaces.Clear();
        foreach (var place in ordered)
        {
            FavouritePlaces.Add(place);
            place.Refresh();
        }
    }

    /// <summary>Copies live view-model state back into the AppSettings instance App.xaml.cs will persist on exit. Places data is already saved continuously by PlacesService — this only covers UI chrome.</summary>
    public void PersistToSettings()
    {
        _settings.IsGridExpanded = IsGridExpanded;

        // Both null is the stored order (D30).
        var sort = CurrentSort?.Format();
        _settings.PlacesSortKey = sort?.Key;
        _settings.PlacesSortDirection = sort?.Direction;
    }

    /// <summary>
    /// Retries the current in-memory store's save (plan 5.2) and refreshes
    /// the banner from the outcome.
    /// </summary>
    private void RetrySave()
    {
        var persistence = _placesService.RetrySave();
        RefreshPersistenceState(persistence);
    }

    /// <summary>
    /// Opens the diagnostic log with its associated app — the "Show Log"
    /// banner action. DiagnosticLog creates its file lazily on first
    /// write, so on a machine where nothing has failed yet the file may
    /// not exist: launching a missing path would throw, and revealing an
    /// empty (or not-yet-created) logs folder would just be confusing, so
    /// this says plainly that there's nothing to show yet instead.
    /// </summary>
    private void ShowLog()
    {
        var logPath = DiagnosticLog.LogFilePath;

        if (!File.Exists(logPath))
        {
            MessageForm.Show("Nothing has been logged yet.", AppName);
            return;
        }

        try
        {
            // UseShellExecute so Windows opens it with whatever the user
            // has associated with .log files — the same approach
            // WindowsShell uses for a place's own resource.
            Process.Start(new ProcessStartInfo(logPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageForm.Show(
                $"Couldn't open the log file:\n{ex.Message}",
                AppName, MessageFormButtons.OK, MessageFormIcon.Error);
        }
    }

    /// <summary>
    /// The single place that ever sets HasUnsavedChanges/PersistenceMessage
    /// (plan 5.2). HasUnsavedChanges is always read straight from
    /// PlacesService.HasUnsavedChanges — never assigned from a returned
    /// PersistenceResult's Saved flag — because PersistenceResult.Ok() is
    /// also what a mutation rejected by validation returns (see its doc
    /// comment: nothing was attempted, so nothing failed to persist).
    /// Assigning from a returned result directly would clear an existing
    /// banner the moment the user typed an invalid alias into an unrelated
    /// dialog. PlacesService.HasUnsavedChanges only ever changes inside
    /// Persist(), so reading it here is the one source of truth for
    /// "is there an unsaved change" — a real successful save is the only
    /// thing that can turn it false.
    ///
    /// <paramref name="result"/> is the PersistenceResult from a mutation
    /// that just ran directly (ToggleFavourite, Remove, TryRestore,
    /// SetFavouriteOrder, RetrySave) and supplies the banner's message text when it failed
    /// just now. The dialog-mediated mutations (AddPlace, RenameAlias,
    /// EditResource, Import, Undo's Restore Place, Recently Deleted)
    /// discard or keep their PersistenceResult inside the
    /// dialog and call this with no argument — when there is still an
    /// unsaved change but no fresh failure message to show, this falls
    /// back to a standing sentence naming the store file rather than
    /// leaving the banner blank or reusing a stale message from a
    /// different failure.
    /// </summary>
    private void RefreshPersistenceState(PersistenceResult? result = null)
    {
        HasUnsavedChanges = _placesService.HasUnsavedChanges;

        if (!HasUnsavedChanges)
        {
            PersistenceMessage = null;
            return;
        }

        if (result is { Saved: false, UserMessage: { } message })
            PersistenceMessage = message;
        else if (PersistenceMessage is null)
            PersistenceMessage = $"Some changes to \"{_placesService.PlacesFilePath}\" haven't been saved yet.";
    }
}
