using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuickerPlaces.Services.Revit.Handlers;

/// <summary>What reading one protocol file produced: the document, or why there is none.</summary>
public sealed record HandlerParse<T> where T : class, IHandlerDocument
{
    public T? Value { get; private init; }

    /// <summary>Why the file was refused; null when <see cref="Value"/> is set.</summary>
    public string? Failure { get; private init; }

    public bool IsValid => Value is not null;

    public static HandlerParse<T> Success(T value) => new() { Value = value };

    public static HandlerParse<T> Fail(string reason) => new() { Failure = reason };
}

/// <summary>
/// Reads and writes the protocol's JSON (docs/revit-handler-protocol.md,
/// "Files in general"): camelCase, unknown properties ignored, a UTF-8 byte-order
/// mark accepted and never written, files over 64 KB refused, times as UTC
/// ISO 8601 round-trip text with a Z. Reading never throws: bad input is
/// reported in <see cref="HandlerParse{T}.Failure"/>.
/// </summary>
public static class HandlerJson
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
        };
        options.Converters.Add(new UtcDateTimeConverter());
        options.Converters.Add(new UtcNullableDateTimeConverter());
        return options;
    }

    /// <summary>Parses the bytes of a protocol file; a leading UTF-8 byte-order mark is skipped.</summary>
    public static HandlerParse<T> Parse<T>(ReadOnlySpan<byte> utf8) where T : class, IHandlerDocument
    {
        if (utf8.Length > HandlerProtocol.MaxFileBytes)
            return HandlerParse<T>.Fail("file is larger than 64 KB");

        if (utf8.Length >= 3 && utf8[0] == 0xEF && utf8[1] == 0xBB && utf8[2] == 0xBF)
            utf8 = utf8[3..];

        try
        {
            var value = JsonSerializer.Deserialize<T>(utf8, Options);
            if (value is null)
                return HandlerParse<T>.Fail("file does not contain an object");

            var problem = value.Problem();
            return problem is null ? HandlerParse<T>.Success(value) : HandlerParse<T>.Fail(problem);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            return HandlerParse<T>.Fail("not valid JSON: " + ex.Message);
        }
    }

    public static HandlerParse<T> Parse<T>(string json) where T : class, IHandlerDocument =>
        Parse<T>(System.Text.Encoding.UTF8.GetBytes(json));

    /// <summary>Reads a protocol file; a missing, locked, oversized or invalid one is a failure, not an exception.</summary>
    public static HandlerParse<T> ReadFile<T>(string path) where T : class, IHandlerDocument
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > HandlerProtocol.MaxFileBytes)
                return HandlerParse<T>.Fail("file is larger than 64 KB");

            var buffer = new byte[(int)stream.Length];
            stream.ReadExactly(buffer);
            return Parse<T>(buffer);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or NotSupportedException)
        {
            return HandlerParse<T>.Fail("cannot be read: " + ex.Message);
        }
    }

    /// <summary>The document as UTF-8 JSON with no byte-order mark.</summary>
    public static byte[] ToUtf8<T>(T document) where T : class, IHandlerDocument =>
        JsonSerializer.SerializeToUtf8Bytes(document, Options);

    public static string ToJson<T>(T document) where T : class, IHandlerDocument =>
        JsonSerializer.Serialize(document, Options);

    /// <summary>Times are always UTC and written as <c>yyyy-MM-ddTHH:mm:ss.fffffffZ</c>.</summary>
    private static string Format(DateTime value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private sealed class UtcDateTimeConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String || !TryParse(reader.GetString(), out var value))
                throw new JsonException("not an ISO 8601 time");
            return value;
        }

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
            writer.WriteStringValue(Format(value));
    }

    private sealed class UtcNullableDateTimeConverter : JsonConverter<DateTime?>
    {
        public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
                return null;
            if (reader.TokenType != JsonTokenType.String || !TryParse(reader.GetString(), out var value))
                throw new JsonException("not an ISO 8601 time");
            return value;
        }

        public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
        {
            if (value is { } v)
                writer.WriteStringValue(Format(v));
            else
                writer.WriteNullValue();
        }
    }

    // A time with no offset is read as UTC: the protocol is UTC throughout.
    private static bool TryParse(string? text, out DateTime value) =>
        DateTime.TryParse(text, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out value);
}
