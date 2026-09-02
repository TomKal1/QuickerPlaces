using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
    private bool _hasUnsavedChanges;
    private string? _persistenceMessage;

    public MainViewModel(AppSettings settings, PlacesService placesService)
    {
        _settings = settings;
        _placesService = placesService;
        _isGridExpanded = settings.IsGridExpanded;

        Places = new ObservableCollection<PlaceViewModel>(_placesService.Places.Select(p => new PlaceViewModel(p)));
        FavouritePlaces = new ObservableCollection<PlaceViewModel>();

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
        RetrySaveCommand = new RelayCommand(RetrySave);
        ShowDataFolderCommand = new RelayCommand(ShowDataFolder);
        ShowLogCommand = new RelayCommand(ShowLog);

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

    public string SubHeaderText => "Your saved folders and links, one click away.";

    /// <summary>All stored places, in insertion order — the DataGrid's built-in column-header sorting covers everything beyond that.</summary>
    public ObservableCollection<PlaceViewModel> Places { get; }

    /// <summary>Favourited places only, ordered by FavouriteOrder — backs the bubble row above the grid.</summary>
    public ObservableCollection<PlaceViewModel> FavouritePlaces { get; }

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
    public RelayCommand RetrySaveCommand { get; }
    public RelayCommand ShowDataFolderCommand { get; }
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

    private void RenameAlias(PlaceViewModel? place)
    {
        if (place is null)
            return;

        var renamed = PlaceFormDialog.ShowRenameAlias(place.Model, _placesService);
        RefreshPersistenceState();
        if (renamed)
            place.Refresh();
    }

    private void EditResource(PlaceViewModel? place)
    {
        if (place is null)
            return;

        var edited = PlaceFormDialog.ShowEditResource(place.Model, _placesService);
        RefreshPersistenceState();
        if (edited)
            place.Refresh();
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

    private void Remove(PlaceViewModel? place)
    {
        if (place is null)
            return;

        var confirm = MessageForm.Show(
            $"Remove \"{place.Alias}\"? This can't be undone.",
            AppName, MessageFormButtons.YesNo, MessageFormIcon.Question);

        if (confirm != MessageFormResult.Yes)
            return;

        var persistence = _placesService.Remove(place.Model);
        RefreshPersistenceState(persistence);
        Places.Remove(place);
        RebuildFavourites();
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

        RebuildFavourites();
        MessageForm.Show($"{imported.Count} imported.", AppName);
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

    /// <summary>Reveals places.json in Explorer, pre-selected — the "Show Data Folder" banner action.</summary>
    private void ShowDataFolder() => ExplorerReveal.Reveal(_placesService.PlacesFilePath);

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
            // has associated with .log files — the same approach Open()
            // uses for a place's own resource.
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
    /// that just ran directly (ToggleFavourite, Remove, SetFavouriteOrder,
    /// RetrySave) and supplies the banner's message text when it failed
    /// just now. The dialog-mediated mutations (AddPlace, RenameAlias,
    /// EditResource, Import) discard their PersistenceResult inside the
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
