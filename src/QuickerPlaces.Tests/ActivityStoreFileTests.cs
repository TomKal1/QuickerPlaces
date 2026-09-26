using System;
using System.IO;
using System.Linq;
using QuickerPlaces.Services;
using QuickerPlaces.Services.Activity;
using QuickerPlaces.Tests.Fakes;
using Xunit;
using static QuickerPlaces.Tests.Fakes.ActivityFixtures;

namespace QuickerPlaces.Tests;

/// <summary>
/// The activity store over a real activity.json in a temporary folder
/// (D32): unicode round-trips, and a damaged file is set aside beside
/// itself without touching places.json.
/// </summary>
public sealed class ActivityStoreFileTests
{
    [Fact]
    public void UnicodePaths_ADayWithNoFolders_AndARootWithNoDays_RoundTrip()
    {
        using var dir = new TempDirectory();
        var time = new ManualTimeProvider();
        var storage = new FilePlacesStorage(dir.Path, "activity.json");
        var first = new ActivityStore(storage, time);
        var jobs = AddRoot(first, @"C:\Jobs – Ärchiv");
        var empty = AddRoot(first, @"\\サーバー\共有");
        const string folder = @"C:\Jobs – Ärchiv\Café 日本 🗂";
        first.Record(new[] { Interval(jobs.RootId, folder, Today, 61.25, true) });
        Assert.True(first.Flush().Saved);

        var second = new ActivityStore(new FilePlacesStorage(dir.Path, "activity.json"), time);

        Assert.Equal(new[] { @"C:\Jobs – Ärchiv", @"\\サーバー\共有" }, second.Roots.Select(r => r.Path));
        var row = Assert.Single(second.QueryPeriod(jobs.RootId, Today, Today)!.Folders);
        Assert.Equal(folder, row.Folder);
        Assert.Equal(TimeSpan.FromSeconds(61.25), row.Time);
        Assert.Empty(second.QueryPeriod(empty.RootId, Today.AddDays(-30), Today)!.Folders);
        Assert.Empty(second.QueryDayTotals(empty.RootId)!);
    }

    [Fact]
    public void ADayWithNoFolders_Loads()
    {
        using var dir = new TempDirectory();
        File.WriteAllText(dir.File("activity.json"),
            @"{""schemaVersion"":1,""roots"":[{""rootId"":""a"",""path"":""C:\\Jobs"",""days"":{""2026-09-25"":{""folders"":{}}}}]}");

        var store = new ActivityStore(new FilePlacesStorage(dir.Path, "activity.json"), new ManualTimeProvider());

        Assert.Equal(Models.StoreLoadOutcome.Ok, store.LoadOutcome);
        Assert.Empty(store.QueryPeriod("a", Today, Today)!.Folders);
    }

    [Fact]
    public void ADamagedFile_IsSetAsideBesideItself_NamedFromTheInjectedLocalTime_AndPlacesJsonIsUntouched()
    {
        using var dir = new TempDirectory();
        File.WriteAllText(dir.File("places.json"), "the user's places");
        File.WriteAllText(dir.File("activity.json"), "{ damaged");
        // 05:06:07 UTC is 15:06:07 at UTC+10.
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero), TestZones.PlusTen);

        var store = new ActivityStore(new FilePlacesStorage(dir.Path, "activity.json"), time);

        var kept = dir.File("activity.corrupt-20260304-150607.json");
        Assert.Equal("{ damaged", File.ReadAllText(kept));
        Assert.False(File.Exists(dir.File("activity.json")));
        Assert.Contains(kept, store.Notice);
        Assert.Equal("the user's places", File.ReadAllText(dir.File("places.json")));
        Assert.Equal(
            new[] { "activity.corrupt-20260304-150607.json", "places.json" },
            Directory.GetFiles(dir.Path).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal));
    }
}
