using System;
using System.IO;
using QuickerPlaces.Models;
using QuickerPlaces.Services;
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
