using System;
using System.IO;
using System.Linq;
using QuickerPlaces.Models;
using QuickerPlaces.Services.Activity;
using QuickerPlaces.Tests.Fakes;
using Xunit;
using static QuickerPlaces.Tests.Fakes.ActivityFixtures;

namespace QuickerPlaces.Tests;

/// <summary>
/// Loading activity.json (Phase 9 plan §7 store tests, D18, D32, D33): a
/// damaged file is quarantined and tracking restarts empty, with one line
/// saying so; a file that cannot be opened, or that a newer version wrote,
/// is never touched, and the store writes nothing for the session. No
/// outcome throws, blocks startup, or asks the user anything.
/// </summary>
public sealed class ActivityStoreLoadTests
{
    [Fact]
    public void NoFile_StartsEmptyAndAvailable_WithoutWriting()
    {
        var storage = new FakePlacesStorage();

        var store = new ActivityStore(storage, new ManualTimeProvider());

        Assert.Equal(StoreLoadOutcome.NotPresent, store.LoadOutcome);
        Assert.True(store.IsAvailable);
        Assert.Null(store.Notice);
        Assert.Empty(store.Roots);
        Assert.Equal(0, storage.WriteCount);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData(@"{""schemaVersion"":""1"",""roots"":[]}")]
    [InlineData(@"{""schemaVersion"":0,""roots"":[]}")]
    [InlineData(@"{""schemaVersion"":1,""roots"":null}")]
    [InlineData(@"{""schemaVersion"":1,""schemaVersion"":1,""roots"":[]}")]
    [InlineData(@"{""schemaVersion"":1,""roots"":[{""rootId"":""a"",""path"":""C:\\Jobs"",""days"":{""not-a-date"":{""folders"":{}}}}]}")]
    [InlineData(@"{""schemaVersion"":1,""roots"":[{""rootId"":""a"",""path"":""C:\\Jobs"",""days"":{""2026-09-25"":{""folders"":{""C:\\Jobs\\A"":{""s"":""x""}}}}}]}")]
    public void ADamagedFile_IsQuarantined_AndTrackingRestartsEmpty(string contents)
    {
        var storage = new FakePlacesStorage { ContentsToReturn = contents };

        var store = new ActivityStore(storage, new ManualTimeProvider());

        Assert.Equal(StoreLoadOutcome.Damaged, store.LoadOutcome);
        Assert.Equal(1, storage.QuarantineCount);
        Assert.True(store.IsAvailable);
        Assert.Empty(store.Roots);
        Assert.Contains(storage.QuarantinedPath!, store.Notice);
        Assert.Contains("reset", store.Notice);
        Assert.Equal(0, storage.WriteCount);
    }

    [Fact]
    public void AfterAQuarantine_TheFirstChangeWritesAFreshFile()
    {
        var storage = new FakePlacesStorage { ContentsToReturn = "{ not json" };
        var store = new ActivityStore(storage, new ManualTimeProvider());

        AddRoot(store);

        Assert.Equal(1, storage.WriteCount);
        Assert.Single(new ActivityStore(storage, new ManualTimeProvider()).Roots);
    }

    [Theory]
    [InlineData("io")]
    [InlineData("access")]
    [InlineData("unexpected")]
    public void AFileThatCannotBeOpened_IsNeverTouched_AndNothingIsWritten(string failure)
    {
        var storage = new FakePlacesStorage
        {
            ContentsToReturn = "{}",
            ReadThrows = failure switch
            {
                "io" => new IOException("Locked."),
                "access" => new UnauthorizedAccessException("Denied."),
                _ => new InvalidOperationException("Something else."),
            }
        };

        var store = new ActivityStore(storage, new ManualTimeProvider());

        Assert.Equal(StoreLoadOutcome.Unreadable, store.LoadOutcome);
        Assert.Equal(0, storage.QuarantineCount);
        AssertUnavailable(store, storage, "couldn't be opened");
    }

    [Fact]
    public void AFileFromANewerVersion_IsNeverTouched_AndNothingIsWritten()
    {
        var storage = new FakePlacesStorage { ContentsToReturn = @"{""schemaVersion"":3,""roots"":[]}" };

        var store = new ActivityStore(storage, new ManualTimeProvider());

        Assert.Equal(StoreLoadOutcome.WrittenByNewerVersion, store.LoadOutcome);
        Assert.Equal(0, storage.QuarantineCount);
        AssertUnavailable(store, storage, "newer version");
    }

    [Fact]
    public void ADamagedFileThatCannotBeSetAside_IsNeverOverwritten()
    {
        var storage = new FakePlacesStorage { ContentsToReturn = "{ not json", QuarantineThrows = new IOException("Locked.") };

        var store = new ActivityStore(storage, new ManualTimeProvider());

        Assert.Equal(StoreLoadOutcome.Damaged, store.LoadOutcome);
        AssertUnavailable(store, storage, "couldn't be set aside");
    }

