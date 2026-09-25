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
/// (PlaceFormDialog, ExportDialog, ImportDialog, MessageForm) are invoked
/// directly from here rather than through an IDialogService abstraction —
/// the same "isn't strict MVVM, but nothing here needs the extra layer"
/// tradeoff the template's MessageForm already made.
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly PlacesService _placesService;

    private bool _isGridExpanded;
    private string _searchText = string.Empty;
    private string? _globalHotkeyText;
    private string? _statusMessage;
    private bool _statusOffersUndo;

    // Most recent removal on top. Session-only: undo history isn't saved.
    private readonly Stack<RemovedPlace> _removedPlaces = new();

    // Hides the status bar a few seconds after its last message. Ctrl+Z
    // still works after it's gone; the bar is just a reminder.
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(8) };

    public MainViewModel(AppSettings settings, PlacesService placesService)
    {
        _settings = settings;
        _placesService = placesService;
        _isGridExpanded = settings.IsGridExpanded;

        Places = new ObservableCollection<PlaceViewModel>(_placesService.Places.Select(p => new PlaceViewModel(p)));
        FavouritePlaces = new ObservableCollection<PlaceViewModel>();

        // The grid binds to this filtered view rather than to Places
        // directly. It's the collection's default view, so the DataGrid's
        // own column-header sorting keeps working on top of the filter.
        PlacesView = CollectionViewSource.GetDefaultView(Places);
        PlacesView.Filter = item => item is PlaceViewModel place && PlaceSearch.Matches(place.Model, SearchText);
        // Listening on the view (not on Places) means the view has already
        // re-filtered by the time the header/empty-state text is recomputed.
        PlacesView.CollectionChanged += (_, _) => RaiseGridStatusChanged();

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
        OpenDataFolderCommand = new RelayCommand(OpenDataFolder);
        CopyResourceCommand = new RelayCommand(parameter => CopyResource(parameter as PlaceViewModel));
        UndoRemoveCommand = new RelayCommand(UndoRemove, () => _removedPlaces.Count > 0);
        DismissStatusCommand = new RelayCommand(() => ShowStatus(null));

        _statusTimer.Tick += (_, _) => ShowStatus(null);

        RebuildFavourites();

        _placesService.SaveFailed += OnSaveFailed;
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

    /// <summary>All stored places, in insertion order — the DataGrid's built-in column-header sorting covers everything beyond that.</summary>
    public ObservableCollection<PlaceViewModel> Places { get; }

    /// <summary>Favourited places only, ordered by FavouriteOrder — backs the bubble row above the grid.</summary>
    public ObservableCollection<PlaceViewModel> FavouritePlaces { get; }

    /// <summary>Places filtered by <see cref="SearchText"/> — what the DataGrid actually shows.</summary>
    public ICollectionView PlacesView { get; }

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
            if (Places.Count == 0)
                return "No places yet. Use Add Folder (Ctrl+N) or Add URL (Ctrl+U) to save your first one.";
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

    /// <summary>A short note shown in the bar under the grid ("Removed "Docs"."), or null to hide the bar.</summary>
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

    /// <summary>Opens the folder holding places.json (and any places.corrupt-*.json backups) in File Explorer.</summary>
    public RelayCommand OpenDataFolderCommand { get; }

    /// <summary>
    /// True if the places file couldn't be read on startup. MainWindow
    /// checks this once (on Loaded) and shows a MessageForm notice — kept
    /// as a plain property rather than firing the notice from the
    /// constructor so a themed owner window exists to center the dialog on.
    /// </summary>
    public bool PlacesLoadFailed => _placesService.LoadFailed;

    public string PlacesFilePath => _placesService.PlacesFilePath;

    /// <summary>Where a copy of the unreadable places file was saved on startup, or null if none was made. Shown in the load-failure notice.</summary>
    public string? CorruptFileBackupPath => _placesService.CorruptFileBackupPath;

    /// <summary>
    /// Warns that a change only exists in memory. Posted to the dispatcher
    /// rather than shown inline, because the failing save can happen in
    /// the middle of a dialog's own Save click (PlaceFormDialog,
    /// ImportDialog); this lets that dialog finish closing first.
    /// </summary>
    private void OnSaveFailed(string errorMessage)
    {
        Application.Current?.Dispatcher.InvokeAsync(() => MessageForm.Show(
            errorMessage + "\n\nYour changes are kept while QuickerPlaces stays open, and it will keep trying to save them.",
            AppName, MessageFormButtons.OK, MessageFormIcon.Warning));
    }

    private void AddPlace(PlaceType type)
    {
        var created = PlaceFormDialog.ShowAdd(type, _placesService);
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

        try
        {
            if (place.Type == PlaceType.Folder && !Directory.Exists(place.Resource))
            {
                MessageForm.Show(
                    $"This folder no longer exists:\n{place.Resource}",
                    AppName, MessageFormButtons.OK, MessageFormIcon.Warning);
                return;
            }

            // UseShellExecute lets Windows pick the right handler either
            // way: Explorer for a folder path, the default browser for a
            // URL — no need to branch on Type here.
            Process.Start(new ProcessStartInfo(place.Resource) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            // Fail gracefully (SI §6.3) — a malformed or no-longer-openable
            // resource should never crash the app.
            MessageForm.Show(
                $"Couldn't open \"{place.Alias}\":\n{ex.Message}",
                AppName, MessageFormButtons.OK, MessageFormIcon.Error);
        }
    }

    private void OpenDataFolder()
    {
        try
        {
            // Highlight places.json itself when it exists; before the first
            // save there's no file yet, so just open the folder.
            var startInfo = File.Exists(PlacesFilePath)
                ? new ProcessStartInfo("explorer.exe", $"/select,\"{PlacesFilePath}\"")
                : new ProcessStartInfo(Path.GetDirectoryName(PlacesFilePath) ?? PlacesFilePath) { UseShellExecute = true };

            Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            MessageForm.Show(
                $"Couldn't open the data folder:\n{ex.Message}",
                AppName, MessageFormButtons.OK, MessageFormIcon.Error);
        }
    }

    private void RenameAlias(PlaceViewModel? place)
    {
        if (place is null)
            return;

        var renamed = PlaceFormDialog.ShowRenameAlias(place.Model, _placesService);
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

        _placesService.ToggleFavourite(place.Model);
        place.Refresh();
        RebuildFavourites();
    }

    private void Remove(PlaceViewModel? place)
    {
        if (place is null)
            return;

        var confirm = MessageForm.Show(
            $"Remove \"{place.Alias}\"?\n\nYou can undo this with Ctrl+Z while QuickerPlaces stays open.",
            AppName, MessageFormButtons.YesNo, MessageFormIcon.Question);

        if (confirm != MessageFormResult.Yes)
            return;

        var removed = _placesService.Remove(place.Model);
        Places.Remove(place);
        RebuildFavourites();

        if (removed is null)
            return;

        _removedPlaces.Push(removed);
        UndoRemoveCommand.RaiseCanExecuteChanged();
        ShowStatus($"Removed \"{place.Alias}\".", offerUndo: true);
    }

    private void UndoRemove()
    {
        if (_removedPlaces.Count == 0)
            return;

        var removed = _removedPlaces.Pop();
        UndoRemoveCommand.RaiseCanExecuteChanged();

        var result = _placesService.TryRestore(removed);
        if (!result.Success)
        {
            // Dropped from the stack, not kept: leaving it would jam Ctrl+Z
            // on this one entry and make every older removal unreachable.
            // The message says so, so the next Ctrl+Z moving on isn't a surprise.
            var next = _removedPlaces.Count > 0 ? "\n\nCtrl+Z will now restore the place removed before it." : string.Empty;
            MessageForm.Show((result.ErrorMessage ?? "That place can't be restored.") + next,
                AppName, MessageFormButtons.OK, MessageFormIcon.Warning);
            return;
        }

        // PlacesService reinserted it at its old position; mirror that so
        // Places stays in the same order as the stored list.
        var restored = new PlaceViewModel(removed.Place);
        Places.Insert(Math.Clamp(removed.Index, 0, Places.Count), restored);
        foreach (var favourite in Places.Where(p => p.IsFavourite))
            favourite.Refresh();

        ClearSearchIfHidden(removed.Place);
        RebuildFavourites();

        ShowStatus(_removedPlaces.Count > 0
            ? $"Restored \"{removed.Place.Alias}\". Ctrl+Z restores the one removed before it."
            : $"Restored \"{removed.Place.Alias}\".");
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

    /// <summary>Shows <paramref name="message"/> in the status bar for a few seconds, or hides the bar when null.</summary>
    private void ShowStatus(string? message, bool offerUndo = false)
    {
        _statusTimer.Stop();
        StatusMessage = message;
        StatusOffersUndo = message is not null && offerUndo;
        if (message is not null)
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

        var imported = ImportDialog.Show(candidates, _placesService);
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

        _placesService.SetFavouriteOrder(ordered.Select(vm => vm.Model).ToList());

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
    }
}
