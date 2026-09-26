using System;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace QuickerPlaces.Services;

/// <summary>
/// Schema migrations for places.json, run on the parsed JSON before it is
/// bound to Place (D11). Binding first would let System.Text.Json convert
/// an offset-less dateAdded into a DateTimeOffset using the process's own
/// local offset — a silent conversion no test can control, and not the
/// rule roadmap §4.7 asks to be recorded. Working on the JsonObject keeps
/// every conversion here, driven by the zone and clock the caller passes
/// in (PlacesService's injected TimeProvider, D12).
///
/// UI-free and linked into the test project, like PlacesService.
/// </summary>
public static class PlacesStoreMigration
{
    private const int V2 = 2;
    private const int V3 = 3;

    /// <summary>
    /// Rewrites a schemaVersion 1 document in place as schemaVersion 2 (D11). Throws
    /// JsonException for any shape it cannot migrate, so PlacesService's existing catch
    /// classifies the store as Damaged (D6) rather than a new exception type escaping
    /// the constructor.
    /// </summary>
    /// <param name="root">The whole parsed document. Only its "places" records and "schemaVersion" are touched.</param>
    /// <param name="localZone">The zone an offset-less dateAdded is interpreted in — the migrating machine's, from the injected clock.</param>
    /// <param name="utcNow">What a missing dateAdded becomes.</param>
    public static MigrationReport MigrateV1ToV2(JsonObject root, TimeZoneInfo localZone, DateTimeOffset utcNow)
    {
        var records = 0;
        var exactOffsets = 0;
        var interpretedAsLocal = 0;
        var missingDates = 0;

        switch (root["places"])
        {
            case null:
                // Missing or JSON null: nothing to migrate. The loader's own
                // "no usable place list" check classifies that document
                // (Damaged) with its own log line, exactly as for v1 today.
                break;

            case JsonArray places:
                foreach (var entry in places)
                {
                    // A bare null in the array is dropped by the loader after
                    // binding (plan 5.3 row 15), as it was in v1; left alone here.
                    if (entry is null)
                        continue;

                    if (entry is not JsonObject record)
                        throw new JsonException("A stored place is not a JSON object.");

                    records++;

                    // v1 has no deletedAt, and the v1 loader ignored unknown
                    // properties, so a stray one in a hand-edited v1 file meant
                    // nothing. It must not start meaning "removed" on upgrade.
                    record.Remove("deletedAt");

                    switch (MigrateDateAdded(record, localZone, utcNow))
                    {
                        case DateKind.Exact: exactOffsets++; break;
                        case DateKind.InterpretedAsLocal: interpretedAsLocal++; break;
                        case DateKind.Missing: missingDates++; break;
                    }
                }
                break;

            default:
                throw new JsonException("The stored place list is not a JSON array.");
        }

        root["schemaVersion"] = V2;
        return new MigrationReport(records, exactOffsets, interpretedAsLocal, missingDates, localZone.Id);
    }

    /// <summary>
    /// Rewrites a schemaVersion 2 document in place as schemaVersion 3 (Phase 3 D28): a
    /// fresh id per record, any stray lastOpenedAt/openCount removed. Throws JsonException
    /// for a shape it cannot migrate, as MigrateV1ToV2 does, so D6's catch classifies the
    /// store Damaged.
    /// </summary>
    /// <param name="root">The whole parsed document. Only its "places" records and "schemaVersion" are touched.</param>
    public static MigrationV3Report MigrateV2ToV3(JsonObject root)
    {
        var records = 0;
        var strayFields = 0;

        switch (root["places"])
        {
            case null:
                // Left for the loader's own "no usable place list" check, as in MigrateV1ToV2.
                break;

            case JsonArray places:
                foreach (var entry in places)
                {
                    if (entry is null)
                        continue;

                    if (entry is not JsonObject record)
                        throw new JsonException("A stored place is not a JSON object.");

                    records++;

                    // v2 never gave these a meaning, so a hand-edited one must
                    // not start meaning "opened" on upgrade (the same reasoning
                    // as v1's stray deletedAt). Missing usage binds to the
                    // defaults: never opened, count 0 (roadmap §4.11).
                    if (record.Remove("lastOpenedAt"))
                        strayFields++;
                    if (record.Remove("openCount"))
                        strayFields++;

                    // Every record gets its identity here, once (D27). An id
                    // already in a v2 file meant nothing, so it is replaced
                    // rather than trusted. Written as the text System.Text.Json
                    // itself writes for a Guid, so the node looks exactly like
                    // one parsed from a v3 file (a Guid-typed JsonValue built in
                    // code refuses GetValue<string>).
                    record["id"] = Guid.NewGuid().ToString("D");
                }
                break;

            default:
                throw new JsonException("The stored place list is not a JSON array.");
        }

        root["schemaVersion"] = V3;
        return new MigrationV3Report(records, strayFields);
    }

