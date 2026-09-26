using System;
using System.Collections.Generic;
using System.ComponentModel;
using QuickerPlaces.Models;

namespace QuickerPlaces.Services;

/// <summary>
/// What the places grid can be sorted by (Phase 3 D29): every column, plus
/// DateAdded, which has no column but is one of Phase 5's tab sort modes
/// (Recent = LastOpened, Frequent = Opens, A–Z = Alias, Path = Destination,
/// Date Added). Its names are what settings.json stores (D30), so renaming
/// one silently drops a remembered sort; Destination rather than Resource
/// so that Phase 4's relabel of that column changes nothing here.
/// </summary>
public enum PlaceSortKey
{
    Alias,
    Type,
    Destination,
    Favourite,
    LastOpened,
    Opens,
    DateAdded
}

/// <summary>
/// An active sort of the places grid (D29). "No sort" — the stored order —
/// is a null <c>PlaceSort?</c>, never a value of this type. UI-free and
/// linked into the test project: MainViewModel applies <see cref="Comparer"/>
/// to its view, and remembers the sort through <see cref="Format"/> and
/// <see cref="Parse"/>.
/// </summary>
public readonly record struct PlaceSort(PlaceSortKey Key, ListSortDirection Direction)
{
    /// <summary>
    /// The direction a column starts in when first clicked: the useful end
    /// first — most recent, most opened, favourites, newest added — and A–Z
    /// for the text columns.
    /// </summary>
    public static ListSortDirection FirstDirection(PlaceSortKey key) => key switch
    {
        PlaceSortKey.Favourite or PlaceSortKey.LastOpened or PlaceSortKey.Opens or PlaceSortKey.DateAdded => ListSortDirection.Descending,
        _ => ListSortDirection.Ascending
    };

    /// <summary>
    /// The header-click cycle: another column starts in its first direction;
    /// the same column again reverses; a third time returns to no sort. The
    /// third state exists because stored order is the default, and without
    /// it there would be no way back to it.
    /// </summary>
    public static PlaceSort? Next(PlaceSort? current, PlaceSortKey clicked)
    {
        var first = FirstDirection(clicked);

        if (current is not { } sort || sort.Key != clicked)
            return new PlaceSort(clicked, first);

        return sort.Direction == first
            ? new PlaceSort(clicked, Opposite(first))
            : null;
    }

    /// <summary>
    /// Compares places by <see cref="Key"/> in <see cref="Direction"/>, then —
    /// always ascending — by alias and then by destination (ordinal), so the
    /// order is total and never shuffles between refreshes. A null
    /// LastOpenedAt is older than any date, so never-opened places sit at
    /// the old end whichever way the column is sorted.
    /// </summary>
    public IComparer<Place> Comparer => new PlaceComparer(Key, Direction);

    /// <summary>
    /// Reads the two settings.json strings (D30) case-insensitively. Missing
    /// or unrecognised — a hand edit, or a key a later build added — means no
    /// sort, never an error: a bad value costs only the sort.
    /// </summary>
    public static PlaceSort? Parse(string? key, string? direction)
    {
        // Enum.TryParse would also accept a number ("3"), which Format never
        // writes; only a defined name counts.
        if (!Enum.TryParse<PlaceSortKey>(key, ignoreCase: true, out var parsedKey) ||
            !Enum.IsDefined(parsedKey) ||
            int.TryParse(key, out _))
        {
            return null;
        }

        if (string.Equals(direction, "ascending", StringComparison.OrdinalIgnoreCase))
            return new PlaceSort(parsedKey, ListSortDirection.Ascending);
        if (string.Equals(direction, "descending", StringComparison.OrdinalIgnoreCase))
            return new PlaceSort(parsedKey, ListSortDirection.Descending);

        return null;
    }

    /// <summary>The two strings <see cref="Parse"/> reads: the key's name, and "ascending" or "descending".</summary>
    public (string Key, string Direction) Format()
        => (Key.ToString(), Direction == ListSortDirection.Ascending ? "ascending" : "descending");

    private static ListSortDirection Opposite(ListSortDirection direction)
        => direction == ListSortDirection.Ascending ? ListSortDirection.Descending : ListSortDirection.Ascending;

    private sealed class PlaceComparer(PlaceSortKey key, ListSortDirection direction) : IComparer<Place>
    {
        private static readonly StringComparer Text = StringComparer.CurrentCultureIgnoreCase;

        public int Compare(Place? x, Place? y)
        {
            if (ReferenceEquals(x, y))
                return 0;
            if (x is null)
                return -1;
            if (y is null)
                return 1;

            var primary = CompareByKey(x, y);
            if (primary != 0)
                return direction == ListSortDirection.Ascending ? primary : -primary;

            var alias = key == PlaceSortKey.Alias ? 0 : Text.Compare(x.Alias, y.Alias);
            return alias != 0 ? alias : string.CompareOrdinal(x.Resource, y.Resource);
        }

        private int CompareByKey(Place x, Place y) => key switch
        {
            PlaceSortKey.Alias => Text.Compare(x.Alias, y.Alias),
            PlaceSortKey.Type => Text.Compare(x.Type.Label(), y.Type.Label()),
            PlaceSortKey.Destination => Text.Compare(x.Resource, y.Resource),
            PlaceSortKey.Favourite => x.IsFavourite.CompareTo(y.IsFavourite),
            // Nullable<T>.Compare puts null first: never opened is oldest.
            PlaceSortKey.LastOpened => Nullable.Compare(x.LastOpenedAt, y.LastOpenedAt),
            PlaceSortKey.Opens => x.OpenCount.CompareTo(y.OpenCount),
            PlaceSortKey.DateAdded => x.DateAdded.CompareTo(y.DateAdded),
            _ => 0
        };
    }
}
