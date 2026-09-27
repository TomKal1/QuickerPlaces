using System;
using System.Collections.Generic;

namespace QuickerPlaces.Models.Activity;

/// <summary>
/// One tracked root: its configuration and its recorded data together
/// (Phase 9 plan D18), so deleting a root and its data is one write. Keys
/// of <see cref="Days"/> and <see cref="DayTotals"/> are local dates (D19).
/// Changed only by ActivityStore.
/// </summary>
public sealed class TrackedRoot
{
    /// <summary>Stable identity, a GUID in "N" format; what the tracker's intervals carry.</summary>
    public string RootId { get; set; } = "";

    /// <summary>The folder as the user chose it, normalized (RootPathMatcher.Normalize). Every folder is recorded under this spelling (5.2).</summary>
    public string Path { get; set; } = "";

    /// <summary>Other prefixes that reach the same folder, such as a mapped drive's network path (D15, D22).</summary>
    public List<string> EquivalentPrefixes { get; set; } = new();

    /// <summary>False when the user stopped tracking but kept the data (5.5).</summary>
    public bool Enabled { get; set; } = true;

    public RollupMode Rollup { get; set; } = RollupMode.RootChild;

    public int Depth { get; set; } = 1;

    public int DwellThresholdSeconds { get; set; } = 5;

    public int IdleTimeoutMinutes { get; set; } = 5;

    /// <summary>When the user confirmed tracking, in UTC. Nothing before it is shown as zero, only as "not tracked" (§1).</summary>
    public DateTimeOffset TrackingStartedAt { get; set; }

    /// <summary>Per-folder detail, kept 62 days.</summary>
    public Dictionary<DateOnly, DayActivity> Days { get; set; } = new();

    /// <summary>Per-day totals, kept 365 days, read only by the calendar (D20).</summary>
    public Dictionary<DateOnly, DayTotal> DayTotals { get; set; } = new();
}