    private enum DateKind
    {
        Exact,
        InterpretedAsLocal,
        Missing
    }

    /// <summary>Rewrites one record's dateAdded as a UTC value, and says which rule it took.</summary>
    private static DateKind MigrateDateAdded(JsonObject record, TimeZoneInfo localZone, DateTimeOffset utcNow)
    {
        var node = record["dateAdded"];

        // Absent or JSON null. The v1 loader filled this in from Place's
        // DateTime.Now initialiser; "now" from the injected clock is the same
        // thing, made testable (D11).
        if (node is null)
        {
            record["dateAdded"] = utcNow.ToUniversalTime();
            return DateKind.Missing;
        }

        // Read through a JsonElement so the test is System.Text.Json's own
        // ISO 8601 parser however the node was built: a JsonValue built in
        // code from a string refuses TryGetValue<DateTime> outright, where a
        // parsed one (as the loader's always is) accepts it.
        var element = JsonSerializer.SerializeToElement(node);
        if (element.ValueKind != JsonValueKind.String || !element.TryGetDateTime(out var parsed))
            throw new JsonException("A stored place's dateAdded is not a date.");

        DateTimeOffset utc;
        if (parsed.Kind == DateTimeKind.Unspecified)
        {
            // No offset in the file: interpreted as local time on the
            // migrating machine (roadmap §4.7) — the injected zone, never
            // the process's own. Only hand-edited files and the v1 fixture
            // hold this shape: the application itself always wrote
            // DateTime.Now, which System.Text.Json serialises with an offset
            // (plan section 1, finding 1).
            //
            // GetUtcOffset returns the zone's *standard* offset for a time
            // in a spring-forward gap and for one that happens twice in a
            // fall-back hour, so this never throws on a real calendar.
            // Built from ticks and clamped rather than through the
            // DateTimeOffset constructor, which would throw for a date at
            // the very edge of the calendar (a default "0001-01-01T00:00:00"
            // east of Greenwich) — an exception type D6's catch does not
            // classify, so it would escape the constructor.
            var offset = localZone.GetUtcOffset(parsed);
            var utcTicks = Math.Clamp(parsed.Ticks - offset.Ticks, DateTime.MinValue.Ticks, DateTime.MaxValue.Ticks);
            utc = new DateTimeOffset(utcTicks, TimeSpan.Zero);
            record["dateAdded"] = utc;
            return DateKind.InterpretedAsLocal;
        }

        // An offset or Z: converted exactly; the migrating machine's zone
        // plays no part. Re-read as a DateTimeOffset because the DateTime
        // above has already been shifted into the process's local time.
        if (!element.TryGetDateTimeOffset(out var exact))
            throw new JsonException("A stored place's dateAdded is not a date.");

        utc = exact.ToUniversalTime();
        record["dateAdded"] = utc;
        return DateKind.Exact;
    }
}

/// <summary>What a migration did, for the log line (counts only: never aliases or destinations).</summary>
/// <param name="Records">Non-null place records migrated.</param>
/// <param name="ExactOffsets">dateAdded values that carried an offset or Z, converted exactly.</param>
/// <param name="InterpretedAsLocal">dateAdded values without an offset, interpreted as local time in <paramref name="ZoneId"/>.</param>
/// <param name="MissingDates">Records without a dateAdded, given the clock's now.</param>
/// <param name="ZoneId">TimeZoneInfo.Id of the zone offset-less values were interpreted in.</param>
public readonly record struct MigrationReport(int Records, int ExactOffsets, int InterpretedAsLocal, int MissingDates, string ZoneId);

/// <summary>What the v2 → v3 migration did, for the log line (counts only: never aliases or destinations).</summary>
/// <param name="Records">Non-null place records migrated, each given a fresh id.</param>
/// <param name="StrayFieldsRemoved">lastOpenedAt/openCount properties found in v2 records and removed.</param>
public readonly record struct MigrationV3Report(int Records, int StrayFieldsRemoved);
