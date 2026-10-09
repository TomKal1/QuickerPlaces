using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace QuickerPlaces.Services.Revit.Handlers;

/// <summary>
/// The protocol folder (docs/revit-handler-protocol.md, "Folder layout"):
/// where each kind of file lives, and the one way files are written. The
/// default root is %LocalAppData%\QuickerPlaces\revit; tests, and a Revit
/// launched with QUICKERPLACES_REVIT_ROOT, use another.
///
/// The path helpers refuse a handler id, release or request id that breaks
/// the protocol's rules, so a value read from a file can never walk out of
/// the folder. UI-free and linked into the test project and the qp CLI.
/// </summary>
public sealed class RevitProtocolFolder
{
    public RevitProtocolFolder(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        Root = root;
    }

    /// <summary>%LocalAppData%\QuickerPlaces\revit.</summary>
    public static RevitProtocolFolder Default() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QuickerPlaces", "revit"));

    public string Root { get; }

    public string HandlersFolder => Path.Combine(Root, "handlers");

    public string InstancesFolder => Path.Combine(Root, "instances");

    public string RequestsRoot => Path.Combine(Root, "requests");

    /// <summary><c>requests\&lt;release&gt;\&lt;handlerId&gt;</c>: the only folder a handler reads requests from.</summary>
    public string RequestFolder(string release, string handlerId)
    {
        Require(HandlerProtocol.IsValidRelease(release), nameof(release));
        Require(HandlerProtocol.IsValidHandlerId(handlerId), nameof(handlerId));
        return Path.Combine(RequestsRoot, release, handlerId);
    }

    public string RequestFile(string release, string handlerId, string requestId) =>
        Path.Combine(RequestFolder(release, handlerId), ValidId(requestId) + ".json");

    public string ResultFile(string release, string handlerId, string requestId) =>
        Path.Combine(RequestFolder(release, handlerId), ValidId(requestId) + ".result.json");

    public string ClaimedFile(string release, string handlerId, string requestId, int processId) =>
        Path.Combine(RequestFolder(release, handlerId), ValidId(requestId) + ".claimed-" + processId);

    /// <summary>The name a registration is written under: <c>&lt;handlerId&gt;-&lt;release&gt;.json</c>.</summary>
    public string RegistrationFile(string release, string handlerId)
    {
        Require(HandlerProtocol.IsValidRelease(release), nameof(release));
        Require(HandlerProtocol.IsValidHandlerId(handlerId), nameof(handlerId));
        return Path.Combine(HandlersFolder, handlerId + "-" + release + ".json");
    }

    /// <summary>The name an instance is written under: <c>&lt;handlerId&gt;-&lt;release&gt;-&lt;processId&gt;.json</c>.</summary>
    public string InstanceFile(string release, string handlerId, int processId)
    {
        Require(HandlerProtocol.IsValidRelease(release), nameof(release));
        Require(HandlerProtocol.IsValidHandlerId(handlerId), nameof(handlerId));
        return Path.Combine(InstancesFolder, handlerId + "-" + release + "-" + processId + ".json");
    }

    /// <summary>True for names readers must ignore: a temporary file (a leading '.' or a trailing ".tmp").</summary>
    public static bool IsIgnorable(string fileName) =>
        fileName.StartsWith('.') || fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The <c>.json</c> files of <paramref name="folder"/> a reader should
    /// open, in name order; none when the folder does not exist or cannot be read.
    /// </summary>
    public static IReadOnlyList<string> JsonFiles(string folder)
    {
        try
        {
            return Directory.EnumerateFiles(folder)
                .Where(path =>
                {
                    var name = Path.GetFileName(path);
                    return !IsIgnorable(name) && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
                })
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    /// Writes <paramref name="content"/> to <paramref name="path"/> atomically:
    /// the whole file goes to <c>.&lt;name&gt;.&lt;32 hex&gt;.tmp</c> in the same
    /// folder, which is then renamed over the final name, so a reader sees
    /// the old file or the new one and never half of either. Creates the
    /// folder; on failure removes the temporary file and throws.
    /// </summary>
    public static void WriteAtomic(string path, byte[] content)
    {
        var folder = Path.GetDirectoryName(path) ?? throw new ArgumentException("path has no folder", nameof(path));
        Directory.CreateDirectory(folder);

        var temporary = Path.Combine(folder, "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllBytes(temporary, content);
            File.Move(temporary, path, overwrite: true);
        }
        catch
        {
            try
            {
                File.Delete(temporary);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Nothing more to do: the rename's own failure is the one to report.
            }
            throw;
        }
    }

    private static string ValidId(string requestId)
    {
        Require(HandlerProtocol.IsValidRequestId(requestId), nameof(requestId));
        return requestId;
    }

    private static void Require(bool ok, string name)
    {
        if (!ok)
            throw new ArgumentException("Not valid under the Revit handler protocol.", name);
    }
}
