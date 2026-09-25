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
        // AppSettings uses double.NaN for "no saved window position yet",
        // which System.Text.Json refuses to write by default — without this,
        // closing the window maximized (or minimized) before it had ever
        // been closed in its normal state threw inside Save and silently
        // lost every setting, including the grid's collapsed state.
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly string _settingsFilePath;

    public SettingsService()
        : this(DefaultSettingsFilePath())
    {
    }

    /// <summary>Backs the service with an explicit file instead of the default %LocalAppData% location — used by the unit tests.</summary>
    public SettingsService(string settingsFilePath)
    {
        _settingsFilePath = settingsFilePath;

        var folder = Path.GetDirectoryName(settingsFilePath);
        if (!string.IsNullOrEmpty(folder))
            Directory.CreateDirectory(folder);
    }

    private static string DefaultSettingsFilePath()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(root, AppInfo.Publisher, AppInfo.Name, "settings.json");
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
            // There is nothing to migrate yet (version 2 only added
            // GlobalHotkey, which a version-1 file simply lacks and so
            // gets its default) — this read exists so a future bump has
            // somewhere to branch, mirroring PlacesService's gate.
            //
            // Unlike PlacesService, an AppSettings whose version is
            // unrecognized (or missing/non-numeric, which
            // JsonSerializer.Deserialize above would have already turned
            // into AppSettings' default SchemaVersion of 1 rather than a
            // parse failure) still falls back to defaults SILENTLY,
            // exactly like a corrupt or unreadable file does below. This
            // is deliberate, not an oversight: settings.json holds only
            // window bounds, a grid toggle and the global hotkey, so
            // losing it costs the user a moment's setup, never data. Do NOT "fix"
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
