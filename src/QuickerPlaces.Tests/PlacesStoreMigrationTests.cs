using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using QuickerPlaces.Services;
using QuickerPlaces.Tests.Fakes;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>
/// Tests 2 to 10 from the Phase 2 plan's section 7: the v1 → v2 migration
/// (D11) on its own, as a pure transform of a JsonObject. Every zone is
/// passed in explicitly from TestZones, so none of these depends on the
/// zone of the machine running them.
/// </summary>
public sealed class PlacesStoreMigrationTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 8, 0, 0, TimeSpan.Zero);

    /// <summary>A v1 document holding one record per given JSON fragment (each an object's body, or a whole JSON value).</summary>
    private static JsonObject V1Document(params string[] records)
        => JsonNode.Parse($$"""{ "schemaVersion": 1, "places": [ {{string.Join(", ", records)}} ] }""")!.AsObject();

    private static string Record(string dateAddedJson)
        => $$"""{ "alias": "Docs", "type": "folder", "resource": "C:\\Docs", "dateAdded": {{dateAddedJson}} }""";

    /// <summary>Migrates a one-record document with the given dateAdded under <paramref name="zone"/>, and returns the rewritten value.</summary>
    private static DateTimeOffset MigrateOne(string dateAddedJson, TimeZoneInfo zone)
    {
        var root = V1Document(Record(dateAddedJson));
        PlacesStoreMigration.MigrateV1ToV2(root, zone, Now);
        return DateAddedOf(root, 0);
    }

    private static DateTimeOffset DateAddedOf(JsonObject root, int index)
    {
        var value = root["places"]![index]!["dateAdded"]!.GetValue<DateTimeOffset>();
        // DateTimeOffset equality compares instants only; the stored value
        // must also *be* UTC, or the next save writes a local offset back.
        Assert.Equal(TimeSpan.Zero, value.Offset);
        return value;
    }

    private static DateTimeOffset Utc(int year, int month, int day, int hour, int minute, int second = 0)
        => new(year, month, day, hour, minute, second, TimeSpan.Zero);

    /// <summary>Test 2: an offset-less dateAdded is read as local time in the given zone (roadmap §4.7).</summary>
    [Fact]
    public void OffsetLessValue_IsReadAsLocalTimeInTheGivenZone()
    {
        Assert.Equal(Utc(2026, 1, 14, 23, 30), MigrateOne("\"2026-01-15T09:30:00\"", TestZones.PlusTen));
        Assert.Equal(Utc(2026, 1, 15, 14, 30), MigrateOne("\"2026-01-15T09:30:00\"", TestZones.MinusFive));
    }

    /// <summary>Test 3: an offset-bearing value converts exactly, whatever the zone — down to the tick.</summary>
    [Fact]
    public void OffsetBearingValue_ConvertsExactly_WhateverTheZone()
    {
        var migrated = MigrateOne("\"2026-01-15T09:30:00.1234567+10:00\"", TestZones.MinusFive);

        Assert.Equal(Utc(2026, 1, 14, 23, 30).AddTicks(1234567), migrated);
    }

    /// <summary>Test 4: a Z value keeps its instant.</summary>
    [Fact]
    public void ZValue_KeepsItsInstant()
        => Assert.Equal(Utc(2026, 1, 15, 9, 30), MigrateOne("\"2026-01-15T09:30:00Z\"", TestZones.PlusTen));

    /// <summary>
    /// Test 5: a time in the spring-forward gap (02:00–03:00 does not exist
    /// on 2026-03-29 in Central Europe) takes the standard offset, +01:00,
    /// and does not throw. The times either side prove the zone's DST rule
    /// is really in effect — without them, a plain fixed +01:00 zone would
    /// pass this test too.
    /// </summary>
    [Theory]
    [InlineData("2026-03-29T01:30:00", "2026-03-29T00:30:00Z")] // before the gap: standard, +01:00
    [InlineData("2026-03-29T02:30:00", "2026-03-29T01:30:00Z")] // in the gap: standard offset
    [InlineData("2026-03-29T03:30:00", "2026-03-29T01:30:00Z")] // after the gap: daylight, +02:00
    public void TimeInTheSpringForwardGap_UsesTheStandardOffset(string local, string expectedUtc)
    {
        Assert.True(TestZones.CentralEuropean.IsInvalidTime(DateTime.Parse("2026-03-29T02:30:00")));

        Assert.Equal(DateTimeOffset.Parse(expectedUtc), MigrateOne($"\"{local}\"", TestZones.CentralEuropean));
    }

    /// <summary>Test 6: a time in the ambiguous fall-back hour (02:00–03:00 happens twice on 2026-10-25) takes the standard offset, +01:00.</summary>
    [Theory]
    [InlineData("2026-10-25T01:30:00", "2026-10-24T23:30:00Z")] // before: still daylight, +02:00
    [InlineData("2026-10-25T02:30:00", "2026-10-25T01:30:00Z")] // ambiguous: standard offset
    [InlineData("2026-10-25T03:30:00", "2026-10-25T02:30:00Z")] // after: standard, +01:00
    public void TimeInTheAmbiguousFallBackHour_UsesTheStandardOffset(string local, string expectedUtc)
    {
        Assert.True(TestZones.CentralEuropean.IsAmbiguousTime(DateTime.Parse("2026-10-25T02:30:00")));

        Assert.Equal(DateTimeOffset.Parse(expectedUtc), MigrateOne($"\"{local}\"", TestZones.CentralEuropean));
    }

    /// <summary>Test 7: a missing dateAdded — absent, or JSON null — becomes the clock's now.</summary>
    [Fact]
    public void MissingDateAdded_BecomesTheClocksNow()
    {
        var root = V1Document(
            """{ "alias": "Docs", "type": "folder", "resource": "C:\\Docs" }""",
            Record("null"));

        var report = PlacesStoreMigration.MigrateV1ToV2(root, TestZones.PlusTen, Now);

        Assert.Equal(Now, DateAddedOf(root, 0));
        Assert.Equal(Now, DateAddedOf(root, 1));
        Assert.Equal(2, report.MissingDates);
    }

    /// <summary>Test 8: a present dateAdded that is not a date throws JsonException, so the loader's catch classifies the store Damaged (D6).</summary>
    [Theory]
    [InlineData("5")]
    [InlineData("\"yesterday\"")]
    [InlineData("true")]
    [InlineData("{ }")]
    [InlineData("\"\"")]
    public void NonDateDateAdded_ThrowsJsonException(string dateAddedJson)
    {
        var root = V1Document(Record(dateAddedJson));

        Assert.Throws<JsonException>(() => PlacesStoreMigration.MigrateV1ToV2(root, TestZones.PlusTen, Now));
    }

    /// <summary>A place that is not an object, or a place list that is not an array, is also a shape the migration cannot handle: JsonException, never another type.</summary>
    [Theory]
    [InlineData("""{ "schemaVersion": 1, "places": [ "Docs" ] }""")]
    [InlineData("""{ "schemaVersion": 1, "places": { "alias": "Docs" } }""")]
    public void UnmigratableShape_ThrowsJsonException(string json)
    {
        var root = JsonNode.Parse(json)!.AsObject();

        Assert.Throws<JsonException>(() => PlacesStoreMigration.MigrateV1ToV2(root, TestZones.PlusTen, Now));
    }

    /// <summary>
    /// Test 9: the output is schemaVersion 2 and has no deletedAt, and a
    /// null entry is skipped (left for the loader to drop, as in v1) rather
    /// than throwing. The input's stray deletedAt — meaningless in v1 —
    /// must not arrive in v2 as "removed".
    /// </summary>
    [Fact]
    public void Output_IsVersion2_HasNoDeletedAt_AndSkipsNullEntries()
    {
        var root = V1Document(
            "null",
            """{ "alias": "Docs", "type": "folder", "resource": "C:\\Docs", "dateAdded": "2026-01-15T09:30:00", "deletedAt": "2026-09-01T00:00:00Z" }""");

        var report = PlacesStoreMigration.MigrateV1ToV2(root, TestZones.PlusTen, Now);

        Assert.Equal(2, root["schemaVersion"]!.GetValue<int>());
        var places = root["places"]!.AsArray();
        Assert.Equal(2, places.Count);
        Assert.Null(places[0]);
        Assert.False(places[1]!.AsObject().ContainsKey("deletedAt"));
        Assert.Equal("Docs", places[1]!["alias"]!.GetValue<string>());
        Assert.Equal(1, report.Records);
    }

    /// <summary>A document with no place list migrates to version 2 with nothing counted; the loader's own check then decides what it is.</summary>
    [Fact]
    public void MissingPlaceList_MigratesNothing()
    {
        var root = JsonNode.Parse("""{ "schemaVersion": 1 }""")!.AsObject();

        var report = PlacesStoreMigration.MigrateV1ToV2(root, TestZones.PlusTen, Now);

        Assert.Equal(2, root["schemaVersion"]!.GetValue<int>());
        Assert.Equal(0, report.Records);
    }

    /// <summary>Test 10: the report counts exact, interpreted and missing values separately, and names the zone — the content of D11's log line.</summary>
    [Fact]
    public void Report_CountsEachKindOfValue_AndNamesTheZone()
    {
        var root = V1Document(
            Record("\"2026-01-15T09:30:00+10:00\""),
            Record("\"2026-01-15T09:30:00\""),
            Record("\"2026-01-15T09:30:00Z\""),
            """{ "alias": "Docs", "type": "folder", "resource": "C:\\Docs" }""",
            "null");

        var report = PlacesStoreMigration.MigrateV1ToV2(root, TestZones.PlusTen, Now);

        Assert.Equal(new MigrationReport(Records: 4, ExactOffsets: 2, InterpretedAsLocal: 1, MissingDates: 1, ZoneId: "Test/PlusTen"), report);
    }

    /// <summary>
    /// Not a numbered plan test: an offset-less date at the very start of
    /// the calendar (a default DateTime, "0001-01-01T00:00:00") has no UTC
    /// instant east of Greenwich. It clamps to the earliest one rather than
    /// throwing ArgumentOutOfRangeException, which D6's catch would not
    /// classify and which would escape the PlacesService constructor.
    /// </summary>
    [Fact]
    public void OffsetLessValueAtTheEdgeOfTheCalendar_ClampsInsteadOfThrowing()
        => Assert.Equal(DateTimeOffset.MinValue, MigrateOne("\"0001-01-01T00:00:00\"", TestZones.PlusTen));

    /// <summary>Not a numbered plan test: a document built in code (string-backed JsonValues, not parsed ones) migrates by the same rules.</summary>
    [Fact]
    public void DocumentBuiltInCode_MigratesTheSameWay()
    {
        var root = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["places"] = new JsonArray(
                new JsonObject { ["alias"] = "A", ["dateAdded"] = "2026-01-15T09:30:00" },
                new JsonObject { ["alias"] = "B", ["dateAdded"] = "2026-01-15T09:30:00+10:00" })
        };

        var report = PlacesStoreMigration.MigrateV1ToV2(root, TestZones.PlusTen, Now);

        Assert.Equal(Utc(2026, 1, 14, 23, 30), DateAddedOf(root, 0));
        Assert.Equal(Utc(2026, 1, 14, 23, 30), DateAddedOf(root, 1));
        Assert.Equal(1, report.ExactOffsets);
        Assert.Equal(1, report.InterpretedAsLocal);
    }
}
