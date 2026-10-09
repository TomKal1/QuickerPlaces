using System;
using System.IO;
using QuickerPlaces.Models;
using QuickerPlaces.Services;
using QuickerPlaces.Services.Revit.AddIns;
using QuickerPlaces.Services.Revit.Opening;
using QuickerPlaces.Tests.Fakes;
using Xunit;

namespace QuickerPlaces.Tests;

public sealed class SettingsServiceTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private SettingsService NewService() => new(_temp.File("settings.json"));

    [Fact]
    public void Missing_file_loads_defaults()
    {
        var settings = NewService().Load();

        Assert.True(double.IsNaN(settings.WindowLeft));
        Assert.True(settings.IsGridExpanded);
    }

    [Fact]
    public void Corrupt_file_loads_defaults()
    {
        File.WriteAllText(_temp.File("settings.json"), "{ nope");

        Assert.True(NewService().Load().IsGridExpanded);
    }

    /// <summary>
    /// Regression: default settings hold NaN for "no saved position", and
    /// System.Text.Json rejects NaN unless told otherwise. That made Save
    /// throw (and swallow) whenever the window was closed maximized before
    /// ever being closed in its normal state, losing every setting.
    /// </summary>
    [Fact]
    public void Settings_with_unset_position_still_round_trip()
    {
        var service = NewService();
        var settings = new AppSettings { WindowMaximized = true, IsGridExpanded = false };

        service.Save(settings);
        var loaded = NewService().Load();

        Assert.True(loaded.WindowMaximized);
        Assert.False(loaded.IsGridExpanded);
        Assert.True(double.IsNaN(loaded.WindowLeft));
    }

    [Fact]
    public void Saved_bounds_round_trip()
    {
        var service = NewService();
        service.Save(new AppSettings { WindowLeft = 10, WindowTop = 20, WindowWidth = 800, WindowHeight = 500 });

        var loaded = NewService().Load();

        Assert.Equal(10, loaded.WindowLeft);
        Assert.Equal(20, loaded.WindowTop);
        Assert.Equal(800, loaded.WindowWidth);
        Assert.Equal(500, loaded.WindowHeight);
    }

    [Fact]
    public void Global_hotkey_defaults_when_missing_from_an_older_file()
    {
        File.WriteAllText(_temp.File("settings.json"), """{ "schemaVersion": 1, "isGridExpanded": false }""");

        var loaded = NewService().Load();

        Assert.Equal(HotkeyGesture.Default, loaded.GlobalHotkey);
        Assert.False(loaded.IsGridExpanded);
    }

    /// <summary>Phase 3 test 38: the remembered sort round-trips, as the two strings PlaceSort.Format writes (D30).</summary>
    [Fact]
    public void Places_sort_round_trips()
    {
        NewService().Save(new AppSettings { PlacesSortKey = "LastOpened", PlacesSortDirection = "descending" });

        var loaded = NewService().Load();

        Assert.Equal(7, AppSettings.CurrentSchemaVersion);
        Assert.Equal(AppSettings.CurrentSchemaVersion, loaded.SchemaVersion);
        Assert.Equal("LastOpened", loaded.PlacesSortKey);
        Assert.Equal("descending", loaded.PlacesSortDirection);
    }

    /// <summary>Phase 3 test 38: a version 2 file has no sort, which is stored order; its hotkey and bounds are kept (no settings migration).</summary>
    [Fact]
    public void A_version_2_file_loads_unsorted_and_keeps_everything_else()
    {
        File.WriteAllText(_temp.File("settings.json"), """{ "schemaVersion": 2, "windowLeft": 10, "globalHotkey": "Ctrl+Shift+Q" }""");

        var loaded = NewService().Load();

        Assert.Null(loaded.PlacesSortKey);
        Assert.Null(loaded.PlacesSortDirection);
        Assert.Equal(10, loaded.WindowLeft);
        Assert.Equal("Ctrl+Shift+Q", loaded.GlobalHotkey);
    }

    [Fact]
    public void Version_3_settings_keep_existing_values_and_default_tray_switches_off()
    {
        File.WriteAllText(_temp.File("settings.json"),
            """{ "schemaVersion": 3, "globalHotkey": "Ctrl+Shift+Q", "placesSortKey": "Alias" }""");

        var loaded = NewService().Load();

        Assert.Equal("Ctrl+Shift+Q", loaded.GlobalHotkey);
        Assert.Equal("Alias", loaded.PlacesSortKey);
        Assert.False(loaded.MinimizeToTray);
        Assert.False(loaded.StartWithWindows);
    }

    [Fact]
    public void Tray_switches_round_trip()
    {
        NewService().Save(new AppSettings { MinimizeToTray = true, StartWithWindows = true });
        var loaded = NewService().Load();

        Assert.True(loaded.MinimizeToTray);
        Assert.True(loaded.StartWithWindows);
    }

    /// <summary>
    /// Phase 3 test 38: an unrecognised sort costs only the sort. The fields
    /// are strings, not enums, precisely so a bad value can't make
    /// deserialisation throw and reset every setting (D30).
    /// </summary>
    [Fact]
    public void An_unrecognised_sort_leaves_the_other_settings_intact()
    {
        File.WriteAllText(_temp.File("settings.json"), """{ "schemaVersion": 3, "windowLeft": 10, "globalHotkey": "Ctrl+Shift+Q", "placesSortKey": "Nonsense", "placesSortDirection": "sideways" }""");

        var loaded = NewService().Load();

        Assert.Equal(10, loaded.WindowLeft);
        Assert.Equal("Ctrl+Shift+Q", loaded.GlobalHotkey);
        Assert.Null(PlaceSort.Parse(loaded.PlacesSortKey, loaded.PlacesSortDirection));
    }

    [Theory]
    [InlineData("Ctrl+Shift+Q")]
    [InlineData("None")]
    [InlineData(null)]
    public void Global_hotkey_setting_round_trips(string? hotkey)
    {
        NewService().Save(new AppSettings { GlobalHotkey = hotkey });

        Assert.Equal(hotkey, NewService().Load().GlobalHotkey);
    }

    [Fact]
    public void Version_4_settings_load_with_the_default_theme_and_highlight()
    {
        File.WriteAllText(_temp.File("settings.json"),
            """{ "schemaVersion": 4, "globalHotkey": "Ctrl+Shift+Q", "minimizeToTray": true }""");

        var loaded = NewService().Load();

        Assert.Equal("Ctrl+Shift+Q", loaded.GlobalHotkey);
        Assert.True(loaded.MinimizeToTray);
        Assert.Equal("dark", loaded.Theme);
        Assert.Equal("green", loaded.Highlight);
    }

    [Fact]
    public void Theme_and_highlight_round_trip()
    {
        NewService().Save(new AppSettings { Theme = "system", Highlight = "cognac" });
        var loaded = NewService().Load();

        Assert.Equal("system", loaded.Theme);
        Assert.Equal("cognac", loaded.Highlight);
    }

    [Fact]
    public void An_unrecognised_theme_costs_only_the_theme()
    {
        File.WriteAllText(_temp.File("settings.json"),
            """{ "schemaVersion": 5, "globalHotkey": "Ctrl+Shift+Q", "theme": "neon", "highlight": "teal" }""");

        var loaded = NewService().Load();

        Assert.Equal("Ctrl+Shift+Q", loaded.GlobalHotkey);
        Assert.Equal(AppTheme.Dark, ThemePreference.ParseTheme(loaded.Theme));
        Assert.Equal(HighlightPreset.Green, ThemePreference.ParseHighlight(loaded.Highlight));
    }

    [Fact]
    public void Revit_settings_round_trip_per_release()
    {
        var settings = new AppSettings();
        settings.RevitReleases!["2025"] = new RevitReleaseSettings { HandlerId = "contoso.tools", LocalFolder = @"D:\Locals" };
        settings.RevitReleases["2024"] = new RevitReleaseSettings { HandlerId = "other" };
        NewService().Save(settings);

        var loaded = NewService().Load();

        Assert.Equal("contoso.tools", loaded.RevitReleases!["2025"].HandlerId);
        Assert.Equal(@"D:\Locals", loaded.RevitReleases["2025"].LocalFolder);
        Assert.Equal("other", loaded.RevitReleases["2024"].HandlerId);
        Assert.Null(loaded.RevitReleases["2024"].LocalFolder);
        Assert.Contains("\"revitReleases\"", File.ReadAllText(_temp.File("settings.json")));
    }

    [Fact]
    public void Load_once_choices_round_trip()
    {
        var allowed = new DateTimeOffset(2026, 10, 9, 8, 30, 0, TimeSpan.Zero);
        var settings = new AppSettings { AllowLoadOnce = true };
        settings.LoadOnceEntries!.Add(new LoadOnceEntry
        {
            Release = 2025, AddInId = "9C0C6B3E-0000-0000-0000-000000000001", Name = "DuctExporter",
            DllPath = @"C:\Addins\DuctExporter.dll", DllSha256 = "ab12", AllowedUtc = allowed,
        });
        NewService().Save(settings);

        var loaded = NewService().Load();

        Assert.True(loaded.AllowLoadOnce);
        var entry = Assert.Single(loaded.LoadOnceEntries!);
        Assert.Equal(2025, entry.Release);
        Assert.Equal("DuctExporter", entry.Name);
        Assert.Equal(@"C:\Addins\DuctExporter.dll", entry.DllPath);
        Assert.Equal("ab12", entry.DllSha256);
        Assert.Equal(allowed, entry.AllowedUtc);
        Assert.Contains("\"allowLoadOnce\": true", File.ReadAllText(_temp.File("settings.json")));
    }

    [Fact]
    public void A_version_6_file_reads_as_load_once_off_with_no_entries()
    {
        File.WriteAllText(_temp.File("settings.json"),
            """{ "schemaVersion": 6, "globalHotkey": "Ctrl+Shift+Q", "revitReleases": { "2025": { "handlerId": "contoso" } } }""");

        var loaded = NewService().Load();

        Assert.False(loaded.AllowLoadOnce);
        Assert.Empty(loaded.LoadOnceEntries ?? []);
        Assert.Equal("contoso", loaded.RevitReleases!["2025"].HandlerId);
    }

    [Fact]
    public void A_version_5_file_without_revit_settings_reads_as_the_defaults()
    {
        File.WriteAllText(_temp.File("settings.json"), """{ "schemaVersion": 5, "globalHotkey": "Ctrl+Shift+Q" }""");

        var loaded = NewService().Load();

        Assert.Equal("Ctrl+Shift+Q", loaded.GlobalHotkey);
        var effective = RevitSettingsResolver.For(loaded, "2025");
        Assert.Null(effective.HandlerId);
        Assert.Equal(@"C:\REVIT_LOCAL2025", effective.LocalFolder);
    }

    [Fact]
    public void Resolver_fills_in_defaults_for_blank_values_and_a_null_dictionary()
    {
        var settings = new AppSettings { RevitReleases = new() { ["2025"] = new RevitReleaseSettings { HandlerId = "  ", LocalFolder = "" } } };

        Assert.Equal(new EffectiveRevitSettings("2025", null, @"C:\REVIT_LOCAL2025"), RevitSettingsResolver.For(settings, "2025"));
        Assert.Equal(new EffectiveRevitSettings("2026", null, @"C:\REVIT_LOCAL2026"), RevitSettingsResolver.For(settings, "2026"));

        settings.RevitReleases = null;
        Assert.Equal(@"C:\REVIT_LOCAL2024", RevitSettingsResolver.For(settings, "2024").LocalFolder);
    }

    [Fact]
    public void Resolver_trims_the_chosen_handler_and_keeps_a_chosen_folder()
    {
        var settings = new AppSettings { RevitReleases = new() { ["2025"] = new RevitReleaseSettings { HandlerId = " contoso ", LocalFolder = @"E:\L" } } };

        Assert.Equal(new EffectiveRevitSettings("2025", "contoso", @"E:\L"), RevitSettingsResolver.For(settings, "2025"));
    }
}
