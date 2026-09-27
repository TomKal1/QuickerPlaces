using System;
using System.Linq;
using QuickerPlaces.Services.Activity;
using QuickerPlaces.Tests.Fakes;
using Xunit;
using static QuickerPlaces.Tests.Fakes.ActivityFixtures;

namespace QuickerPlaces.Tests;

/// <summary>
/// Retention (Phase 9 plan 5.2, D16, D20): folder detail is kept for today
/// and the 61 days before it, day totals for today and the 364 before.
/// Pruning runs at load, in memory only, and in the first flush of each new
/// local day. A period reaching past the detail window returns what is
/// still stored and says so.
/// </summary>
public sealed class ActivityStoreRetentionTests
{
    private static readonly DateOnly LastDetailDay = Today.AddDays(-61);    // 2026-07-26
    private static readonly DateOnly FirstExpiredDetail = Today.AddDays(-62);
    private static readonly DateOnly LastTotalDay = Today.AddDays(-364);    // 2025-09-26
    private static readonly DateOnly FirstExpiredTotal = Today.AddDays(-365);

    private readonly FakePlacesStorage _storage = new();
    private readonly string _jobs;

    /// <summary>
    /// Writes a document from a year before "today", when every one of these
    /// dates was still in the future and so nothing was pruned, with one
    /// folder's minute on each.
    /// </summary>
    public ActivityStoreRetentionTests()
    {
        var past = new ActivityStore(_storage, new ManualTimeProvider(new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero)));
        _jobs = AddRoot(past).RootId;
        past.Record(new[] { FirstExpiredTotal, LastTotalDay, FirstExpiredDetail, LastDetailDay, Today }
            .Select(day => Interval(_jobs, Acme, day, 60, true)));
        Assert.True(past.Flush().Saved);
    }

    private ActivityStore LoadToday(ManualTimeProvider? time = null) => new(_storage, time ?? new ManualTimeProvider());

    private static bool HasDetail(ActivityStore store, string rootId, DateOnly day)
        => store.QueryPeriod(rootId, day, day)!.Folders.Count > 0;

    [Fact]
    public void AtLoad_DetailOlderThan62Days_IsDeleted_AndNewerIsKept()
    {
        var store = LoadToday();

        Assert.False(HasDetail(store, _jobs, FirstExpiredDetail));
        Assert.True(HasDetail(store, _jobs, LastDetailDay));
        Assert.True(HasDetail(store, _jobs, Today));
    }

    [Fact]
    public void AtLoad_TotalsOlderThan365Days_AreDeleted_AndNewerAreKept()
    {
        var totals = LoadToday().QueryDayTotals(_jobs)!;

        Assert.False(totals.ContainsKey(FirstExpiredTotal));
        Assert.Equal(
            new[] { LastTotalDay, FirstExpiredDetail, LastDetailDay, Today },
            totals.Keys.OrderBy(d => d));
    }

    [Fact]
    public void ADayBetweenTheTwoWindows_HasATotal_AndNoDetail()
    {
        var store = LoadToday();

        Assert.Equal(TimeSpan.FromMinutes(1), store.QueryDayTotals(_jobs)![FirstExpiredDetail].Time);
        var period = store.QueryPeriod(_jobs, FirstExpiredDetail, FirstExpiredDetail)!;
        Assert.Empty(period.Folders);
        Assert.True(period.DetailExpired);
    }

    [Fact]
    public void WhileBothExist_EachDaysTotalEqualsTheSumOfItsFolders()
    {
        var store = LoadToday();

        foreach (var day in new[] { LastDetailDay, Today })
        {
            var folders = store.QueryPeriod(_jobs, day, day)!.Folders;
            Assert.Equal(folders.Aggregate(TimeSpan.Zero, (sum, f) => sum + f.Time), store.QueryDayTotals(_jobs)![day].Time);
        }
    }

    [Fact]
    public void PruningAtLoad_WritesNothing_AndReachesDiskWithTheNextWrite()
    {
        var writes = _storage.WriteCount;
        var store = LoadToday();
        Assert.Equal(writes, _storage.WriteCount);

        store.Record(new[] { Interval(_jobs, Acme, Today, 1) });
        store.Flush();

        Assert.DoesNotContain(FirstExpiredDetail.ToString("yyyy-MM-dd"), _storage.LastWritten!.Split("dayTotals")[0]);
        Assert.DoesNotContain(FirstExpiredTotal.ToString("yyyy-MM-dd"), _storage.LastWritten);
    }

    [Fact]
    public void TheFirstFlushOfANewDay_Prunes_AndWrites()
    {
        var time = new ManualTimeProvider();
        var store = LoadToday(time);
        store.Flush();
        var writes = _storage.WriteCount;

        time.Advance(TimeSpan.FromDays(1));
        Assert.True(store.Flush().Saved);

        Assert.Equal(writes + 1, _storage.WriteCount);
        Assert.False(HasDetail(store, _jobs, LastDetailDay));
        Assert.False(store.QueryDayTotals(_jobs)!.ContainsKey(LastTotalDay));
        Assert.True(HasDetail(store, _jobs, Today));
    }

    [Fact]
    public void APeriodReachingPastTheDetailWindow_ReturnsWhatIsStored_AndSaysSo()
    {
        var period = LoadToday().QueryPeriod(_jobs, Today.AddDays(-70), Today)!;

        Assert.True(period.DetailExpired);
        Assert.Equal(TimeSpan.FromMinutes(2), Assert.Single(period.Folders).Time);   // LastDetailDay and today only
    }
}
