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
    /// </summary>
    public const int CurrentSchemaVersion = 1;

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
}
