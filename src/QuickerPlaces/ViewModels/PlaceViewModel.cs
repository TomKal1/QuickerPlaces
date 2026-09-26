using System;
using System.Globalization;
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
    public string TypeLabel => Model.Type.Label();

    /// <summary>
    /// Icon-font glyph for this place's Type, rendered with the theme's
    /// Font.Icons family: a folder for Folder, a globe for URL. A fixed
    /// glyph per type rather than a real favicon / shell icon, since
    /// fetching favicons would mean contacting every saved URL, which
    /// QuickerPlaces deliberately never does (SI §3).
    /// </summary>
    public string TypeGlyph => GlyphFor(Model.Type);

    public string Resource => Model.Resource;

    /// <summary>Hover text for a favourite bubble: the destination, since the bubble itself only shows the alias.</summary>
    public string ToolTipText => $"{TypeLabel}: {Model.Resource}";

    /// <summary>Segoe Fluent Icons / Segoe MDL2 Assets code points (shared by both fonts).</summary>
    public static string GlyphFor(PlaceType type) => type == PlaceType.Folder ? "\uE8B7" : "\uE774";

    public bool IsFavourite => Model.IsFavourite;

    public int? FavouriteOrder => Model.FavouriteOrder;

    /// <summary>The grid's clickable star: Segoe FavoriteStarFill for a favourite, the FavoriteStar outline otherwise.</summary>
    public string FavouriteGlyph => Model.IsFavourite ? "" : "";

    /// <summary>The star's hover text: what clicking it will do, with the keyboard equivalent.</summary>
    public string FavouriteToolTip => Model.IsFavourite ? "Remove from favourites (Ctrl+D)" : "Add to favourites (Ctrl+D)";

    /// <summary>
    /// When the place was added, in local time for the grid's {0:d} column.
    /// The model holds UTC; binding that directly would show tomorrow's
    /// date for an evening add east of Greenwich.
    /// </summary>
    public DateTime DateAdded => Model.DateAdded.LocalDateTime;

    /// <summary>When QuickerPlaces last launched this place (UTC), or null if never (Phase 3 D24).</summary>
    public DateTimeOffset? LastOpenedAt => Model.LastOpenedAt;

    /// <summary>How many times QuickerPlaces has launched this place — the grid's Opens column.</summary>
    public int OpenCount => Model.OpenCount;

    /// <summary>The grid's Last Opened column: "—" if never, otherwise local short date and time (D31).</summary>
    public string LastOpenedText => FormatLastOpened(Model.LastOpenedAt, TimeZoneInfo.Local, CultureInfo.CurrentCulture);

    /// <summary>
    /// Formats a last-opened instant in <paramref name="zone"/> with
    /// <paramref name="culture"/>'s short date and short time ("g"): date
    /// and time, because a launcher is used many times a day and a date
    /// alone would make every place opened today look alike. Formatted
    /// here rather than by a XAML StringFormat, which would ignore the
    /// user's regional settings (D31). The zone and culture are parameters
    /// so tests never depend on the machine's.
    /// </summary>
    public static string FormatLastOpened(DateTimeOffset? value, TimeZoneInfo zone, CultureInfo culture)
        => value is { } opened
            ? TimeZoneInfo.ConvertTime(opened, zone).DateTime.ToString("g", culture)
            : "—";

    /// <summary>
    /// Raises a property-changed notification for every property on this
    /// instance (PropertyName = string.Empty is the standard WPF-binding
    /// convention for "refresh everything bound to this source"). Call
    /// this after a PlacesService method has mutated <see cref="Model"/>
    /// directly.
    /// </summary>
    public void Refresh() => OnPropertyChanged(string.Empty);
}
