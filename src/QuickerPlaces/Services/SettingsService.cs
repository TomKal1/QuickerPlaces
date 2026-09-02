using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using QuickerPlaces.Models;

namespace QuickerPlaces.Services;

/// <summary>
/// Loads and saves AppSettings (window chrome only — see AppSettings'
/// remarks) as JSON under the user's local AppData folder. Save is called
/// once, explicitly, on clean exit (App.xaml.cs) — window bounds don't need
/// write-through persistence the way Places data does, since losing the
/// last few pixels of a resize to a crash is harmless.
/// </summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly string _settingsFilePath;

    public SettingsService()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var folder = Path.Combine(root, AppInfo.Publisher, AppInfo.Name);
        Directory.CreateDirectory(folder);
        _settingsFilePath = Path.Combine(folder, "settings.json");
    }

    /// <summary>Full path to settings.json.</summary>
    public string SettingsFilePath => _settingsFilePath;

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsFilePath))
                return new AppSettings();

            var json = File.ReadAllText(_settingsFilePath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            if (settings is null)
                return new AppSettings();

            // Plan 5.3: settings.json gets the same version read as
            // places.json, but keeps its own, intentionally different
            // policy on what to do with a version it doesn't recognize.
            // There is nothing to migrate yet (settings has only ever
            // been schemaVersion 1) — this read exists so a future bump
            // has somewhere to branch, mirroring PlacesService's gate.
            //
            // Unlike PlacesService, an AppSettings whose version is
            // unrecognized (or missing/non-numeric, which
            // JsonSerializer.Deserialize above would have already turned
            // into AppSettings' default SchemaVersion of 1 rather than a
            // parse failure) still falls back to defaults SILENTLY,
            // exactly like a corrupt or unreadable file does below. This
            // is deliberate, not an oversight: settings.json holds only
            // window bounds and a grid toggle, so losing it costs the
            // user a moment's window resizing, never data. Do NOT "fix"
            // this to match PlacesService's recovery-and-quarantine
            // behaviour for its own sake — the two services have
            // different data at stake and are meant to diverge here.
            if (settings.SchemaVersion > AppSettings.CurrentSchemaVersion)
                return new AppSettings();

            return settings;
        }
        catch
        {
            // Corrupt or unreadable settings file — start fresh rather than
            // crashing the app on launch. Same asymmetry as above: this
            // stays a silent fallback, unlike PlacesService's recovery
            // flow, because settings.json is low-stakes, machine-local
            // presentation state.
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(_settingsFilePath, json);
        }
        catch
        {
            // Window chrome failing to save is not worth surfacing to the
            // user on their way out the door — best effort only.
        }
    }
}
