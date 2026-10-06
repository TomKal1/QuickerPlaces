using System;
using System.Globalization;
using System.Linq;
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
    public void AddAndToggle_UpdateSelectionAndNotifyTheHost()
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
        Assert.Equal("Folder settings updated.", view.StatusMessage);
        view.DismissStatus();
        Assert.Null(view.StatusMessage);
    }

    [Fact]
    public void FailedSettingsWriteDoesNotShowSuccessStatus()
    {
        var view = NewViewModel();
        Assert.True(view.AddRoot(@"C:\Jobs", null));
        _storage.FailNextWrite = true;
        view.DwellSecondsText = "9";

        Assert.True(view.SaveSelectedSettings());
        Assert.True(view.HasUnsavedChanges);
        Assert.True(view.HasError);
        Assert.Null(view.StatusMessage);
    }

    [Fact]
    public void AllVisitedFoldersOptionSavesExactFolderTracking()
    {
        var view = NewViewModel();
        Assert.True(view.AddRoot(@"C:\Jobs", null));

        var option = Assert.Single(view.RollupOptions, item => item.Mode == RollupMode.Exact);
        Assert.Equal("All subfolders", option.Label);
        view.SelectedRollupOption = option;
        Assert.True(view.SaveSelectedSettings());

        Assert.Equal(RollupMode.Exact, view.SelectedRoot!.Config.Rollup);
    }

    [Fact]
    public void TrackedFolderTooltipDescribesItsOwnGrouping()
    {
        var store = new ActivityStore(_storage, _time);
        var root = ActivityFixtures.AddRoot(store);

        Assert.Contains("First folder below root", root.TrackingToolTip);
        Assert.Contains(root.Path, root.TrackingToolTip);

        var exact = root with { Config = root.Config with { Rollup = RollupMode.Exact } };
        Assert.Contains("All subfolders", exact.TrackingToolTip);

        var depth = root with { Config = root.Config with { Rollup = RollupMode.Depth, Depth = 3 } };
        Assert.Contains("level 3", depth.TrackingToolTip);
    }

    [Fact]
    public void ResetSelectedSettingsDraftDiscardsUnappliedEdits()
    {
        var view = NewViewModel();
        Assert.True(view.AddRoot(@"C:\Jobs", null));
        view.Rollup = RollupMode.Exact;
        view.DepthText = "4";
        view.DwellSecondsText = "20";
        view.IdleMinutesText = "9";
        view.EquivalentPrefixesText = @"\\server\share\Jobs";

        view.ResetSelectedSettingsDraft();

        Assert.Equal(RollupMode.RootChild, view.Rollup);
        Assert.Equal("1", view.DepthText);
        Assert.Equal("5", view.DwellSecondsText);
        Assert.Equal("5", view.IdleMinutesText);
        Assert.Empty(view.EquivalentPrefixesText);
    }

    [Theory]
    [InlineData(@"C:\Jobs", @"C:\Jobs", 0, "Root folder")]
    [InlineData(@"C:\Jobs", @"C:\Jobs\Acme", 1, "Level 1 · directly below root")]
    [InlineData(@"C:\Jobs", @"C:\Jobs\Acme\Plans", 2, "Level 2 · below root")]
    [InlineData(@"\\server\share\Jobs", @"\\server\share\Jobs\Acme\Plans", 2, "Level 2 · below root")]
    public void FolderRowsReportLevelBelowTrackedRoot(string root, string folder, int level, string label)
    {
        var row = new ActivityFolderRow(new FolderActivity(folder, TimeSpan.Zero, 1, _time.GetUtcNow()),
            _time.LocalTimeZone, CultureInfo.InvariantCulture, root);

        Assert.Equal(level, row.Level);
        Assert.Equal(label, row.LevelLabel);
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
        Assert.Contains("Earlier rows keep", view.AboutSelectedFolderText);
        Assert.Contains(root.Path, view.AboutSelectedFolderText);
        view.RefreshPeriod();
        Assert.Contains("new visits use", view.PeriodNotice);
        Assert.Contains("new visits use", view.AboutSelectedFolderText);
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
    public void ArrowNavigationFollowsDayRowsAndWeekColumns()
    {
        var view = new ActivityViewModel(new ActivityStore(_storage, _time),
            () => { }, _time, CultureInfo.GetCultureInfo("en-GB"));
        Assert.True(view.AddRoot(@"C:\Jobs", null));
        view.ShowDay(new DateOnly(2026, 9, 10));

        Assert.True(view.MoveCalendarSelection(1, horizontal: false));
        Assert.Equal(new DateOnly(2026, 9, 11), view.PeriodFrom);
        Assert.True(view.MoveCalendarSelection(1, horizontal: true));
        Assert.Equal(new DateOnly(2026, 9, 18), view.PeriodFrom);
        Assert.True(view.MoveCalendarSelection(-1, horizontal: true));
        Assert.Equal(new DateOnly(2026, 9, 11), view.PeriodFrom);
        Assert.True(view.MoveCalendarSelection(-1, horizontal: false));
        Assert.Equal(new DateOnly(2026, 9, 10), view.PeriodFrom);
        Assert.True(Assert.Single(view.CalendarWeeks.SelectMany(week => week.Days),
            day => day.Date == view.PeriodFrom).IsSelected);

        view.SetPeriodMode(ActivityPeriodMode.Week);
        var weekStart = view.PeriodFrom;
        Assert.True(view.MoveCalendarSelection(1, horizontal: true));
        Assert.Equal(weekStart.AddDays(7), view.PeriodFrom);
        Assert.True(view.MoveCalendarSelection(-1, horizontal: false));
        Assert.Equal(weekStart, view.PeriodFrom);

        view.SetPeriodMode(ActivityPeriodMode.Month);
        Assert.True(view.MoveCalendarSelection(-1, horizontal: false));
        Assert.Equal(new DateOnly(2026, 8, 1), view.PeriodFrom);
        Assert.True(view.MoveCalendarSelection(1, horizontal: true));
        Assert.Equal(new DateOnly(2026, 9, 1), view.PeriodFrom);

        view.ShowToday();
        Assert.False(view.MoveCalendarSelection(1, horizontal: false));
        Assert.False(view.MoveCalendarSelection(1, horizontal: true));
        Assert.Equal(new DateOnly(2026, 9, 25), view.PeriodFrom);
    }

    [Fact]
    public void SelectedCalendarBubblesFollowTheTablePeriod()
    {
        var view = new ActivityViewModel(new ActivityStore(_storage, _time),
            () => { }, _time, CultureInfo.GetCultureInfo("en-GB"));
        Assert.True(view.AddRoot(@"C:\Jobs", null));

        Assert.True(view.IsWeekMode);
        Assert.Equal(7, view.CalendarWeeks.SelectMany(week => week.Days).Count(day => day.IsSelected));

        view.SetPeriodMode(ActivityPeriodMode.Month);
        Assert.True(view.IsMonthMode);
        Assert.Equal(30, view.CalendarWeeks.SelectMany(week => week.Days).Count(day => day.IsSelected));

        view.MovePeriod(-1);
        Assert.Equal(31, view.CalendarWeeks.SelectMany(week => week.Days).Count(day => day.IsSelected));

        view.ShowDay(new DateOnly(2025, 12, 31));
        Assert.Equal(2025, view.CalendarYear);
        Assert.Single(view.CalendarWeeks.SelectMany(week => week.Days), day => day.IsSelected);

        view.ShowToday();
        Assert.Equal(2026, view.CalendarYear);
        Assert.False(view.IsWeekMode);
        Assert.False(view.IsMonthMode);
        Assert.Single(view.CalendarWeeks.SelectMany(week => week.Days), day => day.IsSelected);
    }

    [Fact]
    public void ClickingCalendarDateKeepsTheActiveWeekMonthOrDayView()
    {
        var view = new ActivityViewModel(new ActivityStore(_storage, _time),
            () => { }, _time, CultureInfo.GetCultureInfo("en-GB"));
        Assert.True(view.AddRoot(@"C:\Jobs", null));

        view.SelectCalendarDate(new DateOnly(2026, 8, 19));
        Assert.True(view.IsWeekMode);
        Assert.Equal(new DateOnly(2026, 8, 17), view.PeriodFrom);
        Assert.Equal(new DateOnly(2026, 8, 23), view.PeriodTo);
        Assert.Equal(7, view.CalendarWeeks.SelectMany(week => week.Days).Count(day => day.IsSelected));

        view.SetPeriodMode(ActivityPeriodMode.Month);
        view.SelectCalendarDate(new DateOnly(2026, 7, 10));
        Assert.True(view.IsMonthMode);
        Assert.Equal(new DateOnly(2026, 7, 1), view.PeriodFrom);
        Assert.Equal(new DateOnly(2026, 7, 31), view.PeriodTo);
        Assert.Equal(31, view.CalendarWeeks.SelectMany(week => week.Days).Count(day => day.IsSelected));

        view.SetPeriodMode(ActivityPeriodMode.Day);
        view.SelectCalendarDate(new DateOnly(2026, 6, 3));
        Assert.True(view.IsDayMode);
        Assert.Equal(new DateOnly(2026, 6, 3), view.PeriodFrom);
        Assert.Equal(view.PeriodFrom, view.PeriodTo);
        Assert.Single(view.CalendarWeeks.SelectMany(week => week.Days), day => day.IsSelected);

        view.SelectCalendarDate(new DateOnly(2027, 2, 5));
        Assert.Equal(new DateOnly(2027, 2, 5), view.PeriodFrom);
        Assert.Equal(2027, view.CalendarYear);
        Assert.True(Assert.Single(view.CalendarWeeks.SelectMany(week => week.Days),
            day => day.Date == new DateOnly(2027, 2, 5)).IsSelected);
    }

    [Fact]
    public void YearStripCanBrowseFutureYearsAndReturnToToday()
    {
        var view = NewViewModel();
        Assert.True(view.AddRoot(@"C:\Jobs", null));
        Assert.Equal(2026, view.CalendarYear);
        Assert.Equal(12, view.CalendarMonths.Count);
        Assert.NotEmpty(view.CalendarWeeks);
        Assert.Equal(12, view.CalendarMonthMarkers.Count);

        Assert.True(view.SelectCalendarYear(2025));
        Assert.Equal(2025, view.CalendarYear);
        Assert.Equal(2025, view.EarliestCalendarYear);
        Assert.False(view.SelectCalendarYear(2024));
        Assert.Equal(2025, view.CalendarYear);
        Assert.True(view.SelectCalendarYear(2026));
        Assert.Equal(2026, view.CalendarYear);
        Assert.True(view.SelectCalendarYear(2035));
        Assert.Equal(2035, view.CalendarYear);
        Assert.False(view.SelectCalendarYear(9999));
        Assert.Equal(2035, view.CalendarYear);
        Assert.All(view.CalendarWeeks.SelectMany(week => week.Days)
            .Where(day => day.IsInRange), day => Assert.False(day.IsTracked));

        view.ShowToday();
        Assert.Equal(2026, view.CalendarYear);
        Assert.Equal(ActivityPeriodMode.Day, view.PeriodMode);
        Assert.Equal(view.PeriodFrom, view.PeriodTo);
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
