using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuickerPlaces.Models.Workspace;

/// <summary>
/// The root of workspace-layouts.json (configurable canvas plan §4): the
/// user's named layouts, the working arrangement of each layout that has
/// one, which layout is active and what to show at startup. Kept together in
/// one file so deleting or switching a layout updates every reference to it
/// in a single write. Window size and theme stay in settings.json.
///
/// Built-in layouts are code, never written here; only their working
/// arrangements are. Unknown properties survive in <see cref="Extra"/>. UI-free.
/// </summary>
public sealed class WorkspaceDocument
{
    /// <summary>WorkspaceStore sets this from its CurrentSchemaVersion on every write and checks it on every load.</summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>My layouts, in the order the picker lists them.</summary>
    public List<LayoutPreset> Presets { get; set; } = new();

    public List<WorkingArrangement> Working { get; set; } = new();

    /// <summary>The layout shown last, built-in or user. Null means the default built-in.</summary>
    public string? ActivePresetId { get; set; }

    public StartupChoice Startup { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    public WorkspaceDocument Clone() => new()
    {
        SchemaVersion = SchemaVersion,
        Presets = Presets.Select(p => p.Clone()).ToList(),
        Working = Working.Select(w => w.Clone()).ToList(),
        ActivePresetId = ActivePresetId,
        Startup = Startup.Clone(),
        Extra = Extra is null ? null : new Dictionary<string, JsonElement>(Extra),
    };
}

/// <summary>
/// What the workspace shows at startup (plan D2): the last workspace (the
/// default), or one chosen layout. Stored as a string for the same reason as
/// <see cref="DateRule.Rule"/>.
/// </summary>
public sealed class StartupChoice
{
    public const string ResumeName = "resume";
    public const string PresetName = "preset";

    /// <summary>"resume" or "preset"; anything else reads as resume.</summary>
    public string Mode { get; set; } = ResumeName;

    /// <summary>The layout to start with when <see cref="Mode"/> is "preset".</summary>
    public string? PresetId { get; set; }

    [JsonIgnore]
    public bool ResumesLast => Mode != PresetName || PresetId is null;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    public static StartupChoice Resume() => new();

    public static StartupChoice ForPreset(string presetId) => new() { Mode = PresetName, PresetId = presetId };

    public StartupChoice Clone() => new()
    {
        Mode = Mode,
        PresetId = PresetId,
        Extra = Extra is null ? null : new Dictionary<string, JsonElement>(Extra),
    };
}
