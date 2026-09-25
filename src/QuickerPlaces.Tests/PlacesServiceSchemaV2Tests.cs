using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using QuickerPlaces.Models;
using QuickerPlaces.Services;
using QuickerPlaces.Tests.Fakes;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>
/// Tests 13 to 20 and 22 from the Phase 2 plan's section 7: schema v2 as
/// PlacesService loads, gates, migrates and writes it (5.1), and the
/// active/deleted split on the read side (5.3 rows 3, 4, 6, 7, 8, 11).
/// Test 21, the migration's log line, is in DiagnosticLogTests' collection.
///
/// Every service here gets a ManualTimeProvider, so no assertion depends
/// on the clock or zone of the machine running it.
/// </summary>
public sealed class PlacesServiceSchemaV2Tests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 0, 0, 0, TimeSpan.Zero);

    /// <summary>A v1 store in places.v1.json's exact shape: offset-less dates, as the fixture and hand-edits have them.</summary>
    private const string V1Store = """
        { "schemaVersion": 1, "places": [
            { "alias": "Downloads", "type": "folder", "resource": "C:\\Users\\Test\\Downloads", "isFavourite": true, "favouriteOrder": 0, "dateAdded": "2026-01-15T09:30:00" },
            { "alias": "Wiki", "type": "url", "resource": "https://wiki.example.com", "isFavourite": false, "favouriteOrder": null, "dateAdded": "2026-02-03T14:05:22+10:00" }
        ] }
        """;

    private static ManualTimeProvider Clock(TimeZoneInfo? zone = null) => new(Now, zone ?? TestZones.PlusTen);

    /// <summary>JSON string content for a path, with backslashes escaped.</summary>
    private static string Json(string path) => path.Replace("\\", "\\\\");

    /// <summary>
    /// A v2 store holding an active "Wiki", an active favourite "Pinned",
    /// and a deleted "Docs" (a favourite remembered at bubble slot 0) whose
    /// alias and folder are free for active places to take.
    /// </summary>
    private static FakePlacesStorage SeededV2Storage() => new()
    {
        ContentsToReturn = $$"""
            { "schemaVersion": 2, "places": [
                { "alias": "Docs", "type": "folder", "resource": "{{Json(TestPaths.Folder("Docs"))}}", "isFavourite": true, "favouriteOrder": 0, "dateAdded": "2026-01-01T00:00:00+00:00", "deletedAt": "2026-09-24T00:00:00+00:00" },
                { "alias": "Pinned", "type": "folder", "resource": "{{Json(TestPaths.Folder("Pinned"))}}", "isFavourite": true, "favouriteOrder": 0, "dateAdded": "2026-01-01T00:00:00+00:00" },
                { "alias": "Wiki", "type": "url", "resource": "https://wiki.example.com", "isFavourite": false, "favouriteOrder": null, "dateAdded": "2026-01-01T00:00:00+00:00" }
            ] }
            """
    };

    private static PlacesService NewSeededService(out FakePlacesStorage storage, out Place deletedDocs)
    {
        storage = SeededV2Storage();
        var service = new PlacesService(storage, Clock());
        Assert.Equal(StoreLoadOutcome.Ok, service.LoadOutcome);
        deletedDocs = Assert.Single(service.RecentlyDeleted);
        return service;
    }

    // -----------------------------------------------------------------
    // Loading, the version gate, and migrating once (tests 13-17)
    // -----------------------------------------------------------------

    /// <summary>Test 13: loading a v1 store migrates it in memory only — nothing is written, and the stored text is unchanged (roadmap §4.3).</summary>
    [Fact]
    public void LoadingAV1Store_WritesNothing()
    {
        var storage = new FakePlacesStorage { ContentsToReturn = V1Store, FailEveryWrite = true };

        var service = new PlacesService(storage, Clock());

        Assert.Equal(StoreLoadOutcome.Ok, service.LoadOutcome);
        Assert.Equal(2, service.Places.Count);
        Assert.Equal(0, storage.WriteCount);
        Assert.Equal(V1Store, storage.ContentsToReturn);
        Assert.False(service.HasUnsavedChanges);
    }

    /// <summary>
    /// Test 14: the conversion happens once. Loaded under PlusTen and saved
    /// as v2, the store reloads under MinusFive with every DateAdded
    /// unchanged — a v2 document is never migrated again, so the second
    /// zone plays no part.
    /// </summary>
    [Fact]
    public void Migration_HappensOnce_AndTheNextZoneHasNoEffect()
    {
        var storage = new FakePlacesStorage { ContentsToReturn = V1Store };
        var first = new PlacesService(storage, Clock(TestZones.PlusTen));
        Assert.True(first.TryAdd("Added", PlaceType.Url, "https://added.example.com", out _, out var persistence).Success);
        Assert.True(persistence.Saved);
        var before = first.Places.Select(p => (p.Alias, p.DateAdded)).ToList();

        var second = new PlacesService(storage, Clock(TestZones.MinusFive));

        Assert.Equal(StoreLoadOutcome.Ok, second.LoadOutcome);
        Assert.Equal(before, second.Places.Select(p => (p.Alias, p.DateAdded)));
        Assert.Equal(new DateTimeOffset(2026, 1, 14, 23, 30, 0, TimeSpan.Zero), second.Places[0].DateAdded);
        Assert.Equal(2, JsonNode.Parse(storage.LastWritten!)!["schemaVersion"]!.GetValue<int>());
    }

    /// <summary>
    /// Test 15: if the first save fails, the file is still the untouched v1
    /// original, and the next launch migrates the same source values again
    /// to the same result (D11) — nothing was half-converted.
    /// </summary>
    [Fact]
    public void FailedFirstSave_LeavesV1OnDisk_AndTheNextLaunchMigratesTheSameValues()
    {
        var storage = new FakePlacesStorage { ContentsToReturn = V1Store, FailEveryWrite = true };
        var first = new PlacesService(storage, Clock());
        first.TryAdd("Added", PlaceType.Url, "https://added.example.com", out _, out var persistence);
        Assert.False(persistence.Saved);
        Assert.Equal(V1Store, storage.ContentsToReturn);

        storage.FailEveryWrite = false;
        var second = new PlacesService(storage, Clock());

        Assert.Equal(StoreLoadOutcome.Ok, second.LoadOutcome);
        Assert.Equal(
            first.Places.Where(p => p.Alias != "Added").Select(p => (p.Alias, p.DateAdded)),
            second.Places.Select(p => (p.Alias, p.DateAdded)));
    }

    /// <summary>
    /// Test 16, version 2: loads as is, without migrating. The stray-looking
    /// deletedAt proves it: the v1 migration drops that key, so a v2 load
    /// that went through it would have lost the deletion.
    /// </summary>
    [Fact]
    public void Version2_LoadsWithoutMigrating()
    {
        var service = NewSeededService(out _, out var deletedDocs);

        Assert.Equal("Docs", deletedDocs.Alias);
        Assert.Equal(new[] { "Pinned", "Wiki" }, service.Places.Select(p => p.Alias));
    }

    /// <summary>Test 16, version 3: WrittenByNewerVersion, and the file is left byte-identical.</summary>
    [Fact]
    public void Version3_IsWrittenByNewerVersion_AndTheFileIsUntouched()
    {
        using var dir = new TempDirectory();
        var path = dir.File("places.json");
        File.WriteAllText(path, """{ "schemaVersion": 3, "places": [] }""");
        var originalBytes = File.ReadAllBytes(path);

        var service = new PlacesService(new FilePlacesStorage(dir.Path, "places.json"), Clock());

        Assert.Equal(StoreLoadOutcome.WrittenByNewerVersion, service.LoadOutcome);
        Assert.True(service.IsRecoveryUnresolved);
        Assert.Equal(originalBytes, File.ReadAllBytes(path));
        Assert.Single(Directory.GetFiles(dir.Path));
    }

    /// <summary>Test 16, versions 0 and −1: Damaged — no build ever wrote them, so they are not a known migration (§4.3).</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void VersionBelow1_IsDamaged(int version)
    {
        var storage = new FakePlacesStorage { ContentsToReturn = $$"""{ "schemaVersion": {{version}}, "places": [] }""" };

        var service = new PlacesService(storage, Clock());

        Assert.Equal(StoreLoadOutcome.Damaged, service.LoadOutcome);
        Assert.True(service.IsRecoveryUnresolved);
    }

    /// <summary>
    /// Test 17: a v1 store whose date cannot be migrated is Damaged ("a
    /// store that failed to migrate", §4.10) — never written, never
    /// quarantined on the service's own initiative. Real FilePlacesStorage,
    /// so "never written" means the directory holds only the original file.
    /// </summary>
    [Fact]
    public void V1StoreWithAnUnmigratableDate_IsDamaged_NeverWrittenOrQuarantined()
    {
        using var dir = new TempDirectory();
        var path = dir.File("places.json");
        File.WriteAllText(path, """{ "schemaVersion": 1, "places": [ { "alias": "Docs", "type": "folder", "resource": "C:\\Docs", "dateAdded": 5 } ] }""");
        var originalBytes = File.ReadAllBytes(path);

        var service = new PlacesService(new FilePlacesStorage(dir.Path, "places.json"), Clock());

        Assert.Equal(StoreLoadOutcome.Damaged, service.LoadOutcome);
        Assert.True(service.IsRecoveryUnresolved);
        Assert.Empty(service.Places);
        Assert.False(service.TryAdd("New", PlaceType.Url, "https://new.example.com", out _, out _).Success);
        Assert.Equal(originalBytes, File.ReadAllBytes(path));
        Assert.Single(Directory.GetFiles(dir.Path));
    }

    /// <summary>
    /// Not a numbered plan test: a store with a duplicated property name is
    /// Damaged, not a crash. JsonObject cannot hold duplicate keys and would
    /// throw ArgumentException, which the load's catch does not classify;
    /// the parse refuses them as a JsonException instead (D6).
    /// </summary>
    [Theory]
    [InlineData("""{ "schemaVersion": 1, "schemaVersion": 1, "places": [] }""")]
    [InlineData("""{ "schemaVersion": 2, "places": [ { "alias": "A", "alias": "B", "type": "url", "resource": "https://a.example.com" } ] }""")]
    public void DuplicatePropertyNames_AreDamaged_NotACrash(string json)
    {
        var storage = new FakePlacesStorage { ContentsToReturn = json };

        var service = new PlacesService(storage, Clock());

        Assert.Equal(StoreLoadOutcome.Damaged, service.LoadOutcome);
    }

    // -----------------------------------------------------------------
    // Deleted records on the read side (tests 18-20; plan 5.3)
    // -----------------------------------------------------------------

    /// <summary>Test 18, Add: a deleted "Docs" blocks neither its alias nor its folder (§4.10, 5.3 rows 3-4).</summary>
    [Fact]
    public void DeletedRecord_DoesNotBlockAdd()
    {
        var service = NewSeededService(out _, out _);

        Assert.True(service.ValidateAlias("docs").Success);
        Assert.True(service.ValidateResource(TestPaths.Folder("Docs"), PlaceType.Folder).Success);
        var result = service.TryAdd("docs", PlaceType.Folder, TestPaths.Folder("Docs"), out var created, out _);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Contains(created, service.Places);
    }

    /// <summary>Test 18, Rename and Edit: an active place can take a deleted record's alias and folder.</summary>
    [Fact]
    public void DeletedRecord_DoesNotBlockRenameOrEdit()
    {
        var service = NewSeededService(out _, out _);
        var pinned = service.Places.Single(p => p.Alias == "Pinned");

        Assert.True(service.TryRenameAlias(pinned, "DOCS", out _).Success);
        Assert.True(service.TryEditResource(pinned, TestPaths.Folder("Docs"), out _).Success);

        Assert.Equal("DOCS", pinned.Alias);
        Assert.Equal(TestPaths.Folder("Docs"), pinned.Resource);
    }

    /// <summary>Test 18, Import: an incoming "Docs" at the deleted record's folder is offered and commits.</summary>
    [Fact]
    public void DeletedRecord_DoesNotBlockImport()
    {
        using var dir = new TempDirectory();
        var importFile = dir.File("export.json");
        File.WriteAllText(importFile, $$"""
            { "schemaVersion": 2, "places": [
                { "alias": "Docs", "type": "folder", "resource": "{{Json(TestPaths.Folder("Docs"))}}" }
            ] }
            """);
        var service = NewSeededService(out _, out _);

        var (candidates, error) = service.GetImportCandidates(importFile);
        Assert.Null(error);
        var (imported, _) = service.CommitImport(candidates);

        Assert.Equal("Docs", Assert.Single(imported).Alias);
        Assert.Contains(imported[0], service.Places);
    }

    /// <summary>
    /// Test 19: favouriting appends after the active favourites only. The
    /// deleted "Docs" remembers bubble slot 0 and "Pinned" holds it; the new
    /// favourite takes 1, not 2, and the remembered slot is untouched (D9).
    /// </summary>
    [Fact]
    public void Favouriting_AppendsAfterActiveFavouritesOnly()
    {
        var service = NewSeededService(out _, out var deletedDocs);
        var wiki = service.Places.Single(p => p.Alias == "Wiki");

        service.ToggleFavourite(wiki);

        Assert.Equal(1, wiki.FavouriteOrder);
        Assert.True(deletedDocs.IsFavourite);
        Assert.Equal(0, deletedDocs.FavouriteOrder);
    }

    /// <summary>
    /// Not a numbered plan test (5.3 rows 8 and 11): renumbering after an
    /// unfavourite, and a drag-reorder that is (wrongly) handed a deleted
    /// record, both number active favourites only and leave the deleted
    /// record's remembered slot alone (D9).
    /// </summary>
    [Fact]
    public void Renumbering_AndReordering_LeaveTheRememberedSlotAlone()
    {
        var service = NewSeededService(out _, out var deletedDocs);
        var pinned = service.Places.Single(p => p.Alias == "Pinned");
        var wiki = service.Places.Single(p => p.Alias == "Wiki");
        service.ToggleFavourite(wiki);                                  // bubbles: Pinned 0, Wiki 1

        service.SetFavouriteOrder(new List<Place> { deletedDocs, wiki, pinned });

        Assert.Equal(0, wiki.FavouriteOrder);
        Assert.Equal(1, pinned.FavouriteOrder);
        Assert.Equal(0, deletedDocs.FavouriteOrder);

        service.ToggleFavourite(wiki);                                  // RenumberFavourites: Pinned 0

        Assert.Equal(0, pinned.FavouriteOrder);
        Assert.True(deletedDocs.IsFavourite);
        Assert.Equal(0, deletedDocs.FavouriteOrder);
    }

    /// <summary>
    /// Test 20: Rename and Edit refuse a record in Recently Deleted (5.3 row
    /// 6), and toggling its favourite does nothing and writes nothing (row
    /// 7). Only a bug could reach these, but none may change a record the
    /// user cannot see.
    /// </summary>
    [Fact]
    public void DeletedRecord_RefusesRenameAndEdit_AndTogglingItWritesNothing()
    {
        var service = NewSeededService(out var storage, out var deletedDocs);

        var rename = service.TryRenameAlias(deletedDocs, "Renamed", out var renamePersistence);
        var edit = service.TryEditResource(deletedDocs, TestPaths.Folder("Elsewhere"), out var editPersistence);
        var toggle = service.ToggleFavourite(deletedDocs);

        Assert.False(rename.Success);
        Assert.Contains("Recently Deleted", rename.ErrorMessage);
        Assert.True(renamePersistence.Saved);
        Assert.False(edit.Success);
        Assert.Contains("Recently Deleted", edit.ErrorMessage);
        Assert.True(editPersistence.Saved);
        Assert.True(toggle.Saved);

        Assert.Equal("Docs", deletedDocs.Alias);
        Assert.Equal(TestPaths.Folder("Docs"), deletedDocs.Resource);
        Assert.True(deletedDocs.IsFavourite);
        Assert.Equal(0, deletedDocs.FavouriteOrder);
        Assert.Equal(0, storage.WriteCount);
        Assert.False(service.HasUnsavedChanges);
    }

    // -----------------------------------------------------------------
    // Writing v2 (test 22)
    // -----------------------------------------------------------------

    /// <summary>
    /// Test 22: TryAdd stamps DateAdded from the injected clock (D12);
    /// DateAdded and DeletedAt are written and reloaded as UTC, even when
    /// the file held another offset; and an active record has no deletedAt
    /// key at all (D16).
    /// </summary>
    [Fact]
    public void Add_StampsTheClock_DatesRoundTripAsUtc_AndActiveRecordsHaveNoDeletedAt()
    {
        var storage = new FakePlacesStorage
        {
            ContentsToReturn = $$"""
                { "schemaVersion": 2, "places": [
                    { "alias": "Old", "type": "folder", "resource": "{{Json(TestPaths.Folder("Old"))}}", "dateAdded": "2026-01-01T10:00:00+10:00", "deletedAt": "2026-09-20T18:00:00+10:00" }
                ] }
                """
        };
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 9, 25, 16, 30, 0, TimeSpan.FromHours(10)), TestZones.MinusFive);
        var service = new PlacesService(storage, clock);

        Assert.True(service.TryAdd("New", PlaceType.Url, "https://new.example.com", out var created, out _).Success);

        Assert.Equal(new DateTimeOffset(2026, 9, 25, 6, 30, 0, TimeSpan.Zero), created!.DateAdded);
        Assert.Equal(TimeSpan.Zero, created.DateAdded.Offset);

        var written = JsonNode.Parse(storage.LastWritten!)!;
        Assert.Equal(PlacesService.CurrentSchemaVersion, written["schemaVersion"]!.GetValue<int>());
        var old = written["places"]![0]!.AsObject();
        var added = written["places"]![1]!.AsObject();
        Assert.Equal("2026-01-01T00:00:00+00:00", old["dateAdded"]!.GetValue<string>());
        Assert.Equal("2026-09-20T08:00:00+00:00", old["deletedAt"]!.GetValue<string>());
        Assert.Equal("2026-09-25T06:30:00+00:00", added["dateAdded"]!.GetValue<string>());
        Assert.False(added.ContainsKey("deletedAt"));

        var reloaded = new PlacesService(storage, clock);
        var reloadedOld = Assert.Single(reloaded.RecentlyDeleted);
        Assert.Equal(TimeSpan.Zero, reloadedOld.DateAdded.Offset);
        Assert.Equal(TimeSpan.Zero, reloadedOld.DeletedAt!.Value.Offset);
        Assert.Equal(new DateTimeOffset(2026, 9, 20, 8, 0, 0, TimeSpan.Zero), reloadedOld.DeletedAt);
        var reloadedNew = Assert.Single(reloaded.Places);
        Assert.Equal(created.DateAdded, reloadedNew.DateAdded);
        Assert.Null(reloadedNew.DeletedAt);
    }

    /// <summary>Not a numbered plan test: CommitImport stamps DateAdded from the injected clock too (5.2).</summary>
    [Fact]
    public void CommitImport_StampsTheClock()
    {
        var service = new PlacesService(new FakePlacesStorage(), Clock());

        var (imported, _) = service.CommitImport(new[] { new Place { Alias = "A", Type = PlaceType.Url, Resource = "https://a.example.com" } });

        Assert.Equal(Now, Assert.Single(imported).DateAdded);
    }
}
