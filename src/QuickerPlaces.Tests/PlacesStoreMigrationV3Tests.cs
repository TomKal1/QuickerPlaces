using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using QuickerPlaces.Services;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>
/// Test 1 from the Phase 3 plan's section 7: the v2 → v3 migration (D28)
/// on its own, as a pure transform of a JsonObject, like its v1 → v2
/// sibling in PlacesStoreMigrationTests.
/// </summary>
public sealed class PlacesStoreMigrationV3Tests
{
    private const string StrayId = "00000000-0000-0000-0000-0000000000aa";

    private static JsonObject V2Document(params string[] records)
        => JsonNode.Parse($$"""{ "schemaVersion": 2, "places": [ {{string.Join(", ", records)}} ] }""")!.AsObject();

    private static string Record(string alias, string extra = "")
        => $$"""{ "alias": "{{alias}}", "type": "folder", "resource": "C:\\{{alias}}", "dateAdded": "2026-01-14T23:30:00+00:00"{{extra}} }""";

    private static JsonObject RecordAt(JsonObject root, int index) => root["places"]![index]!.AsObject();

    private static Guid IdOf(JsonObject record) => Guid.Parse(record["id"]!.GetValue<string>());

    [Fact]
    public void EveryRecord_GetsADistinctNonEmptyId()
    {
        var root = V2Document(Record("A"), Record("B"), Record("C"));

        PlacesStoreMigration.MigrateV2ToV3(root);

        var ids = Enumerable.Range(0, 3).Select(i => IdOf(RecordAt(root, i))).ToList();
        Assert.DoesNotContain(Guid.Empty, ids);
        Assert.Equal(3, ids.Distinct().Count());
    }

    [Fact]
    public void AStrayIdInAV2Record_IsReplaced()
    {
        var root = V2Document(Record("A", $", \"id\": \"{StrayId}\""));

        PlacesStoreMigration.MigrateV2ToV3(root);

        Assert.NotEqual(Guid.Parse(StrayId), IdOf(RecordAt(root, 0)));
    }

    [Fact]
    public void StrayUsageFields_AreRemoved_AndCounted()
    {
        var root = V2Document(
            Record("A", ", \"lastOpenedAt\": \"2026-09-01T10:00:00+00:00\", \"openCount\": 7"),
            Record("B", ", \"openCount\": 2"),
            Record("C"));

        var report = PlacesStoreMigration.MigrateV2ToV3(root);

        foreach (var i in new[] { 0, 1, 2 })
        {
            Assert.False(RecordAt(root, i).ContainsKey("lastOpenedAt"));
            Assert.False(RecordAt(root, i).ContainsKey("openCount"));
        }
        Assert.Equal(3, report.StrayFieldsRemoved);
        Assert.Equal(3, report.Records);
    }

    [Fact]
    public void OtherFields_AreLeftAlone()
    {
        var root = V2Document(Record("A", ", \"isFavourite\": true, \"favouriteOrder\": 0, \"deletedAt\": \"2026-09-20T08:00:00+00:00\""));
        var before = RecordAt(root, 0).DeepClone().AsObject();

        PlacesStoreMigration.MigrateV2ToV3(root);

        var after = RecordAt(root, 0);
        foreach (var (name, value) in before)
            Assert.True(JsonNode.DeepEquals(value, after[name]), name);
    }

    [Fact]
    public void NullEntries_AreSkipped_AndTheVersionBecomesThree()
    {
        var root = V2Document("null", Record("A"));

        var report = PlacesStoreMigration.MigrateV2ToV3(root);

        Assert.Null(root["places"]![0]);
        Assert.NotEqual(Guid.Empty, IdOf(RecordAt(root, 1)));
        Assert.Equal(1, report.Records);
        Assert.Equal(3, root["schemaVersion"]!.GetValue<int>());
    }

    [Fact]
    public void MissingPlaces_IsLeftForTheLoader()
    {
        var root = JsonNode.Parse("""{ "schemaVersion": 2 }""")!.AsObject();

        var report = PlacesStoreMigration.MigrateV2ToV3(root);

        Assert.Equal(0, report.Records);
        Assert.Equal(3, root["schemaVersion"]!.GetValue<int>());
    }

    [Theory]
    [InlineData("""{ "schemaVersion": 2, "places": [ 5 ] }""")]
    [InlineData("""{ "schemaVersion": 2, "places": { "alias": "A" } }""")]
    public void AnUnmigratableShape_ThrowsJsonException(string json)
    {
        var root = JsonNode.Parse(json)!.AsObject();

        Assert.Throws<JsonException>(() => PlacesStoreMigration.MigrateV2ToV3(root));
    }
}
