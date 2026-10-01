using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using QuickerPlaces.Models;
using QuickerPlaces.Services;

namespace QuickerPlaces.Cli;

/// <summary>
/// The CLI's output shapes. Every name here is part of the contract
/// (apiVersion 1): fields are only ever added, never renamed or removed, so
/// an agent written against one build keeps working on the next. Times are
/// ISO 8601 in UTC; local dates are yyyy-MM-dd in the machine's time zone.
/// </summary>
public static class Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static readonly JsonSerializerOptions Pretty = new(Options) { WriteIndented = true };

    public static string Enum<T>(T value) where T : struct, System.Enum => JsonNamingPolicy.CamelCase.ConvertName(value.ToString());

    public static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// A place as every command shows it. <c>exists</c> is whether a folder is
    /// there now (null for a link). <c>opensTimed</c> is how many of
    /// <c>openCount</c> have a time in <c>opens</c>: opens before schema v4
    /// were counted but not timed. <c>opens</c> itself is included only when
    /// asked for (places get), as it can hold 500 entries.
    /// </summary>
    public static Dictionary<string, object?> Place(Place place, IShell shell, bool includeOpens = false)
    {
        var json = new Dictionary<string, object?>
        {
            ["id"] = place.Id,
            ["alias"] = place.Alias,
            ["type"] = Enum(place.Type),
            ["resource"] = place.Resource,
            ["exists"] = place.Type == PlaceType.Folder ? shell.DirectoryExists(place.Resource) : null,
            ["isFavourite"] = place.IsFavourite,
            ["tags"] = place.Tags,
            ["note"] = place.Note,
            ["dateAdded"] = place.DateAdded,
            ["lastOpenedAt"] = place.LastOpenedAt,
            ["openCount"] = place.OpenCount,
            ["opensTimed"] = place.Opens.Count,
            ["deletedAt"] = place.DeletedAt
        };
        if (includeOpens)
            json["opens"] = place.Opens;
        return json;
    }

    public static IEnumerable<Dictionary<string, object?>> Places(IEnumerable<Place> places, IShell shell) => places.Select(p => Place(p, shell));
}
