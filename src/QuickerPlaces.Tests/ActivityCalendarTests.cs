using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using QuickerPlaces.Services.Activity;
using QuickerPlaces.ViewModels;
using Xunit;

namespace QuickerPlaces.Tests;

public sealed class ActivityCalendarTests
{
    private static readonly DateOnly Today = new(2026, 9, 25);
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("en-GB");

    [Fact]
    public void CalendarCovers365DaysInCultureAlignedWeeks()
    {
        var calendar = ActivityCalendar.Build(new Dictionary<DateOnly, ActivityDayTotal>(),
            Today.AddDays(-200), Today, Culture);
        var visible = calendar.Weeks.SelectMany(week => week.Days).Where(day => day.IsInRange).ToList();

        Assert.Equal(365, visible.Count);
        Assert.Equal(Today.AddDays(-364), visible[0].Date);
        Assert.Equal(Today, visible[^1].Date);
        Assert.All(calendar.Weeks, week => Assert.Equal(DayOfWeek.Monday, week.StartsOn.DayOfWeek));
        Assert.Equal("Mon", calendar.WeekdayLabels[0]);
    }

    [Fact]
    public void PretrackingDaysAreDistinctFromTrackedDaysWithNoActivity()
    {
        var started = Today.AddDays(-100);
        var calendar = ActivityCalendar.Build(new Dictionary<DateOnly, ActivityDayTotal>(),
            started, Today, Culture);
        var cells = calendar.Weeks.SelectMany(week => week.Days).Where(day => day.IsInRange).ToList();

        Assert.False(cells[0].IsTracked);
        Assert.Equal(-1, cells[0].Intensity);
        Assert.Contains("not tracked", cells[0].Label);
        Assert.True(cells[^1].IsTracked);
        Assert.Equal(0, cells[^1].Intensity);
        Assert.Contains("no activity", cells[^1].Label);
    }

    [Fact]
    public void NonzeroDaysUseFourQuartilesAndTiedValuesMatch()
    {
        var totals = new Dictionary<DateOnly, ActivityDayTotal>();
        for (var i = 0; i < 4; i++)
            totals[Today.AddDays(i - 3)] = new ActivityDayTotal(TimeSpan.FromHours(i + 1), 1, i + 1);
        var calendar = ActivityCalendar.Build(totals, Today.AddDays(-10), Today, Culture);
        var cells = calendar.Weeks.SelectMany(week => week.Days)
            .Where(day => day.Date >= Today.AddDays(-3)).ToList();

        Assert.Equal(new[] { 1, 2, 3, 4 }, cells.Select(day => day.Intensity));
        Assert.Contains("4h in 4 folders", cells[^1].Label);

        totals[Today.AddDays(-3)] = new ActivityDayTotal(TimeSpan.FromHours(2), 1, 1);
        calendar = ActivityCalendar.Build(totals, Today.AddDays(-10), Today, Culture);
        cells = calendar.Weeks.SelectMany(week => week.Days)
            .Where(day => day.Date >= Today.AddDays(-3)).ToList();
        Assert.Equal(cells[0].Intensity, cells[1].Intensity);
    }
}
