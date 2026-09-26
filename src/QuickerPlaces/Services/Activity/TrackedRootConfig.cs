using System;
using System.Collections.Generic;
using QuickerPlaces.Models.Activity;

namespace QuickerPlaces.Services.Activity;

/// <summary>
/// What the tracker needs to know about one enabled root (Phase 9 plan 5.3):
/// where it is, which other paths reach the same folder (D15, D22), how a
/// visit is rolled up (D8), and the two thresholds (D7, D9). The store's
/// persisted root (step 2) carries this beside its recorded days; the
/// tracker only ever sees this immutable view of it.
/// </summary>
public sealed record TrackedRootConfig(string RootId, string Path)
{
    /// <summary>Other prefixes the user said reach this root, such as a mapped drive's network path (D15, D22).</summary>
    public IReadOnlyList<string> EquivalentPrefixes { get; init; } = Array.Empty<string>();

    public RollupMode Rollup { get; init; } = RollupMode.RootChild;

    /// <summary>Levels below the root for <see cref="RollupMode.Depth"/>; ignored by the other modes.</summary>
    public int Depth { get; init; } = 1;

    /// <summary>How long a folder must stay in the foreground before its visit counts (D9).</summary>
    public TimeSpan DwellThreshold { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>How long without keyboard or mouse input before time stops accruing (D7).</summary>
    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromMinutes(5);
}
