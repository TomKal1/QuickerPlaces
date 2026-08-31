using QuickerPlaces.Models;
using QuickerPlaces.Mvvm;

namespace QuickerPlaces.ViewModels;

/// <summary>
/// A read-only Place plus a checkbox state — backs the checkbox lists in
/// both ExportDialog (SI §6.5) and ImportDialog (SI §6.6), which are
/// otherwise near-identical UIs ("show places, let the user tick any
/// number of them").
/// </summary>
public sealed class SelectablePlaceViewModel : ObservableObject
{
    private bool _isSelected;

    public SelectablePlaceViewModel(Place place, bool isSelected)
    {
        Place = place;
        _isSelected = isSelected;
    }

    public Place Place { get; }

    public string Alias => Place.Alias;

    public string TypeLabel => Place.Type == PlaceType.Folder ? "Folder" : "URL";

    public string Resource => Place.Resource;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
