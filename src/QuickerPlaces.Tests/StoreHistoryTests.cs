using System;
using System.Linq;
using QuickerPlaces.Services.Activity;
using QuickerPlaces.Services.Documents;
using QuickerPlaces.Services.History;
using QuickerPlaces.Services.RecentFiles;
using QuickerPlaces.Tests.Fakes;
using Xunit;
using static QuickerPlaces.Tests.Fakes.ActivityFixtures;

namespace QuickerPlaces.Tests;

/// <summary>
/// History plan §4: activity.json and recent-files.json hand every day they
/// hold to the history before they prune any, keep their data when that
/// fails, and never delete history: stopping tracking or clearing Recent
/// Files leaves past months as they are.
/// </summary>
public sealed class StoreHistoryTests
{
    private static readonly DateOnly ExpiredDetail = Today.AddDays(-62);
    private static readonly DateOnly ExpiredTotal = Today.AddDays(-365);

    private readonly FakeHistoryFolder _folder = new();
    private readonly FakePlacesStorage _activityFile = new() { StoreFilePath = @"C:\fake\activity.json" };

    private ActivityHistory History(ManualTimeProvider? time = null) => new(_folder, time ?? new ManualTimeProvider(), "DESK-1");

    /// <summary>activity.json written a year ago, when nothing had expired, with a minute in Acme on each day.</summary>
    private string SeedActivity(params DateOnly[] days)
    {
        var past = new ActivityStore(_activityFile, new ManualTimeProvider(new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero)));
        var rootId = AddRoot(past).RootId;
        past.Record(days.Select(day => Interval(rootId, Acme, day, 60, true)));
        Assert.True(past.Flush().Saved);
        return rootId;
    }

    [Fact]
    public void ActivityStore_AtLoad_SavesEveryDayToHistory_BeforePruning()
    {
        var rootId = SeedActivity(ExpiredTotal, ExpiredDetail, Today);

        var store = new ActivityStore(_activityFile, new ManualTimeProvider(), History());

        Assert.Empty(store.QueryPeriod(rootId, ExpiredDetail, ExpiredDetail)!.Folders);
        var month = History().ReadMonth(ExpiredDetail.Year, ExpiredDetail.Month)!.Roots.Single();
        Assert.Equal(60_000, month.Days[ExpiredDetail].Folders[Acme].Milliseconds);
        var year = History().ReadMonth(ExpiredTotal.Year, ExpiredTotal.Month)!.Roots.Single();
        Assert.Equal(60_000, year.Totals[ExpiredTotal].Milliseconds);
        Assert.Equal(60_000, History().ReadMonth(Today.Year, Today.Month)!.Roots.Single().Days[Today].Folders[Acme].Milliseconds);
    }

    [Fact]
    public void ActivityStore_WhenHistoryCantBeSaved_PrunesNothing()
    {
        var rootId = SeedActivity(ExpiredDetail, Today);
        _folder.FailWrites = true;

        var store = new ActivityStore(_activityFile, new ManualTimeProvider(), History());

        Assert.Single(store.QueryPeriod(rootId, ExpiredDetail, ExpiredDetail)!.Folders);
    }

    [Fact]
    public void ActivityStore_OnANewDay_SavesTheDaysItHolds()
    {
        var time = new ManualTimeProvider();
        var store = new ActivityStore(_activityFile, time, History(time));
        var rootId = AddRoot(store).RootId;
        store.Record(new[] { Interval(rootId, Acme, Today, 60, true) });
        Assert.True(store.Flush().Saved);
        Assert.Empty(_folder.Files);

        time.UtcNow = time.UtcNow.AddDays(1);
        Assert.True(store.Flush().Saved);

        Assert.Equal(60_000, History().ReadMonth(Today.Year, Today.Month)!.Roots.Single().Days[Today].Folders[Acme].Milliseconds);
    }

    [Fact]
    public void ActivityStore_StoppingTracking_KeepsTheHistory()
    {
        var rootId = SeedActivity(ExpiredDetail, Today);
        var store = new ActivityStore(_activityFile, new ManualTimeProvider(), History());

        Assert.True(store.SetEnabled(rootId, false).Saved);

        Assert.Single(History().ReadMonth(ExpiredDetail.Year, ExpiredDetail.Month)!.Roots);
        Assert.Single(History().ReadMonth(Today.Year, Today.Month)!.Roots);
    }

    [Fact]
    public void ActivityStore_WithoutHistory_BehavesAsBefore()
    {
        var rootId = SeedActivity(ExpiredDetail, Today);

        var store = new ActivityStore(_activityFile, new ManualTimeProvider());

        Assert.Empty(store.QueryPeriod(rootId, ExpiredDetail, ExpiredDetail)!.Folders);
        Assert.Empty(_folder.Files);
    }

    // ---------------------------------------------------------------
    // Recent Files
    // ---------------------------------------------------------------

    private const string A101 = @"C:\Jobs\Tower B\A-101.pdf";
    private const string Report = @"C:\Jobs\Tower B\Report.docx";

    private readonly FakePlacesStorage _filesFile = new() { StoreFilePath = @"C:\fake\recent-files.json" };

    /// <summary>recent-files.json as a year-old copy left it: tracking on, and an open of A-101 at each instant.</summary>
    private void SeedFiles(params DateTimeOffset[] opens)
    {
        var started = opens.Min().AddDays(-1).ToString("O");
        var list = string.Join(",", opens.OrderBy(o => o).Select(o => $"\"{o:O}\""));
        _filesFile.ContentsToReturn = $$"""
            {"schemaVersion":1,"settings":{"enabled":true,"trackingStartedAt":"{{started}}","resumedAt":"{{started}}"},
             "files":[{"path":"C:\\Jobs\\Tower B\\A-101.pdf","opens":[{{list}}]}]}
            """;
    }

    [Fact]
    public void RecentFilesStore_AtLoad_SavesEveryOpenToHistory_BeforePruning()
    {
        var time = new ManualTimeProvider();
        var expired = time.UtcNow.AddDays(-400);
        SeedFiles(expired, time.UtcNow.AddDays(-3));

        var store = new RecentFilesStore(_filesFile, time, History(time));

        Assert.Equal(1, store.QueryFiles().Single().Opens);
        var history = History(time);
        var expiredDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(expired, time.LocalTimeZone).DateTime);
        Assert.Equal(new[] { expired }, history.ReadMonth(expiredDate.Year, expiredDate.Month)!.Files.Single().Opens);
    }

    [Fact]
    public void RecentFilesStore_WhenHistoryCantBeSaved_PrunesNothing()
    {
        var time = new ManualTimeProvider();
        SeedFiles(time.UtcNow.AddDays(-400), time.UtcNow.AddDays(-3));
        _folder.FailWrites = true;

        var store = new RecentFilesStore(_filesFile, time, History(time));

        Assert.Equal(2, store.QueryFiles().Single().Opens);
    }

    [Fact]
    public void RecentFilesStore_ForgetAndClear_KeepTheHistory()
    {
        var time = new ManualTimeProvider();
        var history = History(time);
        var store = new RecentFilesStore(_filesFile, time, history);
        Assert.True(store.SetEnabled(true).Saved);
        store.Record(new[] { new RecentDocument(A101, time.UtcNow.AddMinutes(1)), new RecentDocument(Report, time.UtcNow.AddMinutes(2)) }, _ => true);
        time.UtcNow = time.UtcNow.AddDays(1);
        Assert.True(store.Flush().Saved);
        Assert.Equal(2, history.ReadMonth(Today.Year, Today.Month)!.Files.Count);

        Assert.True(store.Forget(A101).Saved);
        Assert.True(store.ClearHistory().Saved);
        time.UtcNow = time.UtcNow.AddDays(1);
        Assert.True(store.Flush().Saved);

        Assert.Empty(store.QueryFiles());
        Assert.Equal(2, History(time).ReadMonth(Today.Year, Today.Month)!.Files.Count);
    }
}
