using System;
using System.Globalization;
using System.Linq;
using QuickerPlaces.Models.Activity;
using QuickerPlaces.Models.RecentFiles;
using QuickerPlaces.Services;
using QuickerPlaces.Services.Activity;
using QuickerPlaces.Services.History;
using QuickerPlaces.Services.RecentFiles;
using QuickerPlaces.Services.Sessions;
using QuickerPlaces.Tests.Fakes;
using QuickerPlaces.ViewModels;
using Xunit;
using static QuickerPlaces.Tests.Fakes.ActivityFixtures;

namespace QuickerPlaces.Tests;

/// <summary>
/// History plan §5: Recents and the Library read past months from the
/// activity history when the year strip or a chosen period needs them, and
/// let them go when it no longer does. This PC's days the stores still hold
/// are never counted twice; another PC's are added.
/// </summary>
public sealed class HistoryReadingTests
{
    private const string Jobs = @"C:\Jobs";
    private const string OldPdf = @"C:\Jobs\Acme\Old.pdf";
    private static readonly DateOnly LongAgo = new(2025, 3, 10);
    private static readonly DateOnly MonthsAgo = new(2026, 5, 4);

    private readonly ManualTimeProvider _time = new();
    private readonly FakeHistoryFolder _folder = new();

    private ActivityHistory History(string machine = "DESK-1") => new(_folder, _time, machine);

    private static TrackedRoot Day(DateOnly date, string folder, double minutes, bool withDetail = true)
    {
        var root = new TrackedRoot { Path = Jobs };
        if (withDetail)
        {
            root.Days[date] = new DayActivity();
            root.Days[date].Folders[folder] = new FolderTotal { Milliseconds = (long)(minutes * 60_000), Visits = 2, LastSeenAt = new DateTimeOffset(date.ToDateTime(new TimeOnly(1, 0)), TimeSpan.Zero) };
        }

        root.DayTotals[date] = new DayTotal { Milliseconds = (long)(minutes * 60_000), Visits = 2, Folders = 1 };
        return root;
    }

    /// <summary>The store with C:\Jobs tracked from today, saving to (and reading) the fake history.</summary>
    private ActivityStore Store(out string rootId)
    {
        var store = new ActivityStore(new FakePlacesStorage(), _time, History());
        rootId = AddRoot(store).RootId;
        return store;
    }

    private ActivityViewModel Recents(ActivityStore store) => new(store, () => { }, _time, CultureInfo.InvariantCulture);

    // ---------------------------------------------------------------
    // Reading and the cache
    // ---------------------------------------------------------------

    [Fact]
    public void RecentFilesAndDayTotals_KeepTheSameYear_SoOneCutoffServesBoth()
        => Assert.Equal(RecentFilesStore.RetentionDays, ActivityStore.TotalDays);

    [Fact]
    public void ReadMonth_LeavesOutThisPcsHeldDays_ButNotAnotherPcs()
    {
        Assert.True(History("DESK-1").SaveFolders(new[] { Day(Today, Acme, 30), Day(Today.AddDays(-80), Acme, 10) }));
        Assert.True(History("LAPTOP").SaveFolders(new[] { Day(Today, Beta, 20) }));
        var cutoffs = HistoryCutoffs.For(Today);

        var september = History().ReadMonth(2026, 9, cutoffs)!.Roots.Single();

        Assert.Equal(new[] { Beta }, september.Days[Today].Folders.Keys);
        Assert.Equal(20 * 60_000, september.Totals[Today].Milliseconds);
        var july = History().ReadMonth(2026, 7, cutoffs)!.Roots.Single();
        Assert.Single(july.Days);
    }

