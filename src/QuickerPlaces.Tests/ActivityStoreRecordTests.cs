using System;
using System.Linq;
using QuickerPlaces.Services.Activity;
using QuickerPlaces.Tests.Fakes;
using Xunit;
using static QuickerPlaces.Tests.Fakes.ActivityFixtures;

namespace QuickerPlaces.Tests;

/// <summary>
/// Recording the tracker's intervals and reading them back (Phase 9 plan
/// 5.2, D10, D20, D34–D36): recorded time is in memory at once and on disk
/// only when flushed; durations sum exactly; a visit is counted where one
/// starts; folders are one row however they are cased; day totals are the
/// sums of their folders; and a failed flush loses nothing.
/// </summary>
public sealed class ActivityStoreRecordTests
{
    private readonly FakePlacesStorage _storage = new();
    private readonly ManualTimeProvider _time = new();
    private readonly ActivityStore _store;
    private readonly string _jobs;

    public ActivityStoreRecordTests()
    {
        _store = new ActivityStore(_storage, _time);
        _jobs = AddRoot(_store).RootId;
    }

    private FolderActivity OnlyFolderToday() => Assert.Single(_store.QueryPeriod(_jobs, Today, Today)!.Folders);

    [Fact]
    public void Recording_DoesNotWrite_AndAFlushWritesOnlyWhenThereIsSomethingNew()
    {
        _store.Record(new[] { Interval(_jobs, Acme, Today, 1.5, true) });
        Assert.Equal(1, _storage.WriteCount);
        Assert.True(_store.HasUnsavedChanges);

        Assert.True(_store.Flush().Saved);
        Assert.Equal(2, _storage.WriteCount);
        Assert.False(_store.HasUnsavedChanges);

        Assert.True(_store.Flush().Saved);
        Assert.Equal(2, _storage.WriteCount);

        _store.Record(new[] { Interval(_jobs, Acme, Today, 1.5) });
        _store.Flush();
        Assert.Equal(3, _storage.WriteCount);
    }

    [Fact]
    public void RecordedTime_IsQueryableBeforeItIsFlushed()
    {
        _store.Record(new[] { Interval(_jobs, Acme, Today, 12, true) });

        Assert.Equal(TimeSpan.FromSeconds(12), OnlyFolderToday().Time);
    }

    [Fact]
    public void SubSecondDurations_SumExactly()
    {
        _store.Record(Enumerable.Repeat(Interval(_jobs, Acme, Today, 1.5), 3).Append(Interval(_jobs, Acme, Today, 0.001)));

        Assert.Equal(TimeSpan.FromSeconds(4.501), OnlyFolderToday().Time);
    }

    [Fact]
    public void AVisit_IsCountedOnlyWhereOneStarts_EvenWithNoTime()
    {
        _store.Record(new[]
        {
            Interval(_jobs, Acme, Today, 0, startsVisit: true),
            Interval(_jobs, Acme, Today, 1.5),
            Interval(_jobs, Acme, Today, 1.5, startsVisit: true),
        });

        Assert.Equal(2, OnlyFolderToday().Visits);
    }

    [Fact]
    public void LastVisited_IsTheLatestTimeSeen()
    {
        var later = new DateTimeOffset(2026, 9, 25, 5, 0, 0, TimeSpan.Zero);
        _store.Record(new[]
        {
            Interval(_jobs, Acme, Today, 1.5, lastSeenAt: later),
            Interval(_jobs, Acme, Today, 1.5, lastSeenAt: later.AddHours(-2)),
        });

        Assert.Equal(later, OnlyFolderToday().LastVisited);
    }

    [Fact]
    public void OneFolderCasedTwoWays_IsOneRow_UnderItsFirstSpelling()
    {
        _store.Record(new[]
        {
            Interval(_jobs, Acme, Today, 1.5, true),
            Interval(_jobs, @"C:\Jobs\ACME", Today, 1.5, true),
        });

        var folder = OnlyFolderToday();
        Assert.Equal(Acme, folder.Folder);
        Assert.Equal(TimeSpan.FromSeconds(3), folder.Time);
        Assert.Equal(2, folder.Visits);
    }

    [Fact]
    public void TimeForAnUnknownOrDisabledRoot_IsIgnored()
    {
        var docs = AddRoot(_store, @"D:\Docs").RootId;
        _store.SetEnabled(docs, false);

        _store.Record(new[]
        {
            Interval("deleted-meanwhile", Acme, Today, 10, true),
            Interval(docs, @"D:\Docs\Letters", Today, 10, true),
        });

        Assert.False(_store.HasUnsavedChanges);
        Assert.Empty(_store.QueryPeriod(docs, Today, Today)!.Folders);
    }

