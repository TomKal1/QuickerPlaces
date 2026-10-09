using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using QuickerPlaces.Models.Sessions;
using QuickerPlaces.Services.Activity;
using QuickerPlaces.Services.Documents;

namespace QuickerPlaces.Services.Sessions;

/// <summary>
/// Writes and reads .qpsession files (session sharing plan §3). A shared
/// file comes from someone else, so everything read is checked as untrusted:
/// the format and version, every path and address, and the size. What
/// cannot be used is dropped rather than refusing the whole file, unless
/// nothing usable is left.
///
/// UI-free and linked into the test project.
/// </summary>
public static class SharedSessionFormat
{
    /// <summary>The .qpsession schema version this build writes and reads.</summary>
    public const int CurrentSchemaVersion = 1;

    public const string Extension = ".qpsession";

    public const string FileDialogFilter = "Shared QuickerPlaces sessions (*.qpsession)|*.qpsession";

    /// <summary>A shared session is a short list of paths; anything bigger is not one.</summary>
    public const long MaxFileBytes = 4 * 1024 * 1024;

    /// <summary>The most files one shared session brings in.</summary>
    public const int MaxFiles = 1000;

    /// <summary>The name a shared session with none is given.</summary>
    public const string UnnamedSession = "Shared session";

    public const string NotASharedSessionMessage = "That file isn't a shared QuickerPlaces session.";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static string Serialize(SharedSessionDocument document)
    {
        document.Format = SharedSessionDocument.FormatName;
        document.SchemaVersion = CurrentSchemaVersion;
        return JsonSerializer.Serialize(document, JsonOptions);
    }

    /// <summary>
    /// Writes <paramref name="document"/> to <paramref name="filePath"/>
    /// through a temp file beside it, so writing over an earlier copy never
    /// leaves half a file. Returns null, or what went wrong for the user.
    /// </summary>
    public static string? Write(string filePath, SharedSessionDocument document)
    {
        var temp = filePath + ".tmp";
        try
        {
            File.WriteAllText(temp, Serialize(document));
            File.Move(temp, filePath, overwrite: true);
            return null;
        }
        catch (Exception ex)
        {
            try
            {
                File.Delete(temp);
            }
            catch (Exception)
            {
                // Best effort: the write already failed, and that is what's reported.
            }

            DiagnosticLog.Warn($"Writing a shared session failed ({ex.GetType().Name}).");
            return $"Couldn't save the shared session: {ex.Message}";
        }
    }

    /// <summary>Reads and checks the .qpsession at <paramref name="filePath"/>.</summary>
    public static SharedSessionReadResult Read(string filePath)
    {
        try
        {
            var info = new FileInfo(filePath);
            if (!info.Exists)
                return SharedSessionReadResult.Fail("That file no longer exists.");
            if (info.Length > MaxFileBytes)
                return SharedSessionReadResult.Fail(NotASharedSessionMessage);

            return Parse(File.ReadAllText(filePath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            DiagnosticLog.Warn($"Reading a shared session failed ({ex.GetType().Name}).");
            return SharedSessionReadResult.Fail($"Couldn't read that file: {ex.Message}");
        }
    }

    /// <summary>
    /// Checks a shared session's text. The name is trimmed and shortened to
    /// a session's limit, tags are cleaned as typed tags are, and each file
    /// keeps only a usable path, an https address and a \\server\share path;
    /// a file with neither a usable path nor an address is dropped, as are
    /// repeats.
    /// </summary>
    public static SharedSessionReadResult Parse(string json)
    {
        SharedSessionDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<SharedSessionDocument>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return SharedSessionReadResult.Fail(NotASharedSessionMessage);
        }

        if (document is null || !string.Equals(document.Format, SharedSessionDocument.FormatName, StringComparison.Ordinal))
            return SharedSessionReadResult.Fail(NotASharedSessionMessage);
        if (document.SchemaVersion > CurrentSchemaVersion)
            return SharedSessionReadResult.Fail("That session was shared from a newer version of QuickerPlaces. Update QuickerPlaces to open it.");
        if (document.SchemaVersion < 1)
            return SharedSessionReadResult.Fail(NotASharedSessionMessage);

        var name = (document.Name ?? "").Trim();
        if (name.Length == 0)
            name = UnnamedSession;
        if (name.Length > SessionStore.MaxNameLength)
            name = name[..SessionStore.MaxNameLength].TrimEnd();

        var tags = SessionStore.ParseTags(string.Join(",", (document.Tags ?? new List<string>()).Where(t => t is not null)))
            .Where(t => t.Length <= SessionStore.MaxTagLength)
            .Take(SessionStore.MaxTags)
            .ToList();

        var files = new List<SharedSessionFile>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in document.Files ?? new List<SharedSessionFile>())
        {
            if (file is null)
                continue;

            var path = DocumentPaths.Normalize(file.Path) ?? "";
            var url = CloudPaths.Canonical(file.Url);
            var network = file.NetworkPath is { } unc && unc.TrimStart().StartsWith(@"\\", StringComparison.Ordinal)
                ? DocumentPaths.Normalize(unc)
                : null;
            if (path.Length == 0 && url is null)
                continue;
            if (path.Length == 0 && DocumentKinds.FromPath(CloudPaths.FileName(url)) is null)
                continue;

            var key = path.Length > 0 ? path : url!;
            if (!seen.Add(key))
                continue;

            files.Add(new SharedSessionFile
            {
                Path = path,
                Location = Enum.IsDefined(file.Location) ? file.Location : SharedFileLocation.ThisPc,
                Url = url,
                NetworkPath = network,
            });
            if (files.Count == MaxFiles)
                break;
        }

        if (files.Count == 0)
            return SharedSessionReadResult.Fail("That shared session lists no PDF, Word or Excel files QuickerPlaces can use.");

        return SharedSessionReadResult.Ok(new SharedSessionDocument
        {
            Format = SharedSessionDocument.FormatName,
            SchemaVersion = document.SchemaVersion,
            SharedAt = document.SharedAt,
            Name = name,
            Tags = tags,
            Files = files,
        });
    }

    /// <summary>
    /// A file name for <paramref name="sessionName"/>: the name with the
    /// characters Windows refuses in a file name replaced, then .qpsession.
    /// </summary>
    public static string SuggestedFileName(string sessionName)
    {
        var invalid = new HashSet<char>("<>:\"/\\|?*");
        var chars = (sessionName ?? "").Trim().Select(c => c < 32 || invalid.Contains(c) ? '-' : c).ToArray();
        var stem = new string(chars).Trim(' ', '.');
        return (stem.Length == 0 ? "Session" : stem) + Extension;
    }
}

/// <summary>What <see cref="SharedSessionFormat.Read"/> found: a checked document, or why there is none.</summary>
public sealed record SharedSessionReadResult(SharedSessionDocument? Document, string? ErrorMessage)
{
    public static SharedSessionReadResult Ok(SharedSessionDocument document) => new(document, null);

    public static SharedSessionReadResult Fail(string message) => new(null, message);
}
