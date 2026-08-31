using System;
using QuickerPlaces.Models;
using QuickerPlaces.Mvvm;

namespace QuickerPlaces.ViewModels;

/// <summary>
/// Bindable wrapper around a <see cref="Place"/>. The underlying Place is a
/// plain POCO (kept that way so it serializes cleanly via System.Text.Json
/// in PlacesService) and is sometimes mutated directly by PlacesService
/// methods (rename, favourite toggle, reorder) rather than through this
/// wrapper's own setters — callers that invoke a PlacesService mutation on
/// this item's <see cref="Model"/> should call <see cref="Refresh"/>
/// afterward so the UI picks up the change. Used for both DataGrid rows
/// and favourite bubbles — the same instance backs both, so toggling
/// Favourite from either place is instantly reflected in the other.
/// </summary>
public sealed class PlaceViewModel : ObservableObject
{
    public PlaceViewModel(Place model)
    {
        Model = model;
    }

    /// <summary>The underlying persisted record. PlacesService mutates this directly for rename/edit/favourite/reorder operations.</summary>
    public Place Model { get; }

    public string Alias => Model.Alias;

    public PlaceType Type => Model.Type;

    /// <summary>"Folder" or "URL" — for the DataGrid's Type column.</summary>
    public string TypeLabel => Model.Type == PlaceType.Folder ? "Folder" : "URL";

    public string Resource => Model.Resource;

    public bool IsFavourite => Model.IsFavourite;

    public int? FavouriteOrder => Model.FavouriteOrder;

    public DateTime DateAdded => Model.DateAdded;

    /// <summary>
    /// Raises a property-changed notification for every property on this
    /// instance (PropertyName = string.Empty is the standard WPF-binding
    /// convention for "refresh everything bound to this source"). Call
    /// this after a PlacesService method has mutated <see cref="Model"/>
    /// directly.
    /// </summary>
    public void Refresh() => OnPropertyChanged(string.Empty);
}