    /// <summary>Tracking is off for the session: no roots to track, every change refused with the notice, and nothing ever written.</summary>
    private static void AssertUnavailable(ActivityStore store, FakePlacesStorage storage, string noticeSays)
    {
        Assert.False(store.IsAvailable);
        Assert.Contains(noticeSays, store.Notice);
        Assert.Contains("tracking is off", store.Notice);
        Assert.Empty(store.EnabledRoots());

        var added = store.TryAddRoot(@"C:\Jobs", null, out var root, out _);
        Assert.False(added.Success);
        Assert.Null(root);
        Assert.Equal(store.Notice, added.ErrorMessage);

        store.Record(new[] { Interval("any", Acme, Today, 10, true) });
        Assert.True(store.Flush().Saved);
        Assert.Equal(0, storage.WriteCount);
    }

    [Fact]
    public void ASavedDocument_LoadsBackWithItsRootsAndData()
    {
        var storage = new FakePlacesStorage();
        var time = new ManualTimeProvider();
        var first = new ActivityStore(storage, time);
        var jobs = AddRoot(first);
        first.Record(new[] { Interval(jobs.RootId, Acme, Today, 90, true) });
        Assert.True(first.Flush().Saved);

        var second = new ActivityStore(storage, time);

        Assert.Equal(StoreLoadOutcome.Ok, second.LoadOutcome);
        Assert.Null(second.Notice);
        var root = Assert.Single(second.Roots);
        Assert.Equal(jobs.RootId, root.RootId);
        Assert.Equal(@"C:\Jobs", root.Path);
        Assert.Equal(jobs.TrackingStartedAt, root.TrackingStartedAt);
        var folder = Assert.Single(second.QueryPeriod(jobs.RootId, Today, Today)!.Folders);
        Assert.Equal(new FolderActivity(Acme, TimeSpan.FromSeconds(90), 1, Interval("", "", Today, 0).LastSeenAt), folder);
    }

    [Fact]
    public void FolderKeysThatDifferOnlyInCase_AreMergedOnLoad()
    {
        var storage = new FakePlacesStorage
        {
            ContentsToReturn = @"{""schemaVersion"":1,""roots"":[{""rootId"":""a"",""path"":""C:\\Jobs"",""days"":{""2026-09-25"":{""folders"":{
                ""C:\\Jobs\\Acme"":{""s"":1,""v"":1,""last"":""2026-09-25T01:00:00+00:00""},
                ""C:\\Jobs\\ACME"":{""s"":2.5,""v"":1,""last"":""2026-09-25T02:00:00+00:00""}}}}}]}"
        };

        var store = new ActivityStore(storage, new ManualTimeProvider());

        var folder = Assert.Single(store.QueryPeriod("a", Today, Today)!.Folders);
        Assert.Equal(TimeSpan.FromSeconds(3.5), folder.Time);
        Assert.Equal(2, folder.Visits);
        Assert.Equal(new DateTimeOffset(2026, 9, 25, 2, 0, 0, TimeSpan.Zero), folder.LastVisited);

        // Time recorded after the load, cased a third way, joins the same
        // folder: the day still has one folder, and the file one key.
        store.Record(new[] { Interval("a", @"C:\Jobs\acme", Today, 1) });
        Assert.Equal(1, store.QueryDayTotals("a")![Today].Folders);
        store.Flush();
        Assert.Single(new ActivityStore(storage, new ManualTimeProvider()).QueryPeriod("a", Today, Today)!.Folders);
        Assert.Equal(1, storage.LastWritten!.Split(@"Jobs\\").Length - 1);   // one folder key under C:\\Jobs
    }

    [Fact]
    public void MissingPiecesAndNullEntries_LoadAsEmpty()
    {
        var storage = new FakePlacesStorage
        {
            ContentsToReturn = @"{""schemaVersion"":1,""roots"":[null,
                {""rootId"":""a"",""path"":""C:\\Jobs"",""equivalentPrefixes"":null,""days"":null,""dayTotals"":null},
                {""rootId"":""b"",""path"":""D:\\Docs"",""days"":{""2026-09-25"":{""folders"":null},""2026-09-24"":null}}]}"
        };

        var store = new ActivityStore(storage, new ManualTimeProvider());

        Assert.Equal(StoreLoadOutcome.Ok, store.LoadOutcome);
        Assert.Equal(new[] { "a", "b" }, store.Roots.Select(r => r.RootId));
        Assert.Empty(store.Roots[0].Config.EquivalentPrefixes);
        Assert.Empty(store.QueryPeriod("a", Today.AddDays(-7), Today)!.Folders);
        Assert.Empty(store.QueryPeriod("b", Today.AddDays(-7), Today)!.Folders);
    }

    [Fact]
    public void AMissingOrRepeatedRootId_IsReplaced()
    {
        var storage = new FakePlacesStorage
        {
            ContentsToReturn = @"{""schemaVersion"":1,""roots"":[
                {""rootId"":""a"",""path"":""C:\\Jobs""},
                {""rootId"":""a"",""path"":""D:\\Docs""},
                {""path"":""E:\\Music""}]}"
        };

        var store = new ActivityStore(storage, new ManualTimeProvider());

        var ids = store.Roots.Select(r => r.RootId).ToList();
        Assert.Equal("a", ids[0]);
        Assert.Equal(3, ids.Distinct().Count());
        Assert.All(ids, id => Assert.False(string.IsNullOrEmpty(id)));
    }
}
