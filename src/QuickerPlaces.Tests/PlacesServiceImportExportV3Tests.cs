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
/// Tests 28 and 30 to 33 from the Phase 3 plan's section 7: export writes
/// v3 with the new fields, and import keeps DateAdded, usage and — when it
/// is free — the Id (D33). Test 29, the round trip, extends
/// PlacesServiceTests' Export_then_import_into_empty_store_round_trips;
/// test 31, the newer-version refusal, is that class's
/// An_import_file_from_a_newer_version_is_refused, moved to version 4.
/// </summary>
public sealed class PlacesServiceImportExportV3Tests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 9, 30, 0, TimeSpan.Zero);

    private readonly TempDirectory _temp = new();
    private readonly ManualTimeProvider _clock = new(Now, TestZones.PlusTen);

    public void Dispose() => _temp.Dispose();

    private string ExportFile => Path.Combine(_temp.Path, "export.json");

    private PlacesService NewService(string fileName = "places.json") => new(new FilePlacesStorage(_temp.Path, fileName), _clock);

    private static string Json(string path) => path.Replace("\\", "\\\\");

    private static Guid Id(int n) => Guid.Parse($"00000000-0000-0000-0000-{n:D12}");

    private Place Add(PlacesService service, string alias)
    {
        Assert.True(service.TryAdd(alias, PlaceType.Url, $"https://{alias.ToLowerInvariant()}.example.com", out var place, out _).Success);
        return place!;
    }

    /// <summary>Test 28: an export is v3 and carries id and openCount always, lastOpenedAt when set — and still no deleted record (D16).</summary>
    [Fact]
    public void Export_WritesV3_WithIdAndUsage()
    {
        var service = NewService();
        var opened = Add(service, "Opened");
        var never = Add(service, "Never");
        var removed = Add(service, "Removed");
        service.RecordOpen(opened);
        service.Remove(removed, out _);

        Assert.Null(service.Export(service.Places.Concat(service.RecentlyDeleted), ExportFile));

        var written = JsonNode.Parse(File.ReadAllText(ExportFile))!;
        Assert.Equal(3, written["schemaVersion"]!.GetValue<int>());
        var records = written["places"]!.AsArray().Select(r => r!.AsObject()).ToList();
        Assert.Equal(new[] { "Opened", "Never" }, records.Select(r => r["alias"]!.GetValue<string>()));
        Assert.Equal(opened.Id, Guid.Parse(records[0]["id"]!.GetValue<string>()));
        Assert.Equal(1, records[0]["openCount"]!.GetValue<int>());
        Assert.True(records[0].ContainsKey("lastOpenedAt"));
        Assert.Equal(never.Id, Guid.Parse(records[1]["id"]!.GetValue<string>()));
        Assert.Equal(0, records[1]["openCount"]!.GetValue<int>());
        Assert.False(records[1].ContainsKey("lastOpenedAt"));
    }

    /// <summary>
    /// Test 30: a v2 export imports through v2 → v3 — fresh ids, never-opened
    /// defaults — and keeps its original DateAdded (D33). A stray openCount
    /// in the v2 file means nothing, as in the store (D28).
    /// </summary>
    [Fact]
    public void V2Export_ImportsWithFreshIds_DefaultUsage_AndItsOriginalDateAdded()
    {
        File.WriteAllText(ExportFile, """
            { "schemaVersion": 2, "places": [
                { "id": "00000000-0000-0000-0000-000000000009", "alias": "Wiki", "type": "url", "resource": "https://wiki.example.com", "dateAdded": "2025-06-02T10:15:00+00:00", "openCount": 40 }
            ] }
            """);
        var service = NewService();

        var (candidates, error) = service.GetImportCandidates(ExportFile);
        Assert.Null(error);
        var wiki = Assert.Single(service.CommitImport(candidates).imported);

        Assert.NotEqual(Guid.Empty, wiki.Id);
        Assert.NotEqual(Id(9), wiki.Id);
        Assert.Equal(0, wiki.OpenCount);
        Assert.Null(wiki.LastOpenedAt);
        Assert.Equal(new DateTimeOffset(2025, 6, 2, 10, 15, 0, TimeSpan.Zero), wiki.DateAdded);
    }

    /// <summary>
    /// Test 32: an incoming id already held — by an active place, by a place
    /// in Recently Deleted, or by an earlier record in the same import — gets
    /// a fresh one; a free id is kept (D33).
    /// </summary>
    [Fact]
    public void AnIncomingIdAlreadyHeld_IsReplaced_AndAFreeOneIsKept()
    {
        var service = NewService();
        var active = Add(service, "Active");
        var deleted = Add(service, "Deleted");
        service.Remove(deleted, out _);
        File.WriteAllText(ExportFile, $$"""
            { "schemaVersion": 3, "places": [
                { "id": "{{active.Id}}", "alias": "A", "type": "url", "resource": "https://a.example.com", "dateAdded": "2026-01-01T00:00:00+00:00", "openCount": 0 },
                { "id": "{{deleted.Id}}", "alias": "B", "type": "url", "resource": "https://b.example.com", "dateAdded": "2026-01-01T00:00:00+00:00", "openCount": 0 },
                { "id": "{{Id(7)}}", "alias": "C", "type": "url", "resource": "https://c.example.com", "dateAdded": "2026-01-01T00:00:00+00:00", "openCount": 0 },
                { "id": "{{Id(7)}}", "alias": "D", "type": "url", "resource": "https://d.example.com", "dateAdded": "2026-01-01T00:00:00+00:00", "openCount": 0 }
            ] }
            """);

        var (candidates, _) = service.GetImportCandidates(ExportFile);
        var imported = service.CommitImport(candidates).imported;

        var byAlias = imported.ToDictionary(p => p.Alias);
        Assert.NotEqual(active.Id, byAlias["A"].Id);
        Assert.NotEqual(deleted.Id, byAlias["B"].Id);
        Assert.Equal(Id(7), byAlias["C"].Id);
        Assert.NotEqual(Id(7), byAlias["D"].Id);
        var allIds = service.Places.Concat(service.RecentlyDeleted).Select(p => p.Id).ToList();
        Assert.Equal(allIds.Count, allIds.Distinct().Count());
        Assert.DoesNotContain(Guid.Empty, allIds);
    }

    /// <summary>Test 33: an imported negative openCount becomes 0, an offset lastOpenedAt becomes UTC, and an empty id is replaced (D33).</summary>
    [Fact]
    public void ImportedUsage_IsNormalised()
    {
        File.WriteAllText(ExportFile, """
            { "schemaVersion": 3, "places": [
                { "id": "00000000-0000-0000-0000-000000000000", "alias": "Wiki", "type": "url", "resource": "https://wiki.example.com", "dateAdded": "2026-01-01T10:00:00+10:00", "lastOpenedAt": "2026-09-01T20:00:00+10:00", "openCount": -3 }
            ] }
            """);
        var service = NewService();

        var (candidates, _) = service.GetImportCandidates(ExportFile);
        var wiki = Assert.Single(service.CommitImport(candidates).imported);

        Assert.Equal(0, wiki.OpenCount);
        Assert.Equal(new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.Zero), wiki.LastOpenedAt);
        Assert.Equal(TimeSpan.Zero, wiki.LastOpenedAt!.Value.Offset);
        Assert.Equal(TimeSpan.Zero, wiki.DateAdded.Offset);
        Assert.NotEqual(Guid.Empty, wiki.Id);
    }
}
