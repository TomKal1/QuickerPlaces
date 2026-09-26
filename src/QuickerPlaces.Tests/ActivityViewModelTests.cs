using System;
using QuickerPlaces.Services.Activity;
using QuickerPlaces.Tests.Fakes;
using QuickerPlaces.ViewModels;
using Xunit;

namespace QuickerPlaces.Tests;

public sealed class ActivityViewModelTests
{
    private readonly FakePlacesStorage _storage = new() { StoreFilePath = @"C:\fake\activity.json" };
    private int _rootNotifications;

    private ActivityViewModel NewViewModel()
        => new(new ActivityStore(_storage, new ManualTimeProvider()), () => _rootNotifications++);

    [Fact]
    public void AddToggleAndDelete_UpdateSelectionAndNotifyTheHost()
    {
        var view = NewViewModel();
        Assert.True(view.AddRoot(@"C:\Jobs", null));
        Assert.True(view.HasRoots);
        Assert.Equal(@"C:\Jobs", view.SelectedRoot!.Path);
        Assert.Equal(1, _rootNotifications);

        view.ToggleSelected();
        Assert.False(view.SelectedRoot!.Enabled);
        Assert.Equal("Resume tracking", view.ToggleLabel);
        Assert.Equal(2, _rootNotifications);

        view.DeleteSelected();
        Assert.False(view.HasRoots);
        Assert.Null(view.SelectedRoot);
        Assert.Equal(3, _rootNotifications);
    }

    [Fact]
    public void InvalidSettingsDoNotChangeTheStoreOrWakeTheHost()
    {
        var view = NewViewModel();
        Assert.True(view.AddRoot(@"C:\Jobs", null));
        view.IdleMinutesText = "0";

        Assert.False(view.SaveSelectedSettings());
        Assert.Contains("at least 1", view.ErrorMessage);
        Assert.Equal(1, _rootNotifications);
        Assert.Equal(TimeSpan.FromMinutes(5), view.SelectedRoot!.Config.IdleTimeout);
    }

    [Fact]
    public void FailedSaveStaysVisibleAndCanBeRetried()
    {
        var view = NewViewModel();
        _storage.FailNextWrite = true;

        Assert.True(view.AddRoot(@"C:\Jobs", null));
        Assert.True(view.HasUnsavedChanges);
        Assert.True(view.HasError);
        Assert.Equal(1, _rootNotifications);

        view.RetrySave();
        Assert.False(view.HasUnsavedChanges);
        Assert.False(view.HasError);
    }

    [Fact]
    public void SaveSettingsPersistsThresholdsAndEquivalentPaths()
    {
        var view = NewViewModel();
        Assert.True(view.AddRoot(@"J:\Jobs", null));
        view.DwellSecondsText = "8";
        view.IdleMinutesText = "3";
        view.EquivalentPrefixesText = @"\\server\share\Jobs";

        Assert.True(view.SaveSelectedSettings());
        Assert.Equal(TimeSpan.FromSeconds(8), view.SelectedRoot!.Config.DwellThreshold);
        Assert.Equal(TimeSpan.FromMinutes(3), view.SelectedRoot.Config.IdleTimeout);
        Assert.Single(view.SelectedRoot.Config.EquivalentPrefixes);
        Assert.Equal(2, _rootNotifications);
    }
}
