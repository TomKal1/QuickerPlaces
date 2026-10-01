using System;
using System.Collections.Generic;

namespace QuickerPlaces.Services;

/// <summary>
/// The one rule for a place's tags (schema v4): each trimmed, blanks dropped,
/// and the first spelling kept when two differ only by case. Used by
/// PlacesService wherever tags enter the store — SetTags, import and load.
/// </summary>
public static class PlaceTags
{
    /// <summary>The longest tag kept; a longer one is cut to this length.</summary>
    public const int MaxLength = 64;

    public static List<string> Normalise(IEnumerable<string?>? tags)
    {
        var result = new List<string>();
        if (tags is null)
            return result;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in tags)
        {
            var tag = raw?.Trim();
            if (string.IsNullOrEmpty(tag))
                continue;
            if (tag.Length > MaxLength)
                tag = tag[..MaxLength].TrimEnd();
            if (seen.Add(tag))
                result.Add(tag);
        }
        return result;
    }

    /// <summary>A note as stored: trimmed, or null when nothing is left.</summary>
    public static string? NormaliseNote(string? note)
    {
        var trimmed = note?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
