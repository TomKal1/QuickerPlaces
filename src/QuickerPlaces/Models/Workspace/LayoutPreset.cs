using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuickerPlaces.Models.Workspace;

/// <summary>
/// A user's named layout, under My layouts (configurable canvas plan D2): its
/// panels and, only when the user chose Include current filters, a query
/// (D3). Built-in layouts are not stored as presets; they are code
/// (<c>BuiltInLayouts</c>) and cannot be overwritten or deleted.
///
/// The id, not the name, is what every reference uses, so renaming changes
/// nothing else. Unknown properties survive in <see cref="Extra"/>. UI-free.
/// </summary>
public sealed class LayoutPreset
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    /// <summary>
    /// <see cref="LayoutArrangements.Columns"/> for a layout in two columns;
    /// null (rows) otherwise. A value this build doesn't know reads as rows
    /// and is kept as written.
    /// </summary>
    public string? Arrangement { get; set; }

    public List<PanelInstance> Panels { get; set; } = new();

    /// <summary>The saved query, or null when the layout was saved without filters (the default).</summary>
    public WorkspaceQuery? Filters { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    public LayoutPreset Clone() => new()
    {
        Id = Id,
        Name = Name,
        Arrangement = Arrangement,
        Panels = Panels.Select(p => p.Clone()).ToList(),
        Filters = Filters?.Clone(),
        Extra = Extra is null ? null : new Dictionary<string, JsonElement>(Extra),
    };
}

/// <summary>
/// The arrangement a layout is actually shown with, when that differs from
/// its saved definition (plan D2): kept per layout, built-in or user, so a
/// built-in can be personalised without changing its factory definition and
/// switching away and back returns to the same arrangement.
///
/// <see cref="Query"/> is the query last used with this layout, restored
/// only when the workspace resumes it at startup (D3), never carried to
/// another layout.
/// </summary>
public sealed class WorkingArrangement
{
    public string PresetId { get; set; } = "";

    /// <summary>The arrangement, or null when it matches the definition and only a query is remembered.</summary>
    public List<PanelInstance>? Panels { get; set; }

    /// <summary>For a built-in: the factory version these changes were made against.</summary>
    public int? BuiltInVersion { get; set; }

    public WorkspaceQuery? Query { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    public WorkingArrangement Clone() => new()
    {
        PresetId = PresetId,
        Panels = Panels?.Select(p => p.Clone()).ToList(),
        BuiltInVersion = BuiltInVersion,
        Query = Query?.Clone(),
        Extra = Extra is null ? null : new Dictionary<string, JsonElement>(Extra),
    };
}

/// <summary>How a layout places its panels (Desk layout design §2): rows of the twelve-column canvas, or two columns.</summary>
public static class LayoutArrangements
{
    public const string Rows = "rows";
    public const string Columns = "columns";

    public static bool IsColumns(string? arrangement) => arrangement == Columns;
}
