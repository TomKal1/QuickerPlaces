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
/// Schema v4 through PlacesService: RecordOpen keeps a capped open history,
/// SetTags and SetNote normalise and save, the loader puts hand-edited values
/// right, and export/import carries all three.
/// </summary>
public sealed class PlacesServiceSchemaV4Tests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 9, 30, 0, TimeSpan.Zero);

    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private static PlacesService NewService(out FakePlacesStorage storage, out ManualTimeProvider clock, string? contents = null)
    {
        storage = new FakePlacesStorage { ContentsToReturn = contents };
        clock = new ManualTimeProvider(Now, TestZones.PlusTen);
        return new PlacesService(storage, clock);
    }

    private static Place Add(PlacesService service, string alias)
    {
        Assert.True(service.TryAdd(alias, PlaceType.Url, $"https://{alias.ToLowerInvariant()}.example.com", out var place, out _).Success);
        return place!;
    }

    [Fact]
    public void RecordOpen_AppendsEachOpenInUtc_AndWritesThem()
    {
        var service = NewService(out var storage, out var clock);
        var wiki = Add(service, "Wiki");

        service.RecordOpen(wiki);
        clock.Advance(TimeSpan.FromDays(2));
        service.RecordOpen(wiki);

        Assert.Equal(new[] { Now, Now.AddDays(2) }, wiki.Opens);
        Assert.All(wiki.Opens, o => Assert.Equal(TimeSpan.Zero, o.Offset));
        var written = JsonNode.Parse(storage.LastWritten!)!["places"]![0]!["opens"]!.AsArray();
        Assert.Equal(2, written.Count);
    }

    [Fact]
    public void RecordOpen_KeepsOnlyTheNewestMaxOpens()
    {
        var service = NewService(out _, out var clock);
        var wiki = Add(service, "Wiki");

        for (var i = 0; i < Place.MaxOpens + 3; i++)
        {
            service.RecordOpen(wiki);
            clock.Advance(TimeSpan.FromMinutes(1));
        }

        Assert.Equal(Place.MaxOpens, wiki.Opens.Count);
        Assert.Equal(Now.AddMinutes(3), wiki.Opens[0]);
        Assert.Equal(Place.MaxOpens + 3, wiki.OpenCount);
    }

    [Fact]
    public void RecordOpen_OnADeletedPlace_AddsNoOpen()
    {
        var service = NewService(out _, out _);
        var wiki = Add(service, "Wiki");
        service.Remove(wiki, out _);

        service.RecordOpen(wiki);

        Assert.Empty(wiki.Opens);
    }

    [Fact]
    public void SetTags_Normalises_AndSaves()
    {
        var service = NewService(out var storage, out _);
        var wiki = Add(service, "Wiki");
        var writesBefore = storage.WriteCount;

        var result = service.SetTags(wiki, new[] { " Work ", "", null, "work", "Client A", new string('x', 100) });

        Assert.True(result.Saved);
        Assert.Equal(new[] { "Work", "Client A", new string('x', PlaceTags.MaxLength) }, wiki.Tags);
        Assert.Equal(writesBefore + 1, storage.WriteCount);
        var written = JsonNode.Parse(storage.LastWritten!)!["places"]![0]!["tags"]!.AsArray();
        Assert.Equal("Work", written[0]!.GetValue<string>());
    }

    [Fact]
    public void SetTags_Unchanged_WritesNothing()
    {
        var service = NewService(out var storage, out _);
        var wiki = Add(service, "Wiki");
        service.SetTags(wiki, new[] { "a", "b" });
        var writesBefore = storage.WriteCount;

        service.SetTags(wiki, new[] { " a", "b ", "A" });

        Assert.Equal(writesBefore, storage.WriteCount);
    }

    [Fact]
    public void SetTags_WorksOnAPlaceInRecentlyDeleted()
    {
        var service = NewService(out _, out _);
        var wiki = Add(service, "Wiki");
        service.Remove(wiki, out _);

        service.SetTags(wiki, new[] { "old" });

        Assert.Equal(new[] { "old" }, wiki.Tags);
    }

    [Fact]
    public void SetNote_TrimsAndSaves_AndBlankClearsIt()
    {
        var service = NewService(out var storage, out _);
        var wiki = Add(service, "Wiki");

        service.SetNote(wiki, "  Team wiki, ask Sam for access.  ");
        Assert.Equal("Team wiki, ask Sam for access.", wiki.Note);
        Assert.Equal("Team wiki, ask Sam for access.", JsonNode.Parse(storage.LastWritten!)!["places"]![0]!["note"]!.GetValue<string>());

        service.SetNote(wiki, "   ");
        Assert.Null(wiki.Note);
        Assert.False(JsonNode.Parse(storage.LastWritten!)!["places"]![0]!.AsObject().ContainsKey("note"));
    }

    [Fact]
    public void ANewPlace_IsWrittenWithEmptyOpensAndTags_AndNoNote()
    {
        var service = NewService(out var storage, out _);
        Add(service, "Wiki");

        var record = JsonNode.Parse(storage.LastWritten!)!["places"]![0]!.AsObject();

        Assert.Empty(record["opens"]!.AsArray());
        Assert.Empty(record["tags"]!.AsArray());
        Assert.False(record.ContainsKey("note"));
    }

    [Fact]
    public void Load_NormalisesHandEditedV4Values()
    {
        const string json = """
            { "schemaVersion": 4, "places": [
                { "id": "00000000-0000-0000-0000-000000000001", "alias": "Wiki", "type": "url", "resource": "https://wiki.example.com",
                  "dateAdded": "2026-01-01T00:00:00+00:00", "openCount": 2,
                  "opens": [ "2026-09-02T10:00:00+10:00", "2026-09-01T00:00:00+00:00" ],
                  "tags": [ " a ", "A", "" ], "note": "  " },
                { "id": "00000000-0000-0000-0000-000000000002", "alias": "Docs", "type": "url", "resource": "https://docs.example.com",
                  "dateAdded": "2026-01-01T00:00:00+00:00", "openCount": 0, "opens": null, "tags": null }
            ] }
            """;
        var service = NewService(out _, out _, json);

        Assert.Equal(StoreLoadOutcome.Ok, service.LoadOutcome);
        var wiki = service.Places.Single(p => p.Alias == "Wiki");
        Assert.Equal(new[] { new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 9, 2, 0, 0, 0, TimeSpan.Zero) }, wiki.Opens);
        Assert.All(wiki.Opens, o => Assert.Equal(TimeSpan.Zero, o.Offset));
        Assert.Equal(new[] { "a" }, wiki.Tags);
        Assert.Null(wiki.Note);
        var docs = service.Places.Single(p => p.Alias == "Docs");
        Assert.Empty(docs.Opens);
        Assert.Empty(docs.Tags);
    }

    [Fact]
    public void ExportThenImport_KeepsOpensTagsAndNote()
    {
        var exportFile = Path.Combine(_temp.Path, "export.json");
        var source = NewService(out _, out _);
        var wiki = Add(source, "Wiki");
        source.RecordOpen(wiki);
        source.SetTags(wiki, new[] { "team" });
        source.SetNote(wiki, "Shared wiki");
        Assert.Null(source.Export(source.Places, exportFile));

        var target = NewService(out _, out _);
        var (candidates, error) = target.GetImportCandidates(exportFile);
        Assert.Null(error);
        var imported = Assert.Single(target.CommitImport(candidates).imported);

        Assert.Equal(new[] { Now }, imported.Opens);
        Assert.Equal(new[] { "team" }, imported.Tags);
        Assert.Equal("Shared wiki", imported.Note);
    }

    [Fact]
    public void AV3Export_ImportsWithItsLastOpenAsTheOnlyOpen()
    {
        var exportFile = Path.Combine(_temp.Path, "export.json");
        File.WriteAllText(exportFile, """
            { "schemaVersion": 3, "places": [
                { "id": "00000000-0000-0000-0000-000000000009", "alias": "Wiki", "type": "url", "resource": "https://wiki.example.com",
                  "dateAdded": "2025-06-02T10:15:00+00:00", "lastOpenedAt": "2026-09-01T00:00:00+00:00", "openCount": 40, "tags": [ "stray" ] }
            ] }
            """);
        var service = NewService(out _, out _);

        var (candidates, error) = service.GetImportCandidates(exportFile);
        Assert.Null(error);
        var wiki = Assert.Single(service.CommitImport(candidates).imported);

        Assert.Equal(new[] { new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero) }, wiki.Opens);
        Assert.Equal(40, wiki.OpenCount);
        Assert.Empty(wiki.Tags);
    }
}
