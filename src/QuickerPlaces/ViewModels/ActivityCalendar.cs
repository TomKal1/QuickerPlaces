using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using QuickerPlaces.Services.Activity;

namespace QuickerPlaces.ViewModels;

/// <summary>One year of day cells, laid out by culture-specific weeks.</summary>
public static class ActivityCalendar
{
    /// <summary>Beyond the strip itself: the weekday labels, the panel's border and padding, a little room, plus the unit buttons' column.</summary>
    public const double YearChrome = 150;

    /// <summary>
    /// True when the Year activity panel should show one month rather than the
    /// whole year: when the strip hasn't been built yet (<paramref name="stripWidth"/>
    /// is 0, so there is no year to show), or the panel is narrower than the
    /// strip and its <see cref="YearChrome"/>.
    /// </summary>
    public static bool ShowsMonthView(double panelWidth, int stripWidth)
        => stripWidth <= 0 || panelWidth < stripWidth + YearChrome;

    /// <summary>A January-to-December activity strip with aligned weeks and month markers, from folder totals.</summary>
    public static ActivityCalendarYearResult BuildYear(IReadOnlyDictionary<DateOnly, ActivityDayTotal> totals,
        DateOnly trackingStartedOn, DateOnly today, int year, CultureInfo culture,
        DateOnly? selectedFrom = null, DateOnly? selectedTo = null)
        => BuildYear(totals.ToDictionary(pair => pair.Key, pair => new CalendarDay(
                pair.Value.Time.TotalMilliseconds,
                $"{ActivityFormat.Duration(pair.Value.Time)} in {pair.Value.Folders} {(pair.Value.Folders == 1 ? "folder" : "folders")}")),
            trackingStartedOn, today, year, culture, selectedFrom, selectedTo);

    /// <summary>
    /// The same year strip from any per-day weight: a day's shade ranks its
    /// weight among the year's other nonzero days, and its label is
    /// "date — summary". Recent Files (opens per day) and project sessions
    /// (reopens per day) use this; folders use the overload above.
    /// </summary>
    public static ActivityCalendarYearResult BuildYear(IReadOnlyDictionary<DateOnly, CalendarDay> days,
        DateOnly trackingStartedOn, DateOnly today, int year, CultureInfo culture,
        DateOnly? selectedFrom = null, DateOnly? selectedTo = null)
    {
        selectedTo ??= selectedFrom;
        var first = new DateOnly(year, 1, 1);
        var last = new DateOnly(year, 12, 31);
        var keptFrom = today.AddDays(-364);
        var firstDayOfWeek = (int)culture.DateTimeFormat.FirstDayOfWeek;
        var nonzero = days.Where(pair => pair.Key >= first && pair.Key <= last &&
                pair.Key >= keptFrom && pair.Key <= today && pair.Value.Weight > 0)
            .Select(pair => pair.Value.Weight).OrderBy(value => value).ToArray();
        var months = new List<ActivityCalendarMonth>(12);

        for (var month = 1; month <= 12; month++)
        {
            var monthFirst = new DateOnly(year, month, 1);
            var monthLast = monthFirst.AddMonths(1).AddDays(-1);
            var firstWeek = monthFirst.AddDays(-(((int)monthFirst.DayOfWeek - firstDayOfWeek + 7) % 7));
            var weeks = new List<ActivityCalendarWeek>();
            for (var week = firstWeek; week <= monthLast; week = week.AddDays(7))
            {
                var cells = new ActivityCalendarCell[7];
                for (var offset = 0; offset < 7; offset++)
                {
                    var date = week.AddDays(offset);
                    if (date.Month != month || date.Year != year)
                    {
                        cells[offset] = new ActivityCalendarCell(null, false, false, -1, "");
                        continue;
                    }

                    var dateText = date.ToDateTime(TimeOnly.MinValue).ToString("ddd d MMM yyyy", culture);
                    var isSelected = selectedFrom is { } from && selectedTo is { } to &&
                        date >= from && date <= to;
                    if (date > today || date < trackingStartedOn || date < keptFrom)
                    {
                        var reason = date > today ? "future day"
                            : date < trackingStartedOn ? $"not tracked; tracking started {trackingStartedOn.ToDateTime(TimeOnly.MinValue).ToString("d", culture)}"
                            : "activity history expired";
                        cells[offset] = new ActivityCalendarCell(date, true, false, -1,
                            $"{dateText} — {reason}", isSelected, date == today);
                        continue;
                    }

                    days.TryGetValue(date, out var day);
                    if (day is { Unknown: true })
                    {
                        cells[offset] = new ActivityCalendarCell(date, true, false, -1,
                            $"{dateText} — {day.Summary}", isSelected, date == today);
                        continue;
                    }

                    var intensity = day is null || day.Weight <= 0 ? 0
                        : Math.Clamp((int)Math.Ceiling(4.0 * UpperRank(nonzero, day.Weight) / nonzero.Length), 1, 4);
                    var label = day is null || day.Weight <= 0
                        ? $"{dateText} — no activity"
                        : $"{dateText} — {day.Summary}";
                    cells[offset] = new ActivityCalendarCell(date, true, true, intensity, label,
                        isSelected, date == today);
                }
                weeks.Add(new ActivityCalendarWeek(week, cells));
            }
            months.Add(new ActivityCalendarMonth(month,
                culture.DateTimeFormat.GetAbbreviatedMonthName(month), weeks));
        }

        var dayCells = months.SelectMany(month => month.Weeks).SelectMany(week => week.Days)
            .Where(cell => cell.Date is not null).ToDictionary(cell => cell.Date!.Value);
        var firstYearWeek = first.AddDays(-(((int)first.DayOfWeek - firstDayOfWeek + 7) % 7));
        var lastYearWeek = last.AddDays(-(((int)last.DayOfWeek - firstDayOfWeek + 7) % 7));
        var yearWeeks = new List<ActivityCalendarWeek>();
        for (var week = firstYearWeek; week <= lastYearWeek; week = week.AddDays(7))
        {
            var cells = new ActivityCalendarCell[7];
            for (var offset = 0; offset < 7; offset++)
            {
                var date = week.AddDays(offset);
                cells[offset] = dayCells.GetValueOrDefault(date)
                    ?? new ActivityCalendarCell(null, false, false, -1, "");
            }
            yearWeeks.Add(new ActivityCalendarWeek(week, cells));
        }

        var markers = new List<ActivityCalendarMonthMarker>(12);
        foreach (var month in months)
        {
            var date = new DateOnly(year, month.Month, 1);
            var dayOffset = date.DayNumber - firstYearWeek.DayNumber;
            var leftX = dayOffset / 7 * 14;
            var row = dayOffset % 7;
            var topX = leftX + (row == 0 ? 0 : 14);
            var splitY = 19 + row * 14;
            var points = row == 0
                ? $"{topX + 9},0 {topX},17 {topX},117"
                : $"{topX + 9},0 {topX},17 {topX},{splitY} {leftX},{splitY} {leftX},117";
            markers.Add(new ActivityCalendarMonthMarker(month.Month, month.Label, topX + 12, points));
        }

        var labels = Enumerable.Range(0, 7)
            .Select(offset => culture.DateTimeFormat.GetAbbreviatedDayName(
                (DayOfWeek)((firstDayOfWeek + offset) % 7))).ToArray();
        return new ActivityCalendarYearResult(year, months, yearWeeks, markers, yearWeeks.Count * 14, labels);
    }

