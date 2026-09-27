using System.Text.Json.Serialization;

namespace QuickerPlaces.Models.Activity;

/// <summary>
/// One local day's total under a root, kept 365 days for the calendar (D20)
/// after the per-folder detail has gone. While both exist, these are the
/// sums of that day's <see cref="FolderTotal"/>s.
/// </summary>
public sealed class DayTotal
{
    [JsonPropertyName("s")]
    [JsonConverter(typeof(MillisecondsAsSecondsConverter))]
    public long Milliseconds { get; set; }

    [JsonPropertyName("v")]
    public int Visits { get; set; }

    /// <summary>How many folders had time that day: the calendar's "in 9 folders", which outlives the detail.</summary>
    [JsonPropertyName("f")]
    public int Folders { get; set; }
}
