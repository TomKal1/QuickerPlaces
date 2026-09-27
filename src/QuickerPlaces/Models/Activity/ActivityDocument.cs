using System.Collections.Generic;

namespace QuickerPlaces.Models.Activity;

/// <summary>
/// The root of activity.json (Phase 9 plan 5.2): every tracked root's
/// configuration and recorded days, in one machine-local file with its own
/// schema version (D11, D18). Never part of places.json or a places export.
/// </summary>
public sealed class ActivityDocument
{
    /// <summary>ActivityStore sets this from its CurrentSchemaVersion on every write and checks it on every load.</summary>
    public int SchemaVersion { get; set; } = 1;

    public List<TrackedRoot> Roots { get; set; } = new();
}