    public static ActivityCalendarResult Build(IReadOnlyDictionary<DateOnly, ActivityDayTotal> totals,
        DateOnly trackingStartedOn, DateOnly today, CultureInfo culture)
    {
        var first = today.AddDays(-364);
        var firstDayOfWeek = (int)culture.DateTimeFormat.FirstDayOfWeek;
        var firstWeek = first.AddDays(-(((int)first.DayOfWeek - firstDayOfWeek + 7) % 7));
        var lastWeek = today.AddDays(-(((int)today.DayOfWeek - firstDayOfWeek + 7) % 7));
        var nonzero = totals.Where(pair => pair.Key >= first && pair.Key <= today && pair.Value.Time > TimeSpan.Zero)
            .Select(pair => pair.Value.Time.TotalMilliseconds).OrderBy(value => value).ToArray();
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
                    ? 0 : Math.Clamp((int)Math.Ceiling(4.0 * UpperRank(nonzero, total.Time.TotalMilliseconds) / nonzero.Length), 1, 4);
                var dateText = date.ToDateTime(TimeOnly.MinValue).ToString("ddd d MMM yyyy", culture);
                var label = !tracked
                    ? $"{dateText} — not tracked; tracking started {trackingStartedOn.ToDateTime(TimeOnly.MinValue).ToString("d", culture)}"
                    : total is null || total.Time <= TimeSpan.Zero
                        ? $"{dateText} — no activity"
                        : $"{dateText} — {ActivityFormat.Duration(total.Time)} in {total.Folders} {(total.Folders == 1 ? "folder" : "folders")}";
                cells[offset] = new ActivityCalendarCell(date, true, tracked, intensity, label,
                    IsToday: date == today);
            }
            weeks.Add(new ActivityCalendarWeek(week, cells));
        }

        var labels = Enumerable.Range(0, 7)
            .Select(offset => culture.DateTimeFormat.GetAbbreviatedDayName(
                (DayOfWeek)((firstDayOfWeek + offset) % 7))).ToArray();
        return new ActivityCalendarResult(weeks, labels);
    }

    private static int UpperRank(IReadOnlyList<double> sorted, double value)
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

/// <summary>
/// One day for the year strip: how much happened (any unit; only the ranking
/// matters) and what to say about it. An <paramref name="Unknown"/> day is
/// shown as untracked with its summary as the reason: what happened that day
/// can't be told for the current filter (configurable canvas plan D5).
/// </summary>
public sealed record CalendarDay(double Weight, string Summary, bool Unknown = false);

public sealed record ActivityCalendarResult(IReadOnlyList<ActivityCalendarWeek> Weeks,
    IReadOnlyList<string> WeekdayLabels);

public sealed record ActivityCalendarYearResult(int Year, IReadOnlyList<ActivityCalendarMonth> Months,
    IReadOnlyList<ActivityCalendarWeek> Weeks, IReadOnlyList<ActivityCalendarMonthMarker> MonthMarkers,
    int StripWidth, IReadOnlyList<string> WeekdayLabels);

public sealed record ActivityCalendarMonth(int Month, string Label, IReadOnlyList<ActivityCalendarWeek> Weeks);

public sealed record ActivityCalendarMonthMarker(int Month, string Label, int LabelX, string Points);

public sealed record ActivityCalendarWeek(DateOnly StartsOn, IReadOnlyList<ActivityCalendarCell> Days);

public sealed record ActivityCalendarCell(DateOnly? Date, bool IsInRange, bool IsTracked,
    int Intensity, string Label, bool IsSelected = false, bool IsToday = false)
{
    /// <summary>The day of the month, for the month view's numbered cells; null for a blank cell.</summary>
    public int? DayNumber => Date?.Day;
}
