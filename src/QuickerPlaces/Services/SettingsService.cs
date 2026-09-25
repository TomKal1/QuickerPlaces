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
            return settings ?? new AppSettings();
        }
        catch
        {
            // Corrupt or unreadable settings file — start fresh rather than
            // crashing the app on launch.
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
