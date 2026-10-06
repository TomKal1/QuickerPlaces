using System;
using System.Collections.Generic;
using System.Linq;
using QuickerPlaces.Models.History;
using QuickerPlaces.Services.Activity;

namespace QuickerPlaces.Services.History;

/// <summary>
/// Turns loaded month files into the shapes Recents and the Library already
/// read (history plan §5): folder days and day totals for one tracked folder,
/// matched by path, and file opens. The months come from
/// <see cref="HistoryMonthCache"/> with this PC's held days left out, so
/// adding them to what the stores return never counts a day twice.
///
/// Pure. UI-free and linked into the test project.
/// </summary>
public static class HistoryMerge
{
    /// <summary>The root's days with folder detail, oldest first.</summary>
    public static IReadOnlyList<FolderDay> FolderDays(IEnumerable<HistoryMonthDocument> months, string rootPath)
        => RootsAt(months, rootPath)
            .SelectMany(r => r.Days)
            .GroupBy(d => d.Key)
            .OrderBy(g => g.Key)
            .Select(g => new FolderDay(g.Key, g.SelectMany(d => d.Value.Folders)
                .GroupBy(f => f.Key, StringComparer.OrdinalIgnoreCase)
                .Select(f => new FolderActivity(f.First().Key,
                    TimeSpan.FromMilliseconds(f.Sum(x => x.Value.Milliseconds)),
                    f.Sum(x => x.Value.Visits),
                    f.Max(x => x.Value.LastSeenAt)))
                .ToList()))
            .ToList();

    /// <summary>The root's day totals.</summary>
    public static Dictionary<DateOnly, ActivityDayTotal> DayTotals(IEnumerable<HistoryMonthDocument> months, string rootPath)
    {
        var totals = new Dictionary<DateOnly, ActivityDayTotal>();
        foreach (var root in RootsAt(months, rootPath))
        {
            foreach (var (date, total) in root.Totals)
            {
                var time = TimeSpan.FromMilliseconds(total.Milliseconds);
                totals[date] = totals.TryGetValue(date, out var existing)
                    ? new ActivityDayTotal(existing.Time + time, existing.Visits + total.Visits, Math.Max(existing.Folders, total.Folders))
                    : new ActivityDayTotal(time, total.Visits, total.Folders);
            }
        }

        return totals;
    }

    /// <summary><paramref name="into"/> with <paramref name="more"/> added day by day.</summary>
    public static Dictionary<DateOnly, ActivityDayTotal> Add(IReadOnlyDictionary<DateOnly, ActivityDayTotal> into, IReadOnlyDictionary<DateOnly, ActivityDayTotal> more)
    {
        var sum = into.ToDictionary(d => d.Key, d => d.Value);
        foreach (var (date, total) in more)
        {
            sum[date] = sum.TryGetValue(date, out var existing)
                ? new ActivityDayTotal(existing.Time + total.Time, existing.Visits + total.Visits, Math.Max(existing.Folders, total.Folders))
                : total;
        }

        return sum;
    }

    /// <summary>
    /// Folders summed over <paramref name="from"/> to <paramref name="to"/>
    /// from <paramref name="days"/>, joined to <paramref name="rows"/> (the
    /// store's own), most time first.
    /// </summary>
    public static IReadOnlyList<FolderActivity> SumFolders(IEnumerable<FolderActivity> rows, IEnumerable<FolderDay> days, DateOnly from, DateOnly to)
        => rows.Concat(days.Where(d => d.Date >= from && d.Date <= to).SelectMany(d => d.Folders))
            .GroupBy(f => f.Folder, StringComparer.OrdinalIgnoreCase)
            .Select(g => new FolderActivity(g.First().Folder, TimeSpan.FromTicks(g.Sum(f => f.Time.Ticks)), g.Sum(f => f.Visits), g.Max(f => f.LastVisited)))
            .OrderByDescending(f => f.Time)
            .ThenBy(f => f.Folder, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Every recorded open per file, oldest first.</summary>
    public static Dictionary<string, List<DateTimeOffset>> FileOpens(IEnumerable<HistoryMonthDocument> months)
    {
        var files = new Dictionary<string, List<DateTimeOffset>>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in months.SelectMany(m => m.Files))
        {
            if (!files.TryGetValue(file.Path, out var opens))
                files[file.Path] = opens = new List<DateTimeOffset>();
            opens.AddRange(file.Opens);
        }

        foreach (var path in files.Keys.ToList())
            files[path] = files[path].Distinct().OrderBy(o => o).ToList();
        return files;
    }

    /// <summary>The paths of every tracked folder in the months, each once.</summary>
    public static IReadOnlyList<string> RootPaths(IEnumerable<HistoryMonthDocument> months)
        => months.SelectMany(m => m.Roots).Select(r => r.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>The first day with any activity in the months, for one root or for all, or null.</summary>
    public static DateOnly? FirstDay(IEnumerable<HistoryMonthDocument> months, string? rootPath = null)
    {
        var roots = months.SelectMany(m => m.Roots).Where(r => rootPath is null || Same(r.Path, rootPath));
        var dates = roots.SelectMany(r => r.Days.Keys.Concat(r.Totals.Keys)).ToList();
        return dates.Count == 0 ? null : dates.Min();
    }

    private static IEnumerable<HistoryRoot> RootsAt(IEnumerable<HistoryMonthDocument> months, string rootPath)
        => months.SelectMany(m => m.Roots).Where(r => Same(r.Path, rootPath));

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
