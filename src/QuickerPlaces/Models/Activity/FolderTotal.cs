using System;
using System.Text.Json.Serialization;

namespace QuickerPlaces.Models.Activity;

/// <summary>One folder's total on one day. Short JSON names, since there is one of these per folder per day.</summary>
public sealed class FolderTotal
{
    /// <summary>Foreground-and-active time, held in whole milliseconds and written as seconds ("s": 4321.5).</summary>
    [JsonPropertyName("s")]
    [JsonConverter(typeof(MillisecondsAsSecondsConverter))]
    public long Milliseconds { get; set; }

    [JsonPropertyName("v")]
    public int Visits { get; set; }

    /// <summary>When the folder was last in the foreground that day, in UTC: the grid's Last visited (D23).</summary>
    [JsonPropertyName("last")]
    public DateTimeOffset LastSeenAt { get; set; }
}