    [Fact]
    public void ADaysTotal_IsTheSumOfItsFolders()
    {
        _store.Record(new[]
        {
            Interval(_jobs, Acme, Today, 1.5, true),
            Interval(_jobs, Beta, Today, 2.25, true),
            Interval(_jobs, Acme, Today, 1.5),
            Interval(_jobs, Acme, Today.AddDays(-1), 60, true),
        });

        var totals = _store.QueryDayTotals(_jobs)!;

        Assert.Equal(new ActivityDayTotal(TimeSpan.FromSeconds(5.25), 2, 2), totals[Today]);
        Assert.Equal(new ActivityDayTotal(TimeSpan.FromSeconds(60), 1, 1), totals[Today.AddDays(-1)]);
        Assert.Equal(2, totals.Count);
    }

    [Fact]
    public void APeriod_SumsOnlyItsOwnDays_MostTimeFirst()
    {
        _store.Record(new[]
        {
            Interval(_jobs, Acme, Today.AddDays(-2), 100, true),
            Interval(_jobs, Acme, Today.AddDays(-1), 10, true),
            Interval(_jobs, Beta, Today.AddDays(-1), 30, true),
            Interval(_jobs, Acme, Today, 5, true),
        });

        var period = _store.QueryPeriod(_jobs, Today.AddDays(-1), Today)!;

        Assert.Equal(Today.AddDays(-1), period.From);
        Assert.Equal(Today, period.To);
        Assert.Equal(new[] { Beta, Acme }, period.Folders.Select(f => f.Folder));
        Assert.Equal(new[] { 30.0, 15.0 }, period.Folders.Select(f => f.Time.TotalSeconds));
        Assert.Equal(new[] { 1, 2 }, period.Folders.Select(f => f.Visits));
    }

    [Fact]
    public void AnUnknownRoot_HasNoPeriodAndNoTotals()
    {
        Assert.Null(_store.QueryPeriod("nope", Today, Today));
        Assert.Null(_store.QueryDayTotals("nope"));
    }

    [Fact]
    public void APeriod_SaysWhenTrackingStartedAndHowFarBackDetailIsKept()
    {
        var week = _store.QueryPeriod(_jobs, Today.AddDays(-6), Today)!;

        Assert.Equal(Today, week.TrackingStartedOn);
        Assert.Equal(Today.AddDays(-61), week.DetailKeptFrom);
        Assert.True(week.StartsBeforeTracking);
        Assert.False(week.DetailExpired);
        Assert.False(_store.QueryPeriod(_jobs, Today, Today)!.StartsBeforeTracking);
    }

    [Fact]
    public void TrackingStartedOn_IsTheLocalDateInTheInjectedZone()
    {
        // 15:00 UTC on the 25th is 01:00 on the 26th at UTC+10.
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 9, 25, 15, 0, 0, TimeSpan.Zero), TestZones.PlusTen);
        var store = new ActivityStore(new FakePlacesStorage(), time);
        var root = AddRoot(store);

        Assert.Equal(new DateOnly(2026, 9, 26), store.QueryPeriod(root.RootId, Today, Today)!.TrackingStartedOn);
    }

    [Fact]
    public void AFailedFlush_KeepsTheData_AndTheNextFlushWritesIt()
    {
        _store.Record(new[] { Interval(_jobs, Acme, Today, 30, true) });
        _storage.FailNextWrite = true;

        var failed = _store.Flush();

        Assert.False(failed.Saved);
        Assert.True(_store.HasUnsavedChanges);
        _store.Record(new[] { Interval(_jobs, Acme, Today, 15) });

        Assert.True(_store.Flush().Saved);
        var reloaded = new ActivityStore(_storage, _time);
        Assert.Equal(TimeSpan.FromSeconds(45), Assert.Single(reloaded.QueryPeriod(_jobs, Today, Today)!.Folders).Time);
    }

    [Fact]
    public void TimeIsWrittenAsSeconds_AndReadBackToTheMillisecond()
    {
        _store.Record(new[]
        {
            Interval(_jobs, Acme, Today, 4.5, true),
            Interval(_jobs, Beta, Today, 0.001, true),
        });
        _store.Flush();

        Assert.Contains(@"""s"": 4.5,", _storage.LastWritten);
        Assert.Contains(@"""s"": 0.001,", _storage.LastWritten);
        var reloaded = new ActivityStore(_storage, _time).QueryPeriod(_jobs, Today, Today)!;
        Assert.Equal(new[] { 4500.0, 1.0 }, reloaded.Folders.Select(f => f.Time.TotalMilliseconds));
    }
}
