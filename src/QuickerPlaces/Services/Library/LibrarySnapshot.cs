using System;
using System.Collections.Generic;
using System.Linq;
using QuickerPlaces.Models;
using QuickerPlaces.Models.History;
using QuickerPlaces.Services.Activity;
using QuickerPlaces.Services.Documents;
using QuickerPlaces.Services.History;
using QuickerPlaces.Services.RecentFiles;
using QuickerPlaces.Services.Sessions;

namespace QuickerPlaces.Services.Library;

/// <summary>
/// Everything the Library reads from its four sources, copied at one moment
/// (configurable canvas plan M2): saved places, saved sessions, Recents'
/// day totals and kept folder detail, and Recent Files' kept opens, with
/// today's date and the local zone.
///
/// Captured on the UI thread, because PlacesService and SessionStore are
/// UI-thread only; <see cref="LibraryQueryEngine"/> then works on it off
/// the UI thread. Session snapshots and the store records are immutable;
/// <see cref="Place"/> objects are shared, not copied, so opening a row still
/// opens the saved place itself, and the engine only reads their fields.
///
/// <see cref="WithHistory"/> adds loaded months of activity history (history
/// plan §5): they count toward the year strip and toward a chosen period, but
/// the list with no period stays what the stores hold.
/// UI-free and linked into the test project.
/// </summary>
/// <param name="HistoryFrom">The first day of the earliest history month added, or null when none was: folder detail is known back to it.</param>
/// <param name="HistoryFirstDay">The first day with any activity in the history added, or null.</param>
/// <param name="FilesKeptFrom">With history added, the first day Recent Files itself holds: earlier opens are listed only for a chosen period.</param>
/// <param name="FileSummaries">Every file Recent Files still lists, with its open count and last open: the list with no period. Null keeps the older rule (counted from <see cref="Files"/>).</param>
/// <param name="FileDayCounts">Opens per kind per day for the year Recent Files counts, for days whose opens it no longer holds (before <see cref="FilesDetailFrom"/>).</param>
/// <param name="FilesDetailFrom">The first day Recent Files holds each open for.</param>
/// <param name="HistoryMonths">The months of activity history added: on those days, opens come from the history instead of <see cref="FileDayCounts"/>.</param>
public sealed record LibrarySnapshot(
    IReadOnlyList<Place> Places,
    IReadOnlyList<SessionSnapshot> Sessions,
    bool RecentsAvailable,
    string? RecentsNotice,
    IReadOnlyList<RecentsRootData> Roots,
    DateOnly FolderDetailKeptFrom,
    bool RecentFilesAvailable,
    string? RecentFilesNotice,
    RecentFilesSettingsSnapshot RecentFilesSettings,
    IReadOnlyList<RecentFileHistory> Files,
    DateOnly Today,
    TimeZoneInfo Zone,
    DateOnly? HistoryFrom = null,
    DateOnly? HistoryFirstDay = null,
    DateOnly FilesKeptFrom = default,
    IReadOnlyList<RecentFileSummary>? FileSummaries = null,
    IReadOnlyDictionary<DateOnly, IReadOnlyDictionary<DocumentKind, int>>? FileDayCounts = null,
    DateOnly FilesDetailFrom = default,
    IReadOnlySet<(int Year, int Month)>? HistoryMonths = null)
{
    public static LibrarySnapshot Capture(PlacesService places, SessionStore sessions, ActivityStore activity,
        RecentFilesStore recentFiles, TimeProvider time)
    {
        var zone = time.LocalTimeZone;
        DateOnly Local(DateTimeOffset instant) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);

        var roots = activity.AllRoots
            .Select(root => new RecentsRootData(
                root.RootId,
                root.Enabled && (activity.TrackAllFolders ? root.Config.IsAllFolders : !root.Config.IsAllFolders),
                Local(root.TrackingStartedAt),
                activity.QueryDayTotals(root.RootId) ?? new Dictionary<DateOnly, ActivityDayTotal>(),
                activity.QueryFolderDays(root.RootId) ?? Array.Empty<FolderDay>(),
                root.Path,
                root.IsRemoved))
            .ToList();

        return new LibrarySnapshot(
            places.Places,
            sessions.Sessions,
            activity.IsAvailable,
            activity.Notice,
            roots,
            activity.DetailKeptFrom,
            recentFiles.IsAvailable,
            recentFiles.Notice,
            recentFiles.Settings,
            recentFiles.QueryHistory(),
            Local(time.GetUtcNow()),
            zone,
            FileSummaries: recentFiles.QuerySummary(),
            FileDayCounts: recentFiles.QueryDayCounts(),
            FilesDetailFrom: recentFiles.DetailKeptFrom);
    }

    /// <summary>
    /// This snapshot with <paramref name="months"/> of activity history added:
    /// each tracked folder's days and totals (matched by path), folders only
    /// another PC tracked as extra roots, and file opens. The months must
    /// leave out this PC's days the stores hold (HistoryMonthCache does).
    /// </summary>
    public LibrarySnapshot WithHistory(IReadOnlyList<HistoryMonthDocument> months)
    {
        if (months.Count == 0)
            return this;

        var roots = Roots.Select(root => root with
        {
            DayTotals = HistoryMerge.Add(root.DayTotals, HistoryMerge.DayTotals(months, root.Path)),
            FolderDays = root.FolderDays.Concat(HistoryMerge.FolderDays(months, root.Path)).ToList(),
            TrackingStartedOn = HistoryMerge.FirstDay(months, root.Path) is { } first && first < root.TrackingStartedOn ? first : root.TrackingStartedOn,
        }).ToList();

        foreach (var path in HistoryMerge.RootPaths(months).Where(p => !Roots.Any(r => string.Equals(r.Path, p, StringComparison.OrdinalIgnoreCase))))
        {
            roots.Add(new RecentsRootData("history:" + path, false, HistoryMerge.FirstDay(months, path) ?? Today,
                HistoryMerge.DayTotals(months, path), HistoryMerge.FolderDays(months, path), path));
        }

        var files = Files.ToDictionary(f => f.Path, f => f, StringComparer.OrdinalIgnoreCase);
        foreach (var (path, opens) in HistoryMerge.FileOpens(months))
        {
            if (DocumentKinds.FromPath(path) is not { } kind)
                continue;
            files[path] = files.TryGetValue(path, out var held)
                ? held with { Opens = held.Opens.Concat(opens).Distinct().OrderBy(o => o).ToList() }
                : new RecentFileHistory(path, kind, opens);
        }

        var firstOpen = files.Values.SelectMany(f => f.Opens).Select(LocalDate).DefaultIfEmpty(Today).Min();
        var firstFolder = HistoryMerge.FirstDay(months) ?? Today;
        return this with
        {
            Roots = roots,
            Files = files.Values.ToList(),
            HistoryFrom = months.Select(m => FirstOfMonth(m.Month)).OfType<DateOnly>().DefaultIfEmpty(Today).Min(),
            HistoryFirstDay = firstOpen < firstFolder ? firstOpen : firstFolder,
            FilesKeptFrom = HistoryCutoffs.For(Today).FilesFrom,
            HistoryMonths = months.Select(m => FirstOfMonth(m.Month)).OfType<DateOnly>().Select(d => (d.Year, d.Month)).ToHashSet(),
        };
    }

    private static DateOnly? FirstOfMonth(string month)
        => DateOnly.TryParseExact(month + "-01", "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var first) ? first : null;

    /// <summary>
    /// True when a day's file opens are known one by one: Recent Files holds
    /// them, or a history month for the day is loaded. Otherwise only
    /// <see cref="FileDayCounts"/> knows how many there were.
    /// </summary>
    public bool KnowsOpensOn(DateOnly date)
        => FileDayCounts is null || date >= FilesDetailFrom || HistoryMonths?.Contains((date.Year, date.Month)) == true;

    /// <summary>The local date of <paramref name="instant"/>.</summary>
    public DateOnly LocalDate(DateTimeOffset instant) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, Zone).DateTime);
}

/// <summary>One Recents root: whether it is tracking, since when, its day totals (a year) and its folder detail (<see cref="ActivityStore.DetailDays"/> days).</summary>
/// <param name="Path">The tracked folder's path, for scoping and folder levels.</param>
public sealed record RecentsRootData(
    string RootId,
    bool Enabled,
    DateOnly TrackingStartedOn,
    IReadOnlyDictionary<DateOnly, ActivityDayTotal> DayTotals,
    IReadOnlyList<FolderDay> FolderDays,
    string Path = "",
    bool IsRemoved = false);
