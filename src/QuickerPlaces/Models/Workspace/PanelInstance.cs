using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuickerPlaces.Models.Workspace;

/// <summary>
/// One panel placed in a layout (configurable canvas plan D1, D6): which
/// kind of panel, where it sits in the order (its position in the list), how
/// many of the twelve columns it spans, and whether it is hidden. Never a
/// screen coordinate: the layout engine derives placement from the order,
/// the spans and the width available.
///
/// <see cref="Type"/> is a string, not an enum, so a type written by a newer
/// build survives a round trip through this one as an unavailable
/// placeholder instead of failing the whole file (D6). Unknown properties are
/// kept in <see cref="Extra"/> for the same reason. UI-free.
/// </summary>
public sealed class PanelInstance
{
    /// <summary>Stable within its layout. One panel per type today, but references use this, never the type (D6).</summary>
    public string Id { get; set; } = "";

    /// <summary>One of <see cref="PanelTypes"/>, or a type this build does not know.</summary>
    public string Type { get; set; } = "";

    /// <summary>Columns of twelve: 4, 6, 8 or 12 (<see cref="PanelSpans"/>).</summary>
    public int Span { get; set; } = PanelSpans.Full;

    /// <summary>Hidden panels keep their place and span so Show puts them back where they were.</summary>
    public bool Hidden { get; set; }

    /// <summary>Per-panel view choices (a grouping, a year/month view), as short named strings. Null when there are none.</summary>
    public Dictionary<string, string>? View { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    public PanelInstance Clone() => new()
    {
        Id = Id,
        Type = Type,
        Span = Span,
        Hidden = Hidden,
        View = View is null ? null : new Dictionary<string, string>(View),
        Extra = Extra is null ? null : new Dictionary<string, JsonElement>(Extra),
    };

    /// <summary>Same panel, place-independent: everything but <see cref="Extra"/>, which this build never changes.</summary>
    public bool SameAs(PanelInstance other)
        => Id == other.Id && Type == other.Type && Span == other.Span && Hidden == other.Hidden && SameView(View, other.View);

    private static bool SameView(Dictionary<string, string>? a, Dictionary<string, string>? b)
    {
        if ((a?.Count ?? 0) != (b?.Count ?? 0))
            return false;
        if (a is null || b is null)
            return true;

        foreach (var (key, value) in a)
        {
            if (!b.TryGetValue(key, out var other) || other != value)
                return false;
        }

        return true;
    }
}

/// <summary>The panel types this build knows (plan §1 in-scope panels).</summary>
public static class PanelTypes
{
    public const string Activity = "activity";
    public const string Shelf = "shelf";
    public const string Sessions = "sessions";
    public const string Places = "places";
    public const string Collections = "collections";
    public const string Searches = "searches";

    /// <summary>Every type this build can recognise, in Add panel order.</summary>
    public static readonly IReadOnlyList<string> Known = new[] { Activity, Shelf, Sessions, Places, Collections, Searches };

    /// <summary>
    /// The types that have a working panel. Collections and Saved searches
    /// arrive in M6; until then they are known (so a file naming them is not
    /// repaired away) but not offered, and neither is a built-in layout that
    /// needs them (plan M1: no dead placeholders shipped as features).
    /// </summary>
    public static readonly IReadOnlyList<string> Available = new[] { Activity, Shelf, Sessions, Places };

    public static bool IsKnown(string? type) => type is not null && ((IList<string>)Known).Contains(type);

    public static bool IsAvailable(string? type) => type is not null && ((IList<string>)Available).Contains(type);

    /// <summary>The panel's title, as Add panel and Undo name it.</summary>
    public static string DisplayName(string? type) => type switch
    {
        Activity => "Year activity",
        Shelf => "File shelf",
        Sessions => "Sessions",
        Places => "Saved places",
        Collections => "Collections",
        Searches => "Saved searches",
        _ => "Unavailable panel",
    };

    /// <summary>The span a newly added panel of this type starts with.</summary>
    public static int DefaultSpan(string type) => type switch
    {
        Activity => PanelSpans.Full,
        Shelf => PanelSpans.TwoThirds,
        _ => PanelSpans.Third,
    };
}

/// <summary>The widths a panel may take, in columns of twelve (plan D1).</summary>
public static class PanelSpans
{
    public const int Columns = 12;
    public const int Third = 4;
    public const int Half = 6;
    public const int TwoThirds = 8;
    public const int Full = 12;

    public static readonly IReadOnlyList<int> Allowed = new[] { Third, Half, TwoThirds, Full };

    public static bool IsAllowed(int span) => span is Third or Half or TwoThirds or Full;

    /// <summary>The nearest allowed span, the wider one on a tie, for repairing a hand-edited value.</summary>
    public static int Snap(int span)
    {
        var best = Full;
        foreach (var allowed in Allowed)
        {
            if (System.Math.Abs(allowed - span) <= System.Math.Abs(best - span))
                best = allowed;
        }

        return best;
    }
}
