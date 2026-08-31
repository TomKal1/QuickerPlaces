using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using Microsoft.Win32;
using QuickerPlaces.Models;
using QuickerPlaces.Services;
using QuickerPlaces.ViewModels;

namespace QuickerPlaces.Views;

/// <summary>
/// SI §6.5 — presents the full list of stored places as a checkbox grid
/// (all pre-selected, since "export everything" is the common case) and
/// writes the checked ones to a user-chosen JSON file via
/// PlacesService.Export. See ImportDialog for the mirror-image flow.
/// </summary>
public partial class ExportDialog : Window
{
    private readonly PlacesService _placesService;
    private readonly string _appName;
    private readonly ObservableCollection<SelectablePlaceViewModel> _items;

    private ExportDialog(IReadOnlyList<Place> places, PlacesService placesService, string appName)
    {
        InitializeComponent();

        _placesService = placesService;
        _appName = appName;
        _items = new ObservableCollection<SelectablePlaceViewModel>(
            places.Select(p => new SelectablePlaceViewModel(p, isSelected: true)));

        ItemsGrid.ItemsSource = _items;

        var owner = Application.Current?.MainWindow;
        if (owner is not null && owner.IsLoaded && !ReferenceEquals(owner, this))
        {
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
    }

    public static void Show(IReadOnlyList<Place> places, PlacesService placesService, string appName)
        => new ExportDialog(places, placesService, appName).ShowDialog();

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

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = _items.Where(i => i.IsSelected).Select(i => i.Place).ToList();
        if (selected.Count == 0)
        {
            ShowError("Select at least one place to export.");
            return;
        }

        var saveDialog = new SaveFileDialog
        {
            Title = "Export Places",
            FileName = $"{AppInfo.Name}-Export.json",
            Filter = "JSON file (*.json)|*.json|All files (*.*)|*.*",
            DefaultExt = ".json"
        };

        if (saveDialog.ShowDialog(this) != true)
            return;

        var errorMessage = _placesService.Export(selected, saveDialog.FileName);
        if (errorMessage is not null)
        {
            ShowError(errorMessage);
            return;
        }

        MessageForm.Show($"Exported {selected.Count} place(s).", _appName);
        DialogResult = true;
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
