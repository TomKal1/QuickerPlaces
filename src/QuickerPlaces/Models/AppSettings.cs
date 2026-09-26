namespace QuickerPlaces.Models;

/// <summary>
/// Persisted window/UI chrome settings, serialized as JSON by
/// SettingsService. Deliberately holds only machine-specific presentation
/// state (window bounds, grid collapsed/expanded) — the actual Places data
/// lives in its own file via PlacesService/PlacesStore (see
/// Services/PlacesService.cs), since that data is write-through-on-every-
/// change and conceptually separate from "how big was the window last
/// time". Bump <see cref="SchemaVersion"/> whenever a field is added,
/// removed, or its meaning changes, and add a migration note (or code, in
/// SettingsService.Load) if older settings files on disk need upgrading.
/// </summary>
public sealed class AppSettings
{
    /// <summary>
    /// The schema version this build writes and expects, mirroring
    /// PlacesService.CurrentSchemaVersion. See SettingsService.Load for
    /// the (deliberately different) policy applied when a loaded file's
    /// SchemaVersion doesn't match.
    ///
    /// 2: added GlobalHotkey. No migration needed: a version-1 file simply
    /// lacks the field, so it deserializes to the default hotkey. This must
    /// stay at least 2: builds from main already write 2, and Load resets a
    /// file newer than this to defaults, silently dropping the user's
    /// hotkey and window layout.
    ///
    /// 3: added PlacesSortKey and PlacesSortDirection (Phase 3 D30). No
    /// migration needed either: a version-2 file lacks them, which reads as
    /// no sort — the stored order it already showed.
    /// </summary>
    public const int CurrentSchemaVersion = 3;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    // Last known main window bounds, used to restore the window on the next
    // launch. Left/Top of double.NaN means "no saved position yet" (first
    // run) — MainWindow falls back to WPF's centered startup location.
    public double WindowLeft { get; set; } = double.NaN;
    public double WindowTop { get; set; } = double.NaN;
    public double WindowWidth { get; set; } = 1000;
    public double WindowHeight { get; set; } = 650;
    public bool WindowMaximized { get; set; }

    /// <summary>Whether the Places DataGrid was expanded (vs. collapsed to just the favourite bubbles) on last close.</summary>
    public bool IsGridExpanded { get; set; } = true;

    /// <summary>
    /// The system-wide shortcut that brings QuickerPlaces to the front with
    /// the search box focused, e.g. "Ctrl+Alt+Space" (see
    /// <see cref="HotkeyGesture.TryParse"/> for the format). Empty or "None"
    /// turns it off. Changed in the Settings dialog, which saves straight
    /// away; hand edits to settings.json need the app closed, since it
    /// rewrites the file on exit.
    /// </summary>
    public string? GlobalHotkey { get; set; } = HotkeyGesture.Default;

    /// <summary>
    /// The places grid's remembered sort (Phase 3 D30): a PlaceSortKey name
    /// and "ascending" or "descending", as PlaceSort.Format writes them. Both
    /// null means the stored order. Strings, not enums, deliberately: this
    /// file's enums deserialize strictly, so a misspelt value would throw
    /// and Load's fallback would reset every setting, hotkey included —
    /// PlaceSort.Parse instead ignores what it doesn't recognise. Machine-
    /// local presentation state, like the rest of this file (roadmap §3).
    /// </summary>
    public string? PlacesSortKey { get; set; }

    /// <summary>See <see cref="PlacesSortKey"/>.</summary>
    public string? PlacesSortDirection { get; set; }
}
