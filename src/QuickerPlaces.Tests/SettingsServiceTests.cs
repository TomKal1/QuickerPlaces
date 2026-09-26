using System;
using System.IO;
using QuickerPlaces.Models;
using QuickerPlaces.Services;
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

        Assert.Equal(3, AppSettings.CurrentSchemaVersion);
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
}
