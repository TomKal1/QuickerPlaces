using System;
using QuickerPlaces.Models;

namespace QuickerPlaces.Services;

/// <summary>
/// The matching rule behind the grid's search box. Kept UI-free (and out
/// of MainViewModel) so the rule is unit-testable and so there's one
/// definition of "matches" should anything else ever need it.
/// </summary>
public static class PlaceSearch
{
    private static readonly char[] TermSeparators = { ' ', '\t' };

    /// <summary>
    /// True if <paramref name="place"/> matches <paramref name="query"/>.
    /// The query is split on whitespace and every term must appear
    /// (case-insensitive substring) in either the Alias or the
    /// Path/URL, so "wiki prod" finds "Prod Wiki" as well as an alias
    /// "Wiki" pointing at https://prod.example.com. An empty or
    /// whitespace-only query matches everything.
    /// </summary>
    public static bool Matches(Place place, string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return true;

        var terms = query.Split(TermSeparators, StringSplitOptions.RemoveEmptyEntries);
        foreach (var term in terms)
        {
            var found =
                place.Alias.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                place.Resource.Contains(term, StringComparison.OrdinalIgnoreCase);

            if (!found)
                return false;
        }

        return true;
    }
}
