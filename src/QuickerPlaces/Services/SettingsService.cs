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
