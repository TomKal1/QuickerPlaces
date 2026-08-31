using System;

namespace QuickerPlaces.Models;

/// <summary>
/// One remembered "place" — a folder path or URL filed under a memorable
/// Alias. This is the persisted record (see PlacesStore/PlacesService);
/// ViewModels/PlaceViewModel.cs wraps it for data binding.
/// </summary>
public sealed class Place
{
    /// <summary>User-facing unique name. Uniqueness is case-insensitive ("Docs" and "docs" collide) — enforced by PlacesService, not by this type.</summary>
    public required string Alias { get; set; }

    /// <summary>Whether <see cref="Resource"/> is a folder path or a URL. Determines validation rules and the Open action.</summary>
    public PlaceType Type { get; set; }

    /// <summary>Absolute folder path, or a URL. Duplicate-checked as an exact case-insensitive string match against other places of the same Type — deliberately not normalized (see SI §6.2).</summary>
    public required string Resource { get; set; }

    /// <summary>Whether this place renders as a one-click bubble above the grid.</summary>
    public bool IsFavourite { get; set; }

    /// <summary>User-controlled manual ordering for the favourite bubbles. Null when not a favourite; assigned/renumbered by PlacesService whenever the favourite set or its order changes.</summary>
    public int? FavouriteOrder { get; set; }

    /// <summary>When this place was first added. Internal bookkeeping — not necessarily shown in the UI.</summary>
    public DateTime DateAdded { get; set; } = DateTime.Now;
}
