using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using QuickerPlaces.Models;
using QuickerPlaces.Services;
using QuickerPlaces.Tests.Fakes;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>
/// Tests 5 to 11 from the Phase 3 plan's section 7: schema v3 as
/// PlacesService loads, gates, migrates, normalises and writes it (5.1),
/// and the stable Id (D27). Tests 2 to 4 are in PlacesStoreFixtureTests.
/// </summary>
public sealed class PlacesServiceSchemaV3Tests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 0, 0, 0, TimeSpan.Zero);

    private const string FixedId = "00000000-0000-0000-0000-000000000001";

    private static ManualTimeProvider Clock() => new(Now, TestZones.PlusTen);

    private static string Json(string path) => path.Replace("\\", "\\\\");

    private static readonly string V2Store = $$"""
        { "schemaVersion": 2, "places": [
            { "alias": "Docs", "type": "folder", "resource": "{{Json(TestPaths.Folder("Docs"))}}", "isFavourite": true, "favouriteOrder": 0, "dateAdded": "2026-01-01T00:00:00+00:00" },
            { "alias": "Wiki", "type": "url", "resource": "https://wiki.example.com", "isFavourite": false, "favouriteOrder": null, "dateAdded": "2026-01-02T00:00:00+00:00", "deletedAt": "2026-09-24T00:00:00+00:00" }
        ] }
        """;

    /// <summary>Test 5, version 3: binds without migrating. The fixed id proves it — the migration would have replaced it.</summary>
    [Fact]
    public void Version3_LoadsWithoutMigrating()
    {
        var storage = new FakePlacesStorage
        {
            ContentsToReturn = $$"""
                { "schemaVersion": 3, "places": [
                    { "id": "{{FixedId}}", "alias": "Wiki", "type": "url", "resource": "https://wiki.example.com", "dateAdded": "2026-01-02T00:00:00+00:00", "lastOpenedAt": "2026-09-01T10:00:00+00:00", "openCount": 4 }
                ] }
                """
        };

        var service = new PlacesService(storage, Clock());

        Assert.Equal(StoreLoadOutcome.Ok, service.LoadOutcome);
        var wiki = Assert.Single(service.Places);
        Assert.Equal(Guid.Parse(FixedId), wiki.Id);
        Assert.Equal(4, wiki.OpenCount);
        Assert.Equal(new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.Zero), wiki.LastOpenedAt);
    }

    /// <summary>Test 6: loading a v2 store migrates it in memory only; nothing is written (D11). v1's case is Phase 2's test 13.</summary>
    [Fact]
    public void LoadingAV2Store_WritesNothing()
    {
        var storage = new FakePlacesStorage { ContentsToReturn = V2Store };

        var service = new PlacesService(storage, Clock());

        Assert.Equal(StoreLoadOutcome.Ok, service.LoadOutcome);
        Assert.All(service.Places.Concat(service.RecentlyDeleted), p => Assert.NotEqual(Guid.Empty, p.Id));
        Assert.Equal(0, storage.WriteCount);
        Assert.Equal(V2Store, storage.ContentsToReturn);
    }

    /// <summary>Test 7: ids are assigned once — after the first save, a reload keeps every one (D27, D28).</summary>
    [Fact]
    public void Ids_AreAssignedOnce_AndSurviveASaveAndReload()
    {
        using var dir = new TempDirectory();
        File.WriteAllText(dir.File("places.json"), V2Store);
        var first = new PlacesService(new FilePlacesStorage(dir.Path, "places.json"), Clock());
        var before = first.Places.Concat(first.RecentlyDeleted).Select(p => (p.Alias, p.Id)).ToList();
        Assert.True(first.RetrySave().Saved);

        var second = new PlacesService(new FilePlacesStorage(dir.Path, "places.json"), Clock());

        Assert.Equal(before, second.Places.Concat(second.RecentlyDeleted).Select(p => (p.Alias, p.Id)));
        Assert.Equal(3, JsonNode.Parse(File.ReadAllText(dir.File("places.json")))!["schemaVersion"]!.GetValue<int>());
    }

    /// <summary>
    /// Test 8: a hand-edited v3 store is normalised on load — a negative
    /// openCount becomes 0, an offset lastOpenedAt becomes UTC, and an empty
    /// or repeated id is replaced. The first holder of a repeated id keeps
    /// it (D27). Nothing is written.
    /// </summary>
    [Fact]
    public void AHandEditedV3Store_IsNormalisedOnLoad()
    {
        var storage = new FakePlacesStorage
        {
            ContentsToReturn = $$"""
                { "schemaVersion": 3, "places": [
                    { "id": "{{FixedId}}", "alias": "A", "type": "url", "resource": "https://a.example.com", "dateAdded": "2026-01-01T00:00:00+00:00", "lastOpenedAt": "2026-09-01T20:00:00+10:00", "openCount": -5 },
                    { "id": "00000000-0000-0000-0000-000000000000", "alias": "B", "type": "url", "resource": "https://b.example.com", "dateAdded": "2026-01-01T00:00:00+00:00", "openCount": 1 },
                    { "id": "{{FixedId}}", "alias": "C", "type": "url", "resource": "https://c.example.com", "dateAdded": "2026-01-01T00:00:00+00:00", "openCount": 2 }
                ] }
                """
        };

        var service = new PlacesService(storage, Clock());

        Assert.Equal(StoreLoadOutcome.Ok, service.LoadOutcome);
        var (a, b, c) = (service.Places[0], service.Places[1], service.Places[2]);
        Assert.Equal(0, a.OpenCount);
        Assert.Equal(new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.Zero), a.LastOpenedAt);
        Assert.Equal(TimeSpan.Zero, a.LastOpenedAt!.Value.Offset);
        Assert.Equal(Guid.Parse(FixedId), a.Id);
        Assert.NotEqual(Guid.Empty, b.Id);
        Assert.NotEqual(Guid.Parse(FixedId), c.Id);
        Assert.Equal(3, new[] { a.Id, b.Id, c.Id }.Distinct().Count());
        Assert.Equal(0, storage.WriteCount);
    }

    /// <summary>Test 9: id first and openCount always written; lastOpenedAt only when set, and as UTC.</summary>
    [Fact]
    public void WrittenJson_HasIdFirst_OpenCountAlways_AndLastOpenedAtOnlyWhenSet()
    {
        var storage = new FakePlacesStorage
        {
            ContentsToReturn = $$"""
                { "schemaVersion": 3, "places": [
                    { "id": "{{FixedId}}", "alias": "Opened", "type": "url", "resource": "https://a.example.com", "dateAdded": "2026-01-01T00:00:00+00:00", "lastOpenedAt": "2026-09-01T20:00:00+10:00", "openCount": 1 }
                ] }
                """
        };
        var service = new PlacesService(storage, Clock());
        Assert.True(service.TryAdd("Never", PlaceType.Url, "https://b.example.com", out _, out _).Success);

        var records = JsonNode.Parse(storage.LastWritten!)!["places"]!.AsArray().Select(r => r!.AsObject()).ToList();

        Assert.All(records, r => Assert.Equal("id", r.First().Key));
        Assert.All(records, r => Assert.True(r.ContainsKey("openCount")));
        Assert.Equal("2026-09-01T10:00:00+00:00", records[0]["lastOpenedAt"]!.GetValue<string>());
        Assert.False(records[1].ContainsKey("lastOpenedAt"));
    }

    /// <summary>Test 10: a new place gets a fresh Id and has never been opened.</summary>
    [Fact]
    public void TryAdd_GivesAFreshId_AndNoUsage()
    {
        var service = new PlacesService(new FakePlacesStorage(), Clock());

        service.TryAdd("A", PlaceType.Url, "https://a.example.com", out var a, out _);
        service.TryAdd("B", PlaceType.Url, "https://b.example.com", out var b, out _);

        Assert.NotEqual(Guid.Empty, a!.Id);
        Assert.NotEqual(a.Id, b!.Id);
        Assert.Null(a.LastOpenedAt);
        Assert.Equal(0, a.OpenCount);
    }

    /// <summary>Test 11: nothing a user can do to a place changes its Id (D27).</summary>
    [Fact]
    public void Id_NeverChanges_ThroughRenameEditRemoveOrRestore()
    {
        var service = new PlacesService(new FakePlacesStorage(), Clock());
        service.TryAdd("Docs", PlaceType.Folder, TestPaths.Folder("Docs"), out var docs, out _);
        var id = docs!.Id;

        Assert.True(service.TryRenameAlias(docs, "Documents", out _).Success);
        Assert.True(service.TryEditResource(docs, TestPaths.Folder("Documents"), out _).Success);
        service.Remove(docs, out var removed);
        Assert.True(removed);
        Assert.True(service.TryRestore(docs, out _).Success);
        Assert.Equal(id, docs.Id);

        // Restored under a new alias through the conflict flow (D15).
        service.Remove(docs, out _);
        service.TryAdd("Documents", PlaceType.Url, "https://documents.example.com", out _, out _);
        Assert.True(service.TryRestore(docs, "Documents (old)", docs.Resource, out _).Success);
        Assert.Equal(id, docs.Id);
    }
}
