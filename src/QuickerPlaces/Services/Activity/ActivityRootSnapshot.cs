using System;
using QuickerPlaces.Models.Activity;

namespace QuickerPlaces.Services.Activity;

/// <summary>
/// A copy of one tracked root's configuration, taken under the store's lock
/// (D26): what the Activity window lists, and, for an enabled root, what the
/// tracker is given.
/// </summary>
public sealed record ActivityRootSnapshot(TrackedRootConfig Config, bool Enabled, DateTimeOffset TrackingStartedAt)
{
    public string RootId => Config.RootId;

    public string Path => Config.Path;

    public string TrackingToolTip => $"{Path}\nFolder grouping: " + (Config.Rollup switch
    {
        RollupMode.Exact => "All subfolders",
        RollupMode.Depth => $"Chosen depth below root (level {Config.Depth})",
        _ => "First folder below root"
    });
}