    [Fact]
    public void MonthIndex_SaysWhoseFilesEachMonthHas()
    {
        Assert.True(History("DESK-1").SaveFolders(new[] { Day(LongAgo, Acme, 30), Day(Today, Acme, 30) }));
        Assert.True(History("LAPTOP").SaveFolders(new[] { Day(Today, Beta, 20) }));

        Assert.Equal(new[]
        {
            new HistoryMonthInfo(2025, 3, HasOwn: true, HasOthers: false),
            new HistoryMonthInfo(2026, 9, HasOwn: true, HasOthers: true),
        }, History().MonthIndex());
    }

    [Fact]
    public void Needed_ReadsAnotherPcsMonths_AndThisPcsOnlyBeforeTheCutoff()
    {
        var index = new[]
        {
            new HistoryMonthInfo(2026, 6, true, false),
            new HistoryMonthInfo(2026, 8, true, false),
            new HistoryMonthInfo(2026, 9, true, true),
        };

        Assert.Equal(new[] { (2026, 6), (2026, 9) },
            HistoryMonthCache.Needed(index, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), new DateOnly(2026, 8, 1)));
    }

    [Fact]
    public void Load_KeepsOnlyWhatWasAskedForLast()
    {
        Assert.True(History().SaveFolders(new[] { Day(LongAgo, Acme, 30), Day(MonthsAgo, Acme, 30) }));
        var cache = new HistoryMonthCache(History());
        var cutoffs = HistoryCutoffs.For(Today);

        Assert.Equal(2, cache.Load(new[] { (2025, 3), (2026, 5) }, cutoffs).Count);
        Assert.Equal(2, cache.LoadedCount);

        Assert.Single(cache.Load(new[] { (2026, 5) }, cutoffs));
        Assert.Equal(1, cache.LoadedCount);

        Assert.Empty(cache.Load(Array.Empty<(int, int)>(), cutoffs));
        Assert.Equal(0, cache.LoadedCount);
    }

    // ---------------------------------------------------------------
    // Recents
    // ---------------------------------------------------------------

    [Fact]
    public void Recents_ADayLongAgo_ListsItsFoldersFromHistory_ThenLetsThemGo()
    {
        Assert.True(History().SaveFolders(new[] { Day(LongAgo, Acme, 60) }));
        var vm = Recents(Store(out _));

        vm.ShowDay(LongAgo);

        var row = Assert.Single(vm.PeriodRows);
        Assert.Equal(Acme, row.Folder);
        Assert.DoesNotContain("expired", vm.PeriodNotice);
        Assert.DoesNotContain("Tracking started", vm.PeriodNotice);
        Assert.Equal(1, vm.LoadedHistoryMonths);

        vm.ShowToday();
        Assert.Empty(vm.PeriodRows);
        Assert.Equal(0, vm.LoadedHistoryMonths);
    }

    [Fact]
    public void Recents_TheYearListAndStrip_ReachBackAsFarAsTheHistory()
    {
        Assert.True(History().SaveFolders(new[] { Day(LongAgo, Acme, 60) }));
        var vm = Recents(Store(out _));

        Assert.Equal(2025, vm.EarliestCalendarYear);
        Assert.True(vm.SelectCalendarYear(2025));

        var cell = vm.CalendarWeeks.SelectMany(w => w.Days).Single(c => c.Date == LongAgo);
        Assert.True(cell.IsTracked);
        Assert.True(cell.Intensity > 0);
        Assert.Contains("1h", cell.Label);
    }

    [Fact]
    public void Recents_ADayWithOnlyATotal_StillSaysItsFoldersExpired()
    {
        // Saved before this history began keeping detail: a year-old total, no folders.
        var day = new DateOnly(2025, 6, 2);
        Assert.True(History().SaveFolders(new[] { Day(day, Acme, 45, withDetail: false) }));
        var vm = Recents(Store(out _));

        vm.ShowDay(day);

        Assert.Empty(vm.PeriodRows);
        Assert.Contains("have expired", vm.PeriodNotice);
        Assert.StartsWith("45m total", vm.PeriodSummary);
    }

    [Fact]
    public void Recents_Today_AddsAnotherPcsTime_AndNeverCountsThisPcsCopyTwice()
    {
        var store = Store(out var rootId);
        store.Record(new[] { Interval(rootId, Acme, Today, 30 * 60, startsVisit: true) });
        Assert.True(History("DESK-1").SaveFolders(new[] { Day(Today, Acme, 30) }));
        var laptop = Day(Today, Acme, 15);
        laptop.Days[Today].Folders[Beta] = new FolderTotal { Milliseconds = 5 * 60_000, Visits = 1 };
        Assert.True(History("LAPTOP").SaveFolders(new[] { laptop }));
        var vm = Recents(store);

        vm.ShowDay(Today);

        Assert.Equal(new[] { Acme, Beta }, vm.PeriodRows.Select(r => r.Folder));
        Assert.Equal("50m in 2 folders", vm.PeriodSummary);
    }

    // ---------------------------------------------------------------
    // The Library
    // ---------------------------------------------------------------

    private LibraryViewModel Library(ActivityStore store)
    {
        var places = new PlacesService(new FakePlacesStorage(), _time);
        var shell = new FakeShell();
        return new LibraryViewModel(places, new SessionStore(new FakePlacesStorage(), _time), store,
            new RecentFilesStore(new FakePlacesStorage(), _time), new PlaceLauncher(places, shell), shell, _time, CultureInfo.InvariantCulture);
    }

    [Fact]
    public void Library_APeriodLongAgo_ListsFoldersAndFilesFromHistory_AndTheUndatedListDoesNot()
    {
        Assert.True(History().SaveFolders(new[] { Day(LongAgo, Acme, 60) }));
        Assert.True(History().SaveFiles(new[] { new RecentFileRecord { Path = OldPdf, Opens = { new DateTimeOffset(2025, 3, 10, 1, 0, 0, TimeSpan.Zero) } } }));
        var vm = Library(Store(out _));
        Assert.DoesNotContain(vm.Rows, r => r.Name == "Old.pdf");

        vm.SelectCalendarYear(2025);
        vm.SelectCalendarDate(LongAgo);

        Assert.Contains(vm.Rows, r => r.Name == "Acme");
        Assert.Contains(vm.Rows, r => r.Name == "Old.pdf");
        Assert.DoesNotContain(vm.PeriodNotes, n => n.Contains("aren't listed", StringComparison.Ordinal));

        vm.ClearPeriod();
        Assert.DoesNotContain(vm.Rows, r => r.Name == "Old.pdf");
    }

    [Fact]
    public void Library_TheYearStrip_ShadesYearsFromHistory_AndLetsThemGoAfter()
    {
        Assert.True(History().SaveFolders(new[] { Day(LongAgo, Acme, 60) }));
        var vm = Library(Store(out _));
        Assert.Equal(0, vm.LoadedHistoryMonths);

        Assert.True(vm.SelectCalendarYear(2025));
        var cell = vm.CalendarWeeks.SelectMany(w => w.Days).Single(c => c.Date == LongAgo);
        Assert.True(cell.IsTracked);
        Assert.True(cell.Intensity > 0);
        Assert.Equal(1, vm.LoadedHistoryMonths);

        Assert.True(vm.SelectCalendarYear(2026));
        Assert.Equal(0, vm.LoadedHistoryMonths);
    }

    [Fact]
    public void Library_ASearch_FindsFoldersInHistory()
    {
        Assert.True(History().SaveFolders(new[] { Day(MonthsAgo, Acme, 60), Day(MonthsAgo.AddDays(1), Beta, 10) }));
        var vm = Library(Store(out _));

        vm.SearchText = "acme";

        var heat = vm.CalendarWeeks.SelectMany(w => w.Days).Where(c => c.Date is { } d && d >= MonthsAgo && d <= MonthsAgo.AddDays(1)).ToList();
        Assert.True(heat.Single(c => c.Date == MonthsAgo).Intensity > 0);
        Assert.True(heat.Single(c => c.Date == MonthsAgo.AddDays(1)).Intensity <= 0);
    }
}
