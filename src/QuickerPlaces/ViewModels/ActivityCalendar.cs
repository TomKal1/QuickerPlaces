using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using QuickerPlaces.Services.Activity;

namespace QuickerPlaces.ViewModels;

/// <summary>One year of day cells, laid out by culture-specific weeks.</summary>
public static class ActivityCalendar
{
    public static ActivityCalendarResult Build(IReadOnlyDictionary<DateOnly, ActivityDayTotal> totals,
        DateOnly trackingStartedOn, DateOnly today, CultureInfo culture)
    {
        var first = today.AddDays(-364);
        var firstDayOfWeek = (int)culture.DateTimeFormat.FirstDayOfWeek;
        var firstWeek = first.AddDays(-(((int)first.DayOfWeek - firstDayOfWeek + 7) % 7));
        var lastWeek = today.AddDays(-(((int)today.DayOfWeek - firstDayOfWeek + 7) % 7));
        var nonzero = totals.Where(pair => pair.Key >= first && pair.Key <= today && pair.Value.Time > TimeSpan.Zero)
            .Select(pair => pair.Value.Time).OrderBy(value => value).ToArray();
        var weeks = new List<ActivityCalendarWeek>();

        for (var week = firstWeek; week <= lastWeek; week = week.AddDays(7))
        {
            var cells = new ActivityCalendarCell[7];
            for (var offset = 0; offset < 7; offset++)
            {
                var date = week.AddDays(offset);
                if (date < first || date > today)
                {
                    cells[offset] = new ActivityCalendarCell(null, false, false, -1, "");
                    continue;
                }

                var tracked = date >= trackingStartedOn;
                totals.TryGetValue(date, out var total);
                var intensity = !tracked ? -1 : total is null || total.Time <= TimeSpan.Zero
                    ? 0 : Math.Clamp((int)Math.Ceiling(4.0 * UpperRank(nonzero, total.Time) / nonzero.Length), 1, 4);
                var dateText = date.ToDateTime(TimeOnly.MinValue).ToString("ddd d MMM yyyy", culture);
                var label = !tracked
                    ? $"{dateText} — not tracked; tracking started {trackingStartedOn.ToDateTime(TimeOnly.MinValue).ToString("d", culture)}"
                    : total is null || total.Time <= TimeSpan.Zero
                        ? $"{dateText} — no activity"
                        : $"{dateText} — {ActivityFormat.Duration(total.Time)} in {total.Folders} {(total.Folders == 1 ? "folder" : "folders")}";
                cells[offset] = new ActivityCalendarCell(date, true, tracked, intensity, label);
            }
            weeks.Add(new ActivityCalendarWeek(week, cells));
        }

        var labels = Enumerable.Range(0, 7)
            .Select(offset => culture.DateTimeFormat.GetAbbreviatedDayName(
                (DayOfWeek)((firstDayOfWeek + offset) % 7))).ToArray();
        return new ActivityCalendarResult(weeks, labels);
    }

    private static int UpperRank(IReadOnlyList<TimeSpan> sorted, TimeSpan value)
    {
        var low = 0;
        var high = sorted.Count;
        while (low < high)
        {
            var mid = low + (high - low) / 2;
            if (sorted[mid] <= value) low = mid + 1;
            else high = mid;
        }
        return low;
    }
}

public sealed record ActivityCalendarResult(IReadOnlyList<ActivityCalendarWeek> Weeks,
    IReadOnlyList<string> WeekdayLabels);

public sealed record ActivityCalendarWeek(DateOnly StartsOn, IReadOnlyList<ActivityCalendarCell> Days);

public sealed record ActivityCalendarCell(DateOnly? Date, bool IsInRange, bool IsTracked,
    int Intensity, string Label);
