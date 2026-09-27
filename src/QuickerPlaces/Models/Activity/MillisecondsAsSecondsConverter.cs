using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuickerPlaces.Models.Activity;

/// <summary>
/// Writes a whole number of milliseconds as seconds with up to three
/// decimals (4321500 as 4321.5), and reads it back exactly. Time is summed
/// as integers so totals never drift from the sum of their parts, and
/// written as seconds so the file stays readable.
/// </summary>
public sealed class MillisecondsAsSecondsConverter : JsonConverter<long>
{
    public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.Number)
            throw new JsonException($"Expected a number of seconds, found {reader.TokenType}.");

        return (long)Math.Round(reader.GetDecimal() * 1000m, MidpointRounding.AwayFromZero);
    }

    public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options)
        => writer.WriteNumberValue(value / 1000m);
}
