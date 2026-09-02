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

        RebuildFavourites();
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

    public string PlacesFilePath => _placesService.PlacesFilePath;

    private void AddPlace(PlaceType type)
    {
        var created = PlaceFormDialog.ShowAdd(type, _placesService);
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
        if (renamed)
            place.Refresh();
    }

    private void EditResource(PlaceViewModel? place)
    {
        if (place is null)
            return;

        var edited = PlaceFormDialog.ShowEditResource(place.Model, _placesService);
        if (edited)
            place.Refresh();
    }

    private void ToggleFavourite(PlaceViewModel? place)
    {
        if (place is null)
            return;

        // The returned PersistenceResult isn't consumed here — a banner
        // that reads it is a later step. PlacesService.HasUnsavedChanges
        // is the durable state that survives until then.
        _placesService.ToggleFavourite(place.Model);
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

        // As above — the PersistenceResult is discarded here; the banner
        // that would read it is a later step.
        _placesService.Remove(place.Model);
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

        var imported = ImportDialog.Show(candidates, _placesService);
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

        // As above — the PersistenceResult is discarded here; the banner
        // that would read it is a later step.
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
