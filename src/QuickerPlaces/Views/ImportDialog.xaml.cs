using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using QuickerPlaces.Models;
using QuickerPlaces.Services;
using QuickerPlaces.ViewModels;

namespace QuickerPlaces.Views;

/// <summary>
/// SI §6.6 — the second half of import, after MainViewModel has already
/// read the chosen file and filtered out colliding items via
/// PlacesService.GetImportCandidates. Presents the remaining candidates as
/// a checkbox grid (all pre-selected) and commits the checked ones via
/// PlacesService.CommitImport. See ExportDialog for the mirror-image flow.
/// </summary>
public partial class ImportDialog : Window
{
    private readonly PlacesService _placesService;
    private readonly ObservableCollection<SelectablePlaceViewModel> _items;

    private ImportDialog(IReadOnlyList<Place> candidates, PlacesService placesService)
    {
        InitializeComponent();

        _placesService = placesService;
        _items = new ObservableCollection<SelectablePlaceViewModel>(
            candidates.Select(p => new SelectablePlaceViewModel(p, isSelected: true)));

        ItemsGrid.ItemsSource = _items;

        var owner = Application.Current?.MainWindow;
        if (owner is not null && owner.IsLoaded && !ReferenceEquals(owner, this))
        {
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
    }

    /// <summary>Shows the dialog and returns the places actually imported — empty if the user cancelled or selected none.</summary>
    public static List<Place> Show(IReadOnlyList<Place> candidates, PlacesService placesService)
    {
        var dialog = new ImportDialog(candidates, placesService);
        dialog.ShowDialog();
        return dialog.ImportedPlaces;
    }

    private List<Place> ImportedPlaces { get; set; } = new();

    private void SelectAllButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _items)
            item.IsSelected = true;
    }

    private void SelectNoneButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _items)
            item.IsSelected = false;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = _items.Where(i => i.IsSelected).Select(i => i.Place).ToList();
        if (selected.Count == 0)
        {
            ShowError("Select at least one place to import.");
            return;
        }

        // The persistence outcome isn't surfaced here for the same reason
        // as PlaceFormDialog: PlacesService.HasUnsavedChanges is the
        // durable record a later step's banner reads from, so this dialog
        // doesn't need its own copy of it.
        var (imported, _) = _placesService.CommitImport(selected);
        ImportedPlaces = imported;
        DialogResult = true;
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
