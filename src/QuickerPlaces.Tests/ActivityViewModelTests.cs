using System;
using System.Globalization;
using QuickerPlaces.Models.Activity;
using QuickerPlaces.Services.Activity;
using QuickerPlaces.Tests.Fakes;
using QuickerPlaces.ViewModels;
using Xunit;

namespace QuickerPlaces.Tests;

public sealed class ActivityViewModelTests
{
    private readonly FakePlacesStorage _storage = new() { StoreFilePath = @"C:\fake\activity.json" };
    private readonly ManualTimeProvider _time = new();
    private int _rootNotifications;

    private ActivityViewModel NewViewModel()
        => new(new ActivityStore(_storage, _time), () => _rootNotifications++, _time);

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

    [Fact]
    public void ChangingGrouping_ExplainsWhyEarlierRowsStayGroupedAsRecorded()
    {
        var store = new ActivityStore(_storage, _time);
        var root = ActivityFixtures.AddRoot(store);
        store.Record(new[] { ActivityFixtures.Interval(root.RootId, ActivityFixtures.Acme,
            ActivityFixtures.Today, 30, startsVisit: true) });
        var view = new ActivityViewModel(store, () => { }, _time);

        view.Rollup = RollupMode.Depth;
        view.DepthText = "2";
        Assert.True(view.SaveSelectedSettings());

        Assert.Equal(RollupMode.Depth, view.SelectedRoot!.Config.Rollup);
        Assert.Equal(2, view.SelectedRoot.Config.Depth);
        Assert.Equal(ActivityFixtures.Acme, Assert.Single(view.PeriodRows).Folder);
        Assert.Contains("Earlier rows keep", view.PeriodNotice);
        view.RefreshPeriod();
        Assert.Contains("new visits use", view.PeriodNotice);
    }

    [Fact]
    public void WeekAndMonthNavigationUseCalendarBoundaries()
    {
        var view = new ActivityViewModel(new ActivityStore(_storage, _time),
            () => { }, _time, CultureInfo.GetCultureInfo("en-GB"));
        Assert.Equal(new DateOnly(2026, 9, 21), view.PeriodFrom);
        Assert.Equal(new DateOnly(2026, 9, 27), view.PeriodTo);

        view.SetPeriodMode(ActivityPeriodMode.Month);
        Assert.Equal(new DateOnly(2026, 9, 1), view.PeriodFrom);
        Assert.Equal(new DateOnly(2026, 9, 30), view.PeriodTo);
        view.MovePeriod(-1);
        Assert.Equal(new DateOnly(2026, 8, 1), view.PeriodFrom);
        view.MovePeriod(1);
        Assert.Equal(new DateOnly(2026, 9, 1), view.PeriodFrom);
        Assert.False(view.CanMoveNext);
    }

    [Fact]
    public void DayViewUsesStoredDayTotalWhenFolderDetailHasExpired()
    {
        var store = new ActivityStore(_storage, _time);
        var root = ActivityFixtures.AddRoot(store);
        var oldDay = new DateOnly(2026, 6, 1);
        store.Record(new[] { ActivityFixtures.Interval(root.RootId, ActivityFixtures.Acme,
            oldDay, 3600, startsVisit: true) });
        _time.Advance(TimeSpan.FromDays(1));
        store.Flush();
        var view = new ActivityViewModel(store, () => { }, _time);

        view.ShowDay(oldDay);

        Assert.Empty(view.PeriodRows);
        Assert.Contains("1h total", view.PeriodSummary);
        Assert.Contains("expired", view.PeriodNotice);
    }

    [Fact]
    public void CurrentWeekShowsRecordedFolderWithReadableFields()
    {
        var store = new ActivityStore(_storage, _time);
        var root = ActivityFixtures.AddRoot(store);
        store.Record(new[] { ActivityFixtures.Interval(root.RootId, ActivityFixtures.Acme,
            ActivityFixtures.Today, 1920, startsVisit: true) });
        var view = new ActivityViewModel(store, () => { }, _time);

        var row = Assert.Single(view.PeriodRows);
        Assert.Equal(ActivityFixtures.Acme, row.Folder);
        Assert.Equal(1, row.Visits);
        Assert.Equal("32m", row.TimeText);
        Assert.Contains("32m", view.PeriodSummary);
    }

    [Theory]
    [InlineData(0, "0m")]
    [InlineData(30, "<1m")]
    [InlineData(1920, "32m")]
    [InlineData(11520, "3h 12m")]
    public void DurationFormatUsesReadableHoursAndMinutes(int seconds, string expected)
        => Assert.Equal(expected, ActivityFormat.Duration(TimeSpan.FromSeconds(seconds)));
}
