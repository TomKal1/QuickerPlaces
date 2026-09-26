using System;
using System.IO;
using System.Linq;
using QuickerPlaces.Models;
using QuickerPlaces.Services;
using QuickerPlaces.Tests.Fakes;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>
/// Tests 41 to 45 from the Phase 2 plan's section 7: places in Recently
/// Deleted are purged seven full days after removal, at exactly two points
/// — after a successful load, in memory only, and before a save — and never
/// from a store that failed to load (D13, D14, roadmap §4.10). Test 46, the
/// purge's log line, is in DiagnosticLogTests' collection; test 40, the
/// policy's arithmetic, is RecentlyDeletedPolicyTests.
///
/// Every service here gets a ManualTimeProvider, so no assertion depends
/// on the clock or zone of the machine running it.
/// </summary>
public sealed class PlacesServicePurgeTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    private static ManualTimeProvider Clock() => new(Now);

    /// <summary>JSON string content for a path, with backslashes escaped.</summary>
    private static string Json(string path) => path.Replace("\\", "\\\\");

    /// <summary>A deleted record, removed at <paramref name="deletedAt"/>, as it appears in a v2 store.</summary>
    private static string DeletedRecord(string alias, DateTimeOffset deletedAt)
        => $$"""{ "alias": "{{alias}}", "type": "url", "resource": "https://{{alias.ToLowerInvariant()}}.example.com", "dateAdded": "2026-01-01T00:00:00+00:00", "deletedAt": "{{deletedAt.ToString("O")}}" }""";

    // -----------------------------------------------------------------
    // Test 44 — written before any purge code existed (plan section 10,
    // step 6): what keeps a purge from ever reaching a file that did not
    // load.
    // -----------------------------------------------------------------

    /// <summary>
    /// Test 44, Damaged: a v2 store holding a long-expired record that
    /// cannot be bound (a dateAdded that is not a date) is left
    /// byte-identical, with no other file written beside it — even after
    /// the clock moves on and a save is attempted (§4.10, D3, D14).
    /// </summary>
    [Fact]
    public void NoPurge_FromADamagedStore()
    {
        using var dir = new TempDirectory();
        var path = dir.File("places.json");
        File.WriteAllText(path, $$"""
            { "schemaVersion": 2, "places": [
                {{DeletedRecord("Expired", Now.AddDays(-30))}},
                { "alias": "Broken", "type": "folder", "resource": "{{Json(TestPaths.Folder("Broken"))}}", "dateAdded": 5 }
            ] }
            """);

        AssertNoPurgeReachesTheFile(dir, path, StoreLoadOutcome.Damaged, holdOpen: false);
    }

    /// <summary>Test 44, Unreadable: a store with an expired record, held open with FileShare.None while loading, is left byte-identical.</summary>
    [Fact]
    public void NoPurge_FromAnUnreadableStore()
    {
        using var dir = new TempDirectory();
        var path = dir.File("places.json");
        File.WriteAllText(path, $$"""{ "schemaVersion": 2, "places": [ {{DeletedRecord("Expired", Now.AddDays(-30))}} ] }""");

        AssertNoPurgeReachesTheFile(dir, path, StoreLoadOutcome.Unreadable, holdOpen: true);
    }

    /// <summary>Test 44, WrittenByNewerVersion: a version 4 store with an expired record is left byte-identical.</summary>
    [Fact]
    public void NoPurge_FromAStoreWrittenByANewerVersion()
    {
        using var dir = new TempDirectory();
        var path = dir.File("places.json");
        File.WriteAllText(path, $$"""{ "schemaVersion": 4, "places": [ {{DeletedRecord("Expired", Now.AddDays(-30))}} ] }""");

        AssertNoPurgeReachesTheFile(dir, path, StoreLoadOutcome.WrittenByNewerVersion, holdOpen: false);
    }

    private static void AssertNoPurgeReachesTheFile(TempDirectory dir, string path, StoreLoadOutcome expected, bool holdOpen)
    {
        var originalBytes = File.ReadAllBytes(path);
        var clock = Clock();

        PlacesService service;
        using (holdOpen ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None) : null)
        {
            service = new PlacesService(new FilePlacesStorage(dir.Path, "places.json"), clock);
        }

        Assert.Equal(expected, service.LoadOutcome);
        Assert.True(service.IsRecoveryUnresolved);
        Assert.Empty(service.RecentlyDeleted);

        clock.Advance(TimeSpan.FromDays(30));
        Assert.False(service.RetrySave().Saved);
        Assert.False(service.TryAdd("New", PlaceType.Url, "https://new.example.com", out _, out _).Success);

        Assert.Equal(originalBytes, File.ReadAllBytes(path));
        Assert.Single(Directory.GetFiles(dir.Path));
    }

    // -----------------------------------------------------------------
    // The two purge points (tests 41-43, 45)
    // -----------------------------------------------------------------

    /// <summary>A v2 store with one active place and the given deleted records.</summary>
    private static FakePlacesStorage SeededStorage(params string[] deletedRecords) => new()
    {
        ContentsToReturn = $$"""
            { "schemaVersion": 2, "places": [
                { "alias": "Active", "type": "folder", "resource": "{{Json(TestPaths.Folder("Active"))}}", "dateAdded": "2026-01-01T00:00:00+00:00" }{{string.Concat(deletedRecords.Select(r => ",\n" + r))}}
            ] }
            """
    };

    /// <summary>
    /// Test 41: the purge after a successful load. A record deleted exactly
    /// seven days (168 hours) before now is gone; one deleted a second later
    /// remains. In memory only: nothing is written and the stored text is
    /// unchanged — loading never writes (D14 point 1).
    /// </summary>
    [Fact]
    public void StartupPurge_RemovesExactlyTheExpired_AndWritesNothing()
    {
        var storage = SeededStorage(
            DeletedRecord("Expired", Now - TimeSpan.FromDays(7)),
            DeletedRecord("Kept", Now - TimeSpan.FromDays(7) + TimeSpan.FromSeconds(1)));
        var original = storage.ContentsToReturn;

        var service = new PlacesService(storage, Clock());

        Assert.Equal(StoreLoadOutcome.Ok, service.LoadOutcome);
        Assert.Equal("Kept", Assert.Single(service.RecentlyDeleted).Alias);
        Assert.Equal("Active", Assert.Single(service.Places).Alias);
        Assert.Equal(0, storage.WriteCount);
        Assert.Equal(original, storage.ContentsToReturn);
        Assert.False(service.HasUnsavedChanges);
    }

    /// <summary>Test 42: the purge before a save. Once the clock passes expiry, the next save — here a TryAdd — writes the store without the expired record (D14 point 2).</summary>
    [Fact]
    public void PurgeBeforeSave_LeavesTheExpiredRecordOutOfTheWrite()
    {
        var storage = SeededStorage(DeletedRecord("Expiring", Now - TimeSpan.FromDays(6)));
        var clock = Clock();
        var service = new PlacesService(storage, clock);
        Assert.Single(service.RecentlyDeleted);

        clock.Advance(TimeSpan.FromDays(1));
        Assert.True(service.TryAdd("New", PlaceType.Url, "https://new.example.com", out _, out var persistence).Success);

        Assert.True(persistence.Saved);
        Assert.Empty(service.RecentlyDeleted);
        Assert.DoesNotContain("Expiring", storage.LastWritten);
        Assert.Contains("\"New\"", storage.LastWritten);
    }

    /// <summary>
    /// Test 43: RetrySave purges too. The add's save fails before the record
    /// expires; by the time Retry runs it has, and the retried write leaves
    /// it out — Retry is a whole-store save like any other (D2, D14).
    /// </summary>
    [Fact]
    public void RetrySave_PurgesToo()
    {
        var storage = SeededStorage(DeletedRecord("Expiring", Now - TimeSpan.FromDays(6)));
        var clock = Clock();
        var service = new PlacesService(storage, clock);
        storage.FailNextWrite = true;
        service.TryAdd("New", PlaceType.Url, "https://new.example.com", out _, out var failed);
        Assert.False(failed.Saved);
        Assert.Single(service.RecentlyDeleted);

        clock.Advance(TimeSpan.FromDays(2));
        var retry = service.RetrySave();

        Assert.True(retry.Saved);
        Assert.False(service.HasUnsavedChanges);
        Assert.Empty(service.RecentlyDeleted);
        Assert.DoesNotContain("Expiring", storage.LastWritten);
        Assert.Contains("\"New\"", storage.LastWritten);
    }

    /// <summary>
    /// Test 45: a successful Reload() purges like a startup — the store
    /// could not be read at launch, so nothing was purged then; once it
    /// can, the expired record goes, in memory only.
    /// </summary>
    [Fact]
    public void SuccessfulReload_PurgesLikeAStartup()
    {
        var storage = SeededStorage(
            DeletedRecord("Expired", Now - TimeSpan.FromDays(8)),
            DeletedRecord("Kept", Now - TimeSpan.FromDays(1)));
        storage.ReadThrows = new IOException("Locked by another process.");
        var service = new PlacesService(storage, Clock());
        Assert.Equal(StoreLoadOutcome.Unreadable, service.LoadOutcome);

        storage.ReadThrows = null;
        var outcome = service.Reload();

        Assert.Equal(StoreLoadOutcome.Ok, outcome);
        Assert.Equal("Kept", Assert.Single(service.RecentlyDeleted).Alias);
        Assert.Equal(0, storage.WriteCount);
    }

    /// <summary>
    /// Not a numbered plan test: a place past expiry but not yet purged
    /// ("Expiring", D13) is still restorable, and restoring it takes it out
    /// of the purge — the save that follows keeps it, active.
    /// </summary>
    [Fact]
    public void AnExpiredPlaceNotYetPurged_CanStillBeRestored()
    {
        var storage = SeededStorage(DeletedRecord("Expiring", Now - TimeSpan.FromDays(6)));
        var clock = Clock();
        var service = new PlacesService(storage, clock);
        var expiring = Assert.Single(service.RecentlyDeleted);

        clock.Advance(TimeSpan.FromDays(2));
        var result = service.TryRestore(expiring, out var persistence);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.True(persistence.Saved);
        Assert.Contains(expiring, service.Places);
        Assert.Contains("\"Expiring\"", storage.LastWritten);
    }
}
