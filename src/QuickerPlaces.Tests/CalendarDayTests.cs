using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using QuickerPlaces.ViewModels;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>The year strip built from any per-day weight (documents plan §6), as Recent Files and sessions use it.</summary>
public sealed class CalendarDayTests
{
    private static readonly DateOnly Today = new(2026, 9, 28);

    [Fact]
    public void Weights_AreRankedIntoShades_AndSummariesBecomeLabels()
    {
        var days = new Dictionary<DateOnly, CalendarDay>
        {
            [Today] = new(4, "4 opens of 3 files"),
            [Today.AddDays(-1)] = new(1, "1 open of 1 file"),
            [Today.AddDays(-2)] = new(0, "ignored"),
        };

        var year = ActivityCalendar.BuildYear(days, Today.AddDays(-30), Today, 2026, CultureInfo.InvariantCulture);
        var cells = year.Weeks.SelectMany(w => w.Days).Where(c => c.Date is not null).ToDictionary(c => c.Date!.Value);

        Assert.Equal(4, cells[Today].Intensity);
        Assert.Equal(2, cells[Today.AddDays(-1)].Intensity);
        Assert.Equal(0, cells[Today.AddDays(-2)].Intensity);
        Assert.EndsWith("— 4 opens of 3 files", cells[Today].Label);
        Assert.EndsWith("— no activity", cells[Today.AddDays(-2)].Label);
        Assert.Equal(-1, cells[Today.AddDays(-31)].Intensity);
        Assert.Contains("not tracked", cells[Today.AddDays(-31)].Label);
        Assert.Equal(-1, cells[Today.AddDays(1)].Intensity);
    }
}
