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
    public void CalendarYearStartsOnJanuaryFirstAndSeparatesAllTwelveMonths()
    {
        var calendar = ActivityCalendar.BuildYear(new Dictionary<DateOnly, ActivityDayTotal>(),
            new DateOnly(2025, 1, 1), Today, 2026, Culture);
        var dates = calendar.Months.SelectMany(month => month.Weeks)
            .SelectMany(week => week.Days).Where(day => day.IsInRange).ToList();

        Assert.Equal(12, calendar.Months.Count);
        Assert.Equal("Jan", calendar.Months[0].Label);
        Assert.Equal("Dec", calendar.Months[^1].Label);
        Assert.Equal(new DateOnly(2026, 1, 1), dates[0].Date);
        Assert.Equal(new DateOnly(2026, 12, 31), dates[^1].Date);
        Assert.Equal(365, dates.Count);
        Assert.Equal(31, calendar.Months[0].Weeks.SelectMany(week => week.Days).Count(day => day.IsInRange));
        Assert.All(calendar.Months.SelectMany(month => month.Weeks),
            week => Assert.Equal(DayOfWeek.Monday, week.StartsOn.DayOfWeek));
        Assert.Equal("Mon", calendar.WeekdayLabels[0]);
        var continuousDays = calendar.Weeks.SelectMany(week => week.Days)
            .Where(day => day.Date is not null).ToList();
        Assert.Equal(365, continuousDays.Count);
        Assert.Equal(365, continuousDays.Select(day => day.Date).Distinct().Count());
        var boundaryWeek = Assert.Single(calendar.Weeks,
            week => week.Days.Any(day => day.Date == new DateOnly(2026, 4, 1)));
        Assert.Equal(new DateOnly(2026, 3, 31), boundaryWeek.Days[1].Date);
        Assert.Equal(new DateOnly(2026, 4, 1), boundaryWeek.Days[2].Date);
        var aprilLine = Assert.Single(calendar.MonthMarkers, marker => marker.Month == 4);
        Assert.Contains("196,47 182,47", aprilLine.Points);
    }

    [Fact]
    public void CalendarYearDistinguishesSelectedDaysFutureDaysAndExpiredHistory()
    {
        var selected = new DateOnly(2026, 9, 20);
        var current = ActivityCalendar.BuildYear(new Dictionary<DateOnly, ActivityDayTotal>(),
            new DateOnly(2025, 1, 1), Today, 2026, Culture, selected);
        var currentDays = current.Months.SelectMany(month => month.Weeks).SelectMany(week => week.Days).ToList();

        Assert.True(Assert.Single(currentDays, day => day.Date == selected).IsSelected);
        var today = Assert.Single(currentDays, day => day.IsToday);
        Assert.Equal(Today, today.Date);
        Assert.False(today.IsSelected);
        var future = Assert.Single(currentDays, day => day.Date == new DateOnly(2026, 12, 31));
        Assert.False(future.IsTracked);
        Assert.Contains("future day", future.Label);

        var previous = ActivityCalendar.BuildYear(new Dictionary<DateOnly, ActivityDayTotal>(),
            new DateOnly(2025, 1, 1), Today, 2025, Culture);
        var expired = Assert.Single(previous.Months.SelectMany(month => month.Weeks)
            .SelectMany(week => week.Days), day => day.Date == new DateOnly(2025, 1, 1));
        Assert.False(expired.IsTracked);
        Assert.Contains("expired", expired.Label);
        Assert.DoesNotContain(previous.Weeks.SelectMany(week => week.Days), day => day.IsToday);
    }

    [Fact]
    public void CalendarOutlinesSelectedWeekAndMonthWithoutChangingActivityIntensity()
    {
        var activeDay = new DateOnly(2026, 9, 23);
        var totals = new Dictionary<DateOnly, ActivityDayTotal>
        {
            [activeDay] = new(TimeSpan.FromMinutes(12), 1, 1)
        };
        var week = ActivityCalendar.BuildYear(totals, new DateOnly(2026, 1, 1), Today,
            2026, Culture, new DateOnly(2026, 9, 21), new DateOnly(2026, 9, 27));
        var weekDays = week.Weeks.SelectMany(item => item.Days).ToList();
        Assert.Equal(7, weekDays.Count(day => day.IsSelected));
        Assert.Equal(4, Assert.Single(weekDays, day => day.Date == activeDay).Intensity);
        var future = Assert.Single(weekDays, day => day.Date == new DateOnly(2026, 9, 27));
        Assert.True(future.IsSelected);
        Assert.False(future.IsTracked);
        var todayInWeek = Assert.Single(weekDays, day => day.IsToday);
        Assert.True(todayInWeek.IsSelected);
        Assert.Equal(0, todayInWeek.Intensity);

        var month = ActivityCalendar.BuildYear(totals, new DateOnly(2026, 1, 1), Today,
            2026, Culture, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));
        Assert.Equal(30, month.Weeks.SelectMany(item => item.Days).Count(day => day.IsSelected));
    }

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
