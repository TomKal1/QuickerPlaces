using System.Text.Json;
using System.Text.Json.Nodes;
using QuickerPlaces.Services;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>
/// The v3 → v4 migration (opens, tags, note) on its own, as a pure transform
/// of a JsonObject, like its siblings in PlacesStoreMigrationTests and
/// PlacesStoreMigrationV3Tests.
/// </summary>
public sealed class PlacesStoreMigrationV4Tests
{
    private static JsonObject V3Document(params string[] records)
        => JsonNode.Parse($$"""{ "schemaVersion": 3, "places": [ {{string.Join(", ", records)}} ] }""")!.AsObject();

    private static string Record(string alias, string extra = "")
        => $$"""{ "id": "00000000-0000-0000-0000-000000000001", "alias": "{{alias}}", "type": "folder", "resource": "C:\\{{alias}}", "dateAdded": "2026-01-14T23:30:00+00:00", "openCount": 0{{extra}} }""";

    private static JsonObject RecordAt(JsonObject root, int index) => root["places"]![index]!.AsObject();

    [Fact]
    public void LastOpenedAt_IsSeededAsTheOnlyOpen()
    {
        var root = V3Document(Record("A", ", \"lastOpenedAt\": \"2026-09-01T10:00:00+00:00\""));

        var report = PlacesStoreMigration.MigrateV3ToV4(root);

        var opens = RecordAt(root, 0)["opens"]!.AsArray();
        Assert.Equal("2026-09-01T10:00:00+00:00", Assert.Single(opens)!.GetValue<string>());
        Assert.Equal(1, report.OpensSeeded);
        Assert.Equal(4, root["schemaVersion"]!.GetValue<int>());
    }

    [Fact]
    public void ANeverOpenedRecord_GetsNoOpens()
    {
        var root = V3Document(Record("A"));

        var report = PlacesStoreMigration.MigrateV3ToV4(root);

        Assert.False(RecordAt(root, 0).ContainsKey("opens"));
        Assert.Equal(0, report.OpensSeeded);
        Assert.Equal(1, report.Records);
    }

    [Fact]
    public void StrayV4Fields_AreRemoved_AndCounted()
    {
        var root = V3Document(Record("A", ", \"opens\": [\"2020-01-01T00:00:00+00:00\"], \"tags\": [\"x\"], \"note\": \"n\""));

        var report = PlacesStoreMigration.MigrateV3ToV4(root);

        Assert.False(RecordAt(root, 0).ContainsKey("opens"));
        Assert.False(RecordAt(root, 0).ContainsKey("tags"));
        Assert.False(RecordAt(root, 0).ContainsKey("note"));
        Assert.Equal(3, report.StrayFieldsRemoved);
    }

    [Fact]
    public void ANonObjectRecord_Throws()
    {
        var root = JsonNode.Parse("""{ "schemaVersion": 3, "places": [ 7 ] }""")!.AsObject();

        Assert.Throws<JsonException>(() => PlacesStoreMigration.MigrateV3ToV4(root));
    }

    [Fact]
    public void APlaceListThatIsNotAnArray_Throws()
    {
        var root = JsonNode.Parse("""{ "schemaVersion": 3, "places": {} }""")!.AsObject();

        Assert.Throws<JsonException>(() => PlacesStoreMigration.MigrateV3ToV4(root));
    }
}
