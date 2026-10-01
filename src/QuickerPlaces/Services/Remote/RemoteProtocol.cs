using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace QuickerPlaces.Services.Remote;

/// <summary>One operation for <see cref="StoreOperations"/>: its name and its arguments.</summary>
public sealed record OperationRequest(string Op, JsonObject Args)
{
    public JsonObject ToJson() => new()
    {
        ["protocol"] = RemoteProtocol.Version,
        ["op"] = Op,
        ["args"] = Args.DeepClone()
    };

    public static OperationRequest FromJson(JsonObject json)
    {
        if (json["protocol"]?.GetValue<int>() != RemoteProtocol.Version)
            throw new JsonException($"Unsupported protocol (expected {RemoteProtocol.Version}).");
        return new OperationRequest(
            json["op"]?.GetValue<string>() ?? throw new JsonException("Missing op."),
            json["args"] as JsonObject ?? new JsonObject());
    }
}

/// <summary>An operation's answer: data on success, or an error code and message.</summary>
public sealed record OperationReply(bool Ok, JsonObject? Data, string? Code, string? Message, JsonObject? Details)
{
    public static OperationReply Success(JsonObject data) => new(true, data, null, null, null);

    public static OperationReply Fail(string code, string message, JsonObject? details = null) => new(false, null, code, message, details);

    public JsonObject ToJson() => Ok
        ? new JsonObject { ["ok"] = true, ["data"] = Data?.DeepClone() }
        : new JsonObject { ["ok"] = false, ["error"] = new JsonObject { ["code"] = Code, ["message"] = Message, ["details"] = Details?.DeepClone() } };

    public static OperationReply FromJson(JsonObject json)
    {
        if (json["ok"]?.GetValue<bool>() == true)
            return Success(json["data"] as JsonObject ?? new JsonObject());

        var error = json["error"] as JsonObject ?? throw new JsonException("A failed reply has no error.");
        return Fail(error["code"]?.GetValue<string>() ?? "internal", error["message"]?.GetValue<string>() ?? "", error["details"] as JsonObject);
    }
}

/// <summary>
/// How qp talks to the running app: a named pipe only the same Windows user
/// can open (PipeOptions.CurrentUserOnly), one request per connection, each
/// side sending one line of JSON. The name carries the user and the data
/// root, so two users, or a copy on test data, each get their own.
/// </summary>
public static class RemoteProtocol
{
    /// <summary>Raised only for a change an older app or qp couldn't understand.</summary>
    public const int Version = 1;

    /// <summary>The longest line either side reads, so a stray client can't make the app buffer without end.</summary>
    public const int MaxLineLength = 1 << 20;

    public static string PipeName(string? scope)
    {
        var user = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Environment.UserDomainName + "\\" + Environment.UserName)), 0, 6);
        return scope is null ? $"{AppInfo.Publisher}.{AppInfo.Name}.qp.{user}" : $"{AppInfo.Publisher}.{AppInfo.Name}.qp.{user}.{scope}";
    }

    public static void WriteLine(Stream stream, JsonObject json)
    {
        var bytes = Encoding.UTF8.GetBytes(json.ToJsonString() + "\n");
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush();
    }

    /// <summary>
    /// One line of JSON from <paramref name="stream"/>, or null when the other
    /// side hangs up before sending anything. Cancelled by <paramref name="cancel"/>,
    /// so a stalled peer can't hold the reader forever. Each connection carries
    /// one message, so bytes after the newline are ignored.
    /// </summary>
    public static async Task<JsonObject?> ReadLineAsync(Stream stream, CancellationToken cancel)
    {
        var buffer = new MemoryStream();
        var chunk = new byte[4096];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancel).ConfigureAwait(false);
            if (read == 0)
            {
                if (buffer.Length == 0)
                    return null;
                break;
            }

            var newline = Array.IndexOf(chunk, (byte)'\n', 0, read);
            buffer.Write(chunk, 0, newline >= 0 ? newline : read);
            if (buffer.Length > MaxLineLength)
                throw new InvalidDataException("The message is too long.");
            if (newline >= 0)
                break;
        }

        return JsonNode.Parse(buffer.ToArray()) as JsonObject ?? throw new JsonException("Expected a JSON object.");
    }
}
