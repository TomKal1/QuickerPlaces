using System;
using System.Collections.Generic;
using QuickerPlaces.Models.Sessions;
using QuickerPlaces.Services.Activity;
using QuickerPlaces.Services.Documents;

namespace QuickerPlaces.Services.Sessions;

/// <summary>
/// The decisions behind sharing a session (session sharing plan §4, §5): on
/// the sender's PC, where each file lives and how another PC could reach
/// it; on the recipient's, where each shared file is on this PC, if
/// anywhere. Checks for files go through <see cref="IShell"/>, so the tests
/// never touch a disk.
///
/// UI-free and linked into the test project.
/// </summary>
public static class SessionSharing
{
    /// <summary>
    /// How <paramref name="path"/> is shared: its web address when a synced
    /// OneDrive or SharePoint folder holds it, its network path when it is on
    /// a share or a mapped drive, or neither when it is only on this PC.
    /// </summary>
    public static SharedSessionFile Describe(string path, IReadOnlyList<CloudSyncRoot> roots, INetworkDriveResolver? network)
    {
        var file = new SharedSessionFile { Path = path, Location = SharedFileLocation.ThisPc };

        if (CloudPaths.ToUrl(path, roots, out var root) is { } url)
        {
            file.Url = url;
            file.Location = root!.Kind == CloudLibraryKind.Library ? SharedFileLocation.CloudLibrary : SharedFileLocation.PersonalCloud;
            return file;
        }

        if (path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            file.NetworkPath = path;
            file.Location = SharedFileLocation.Network;
            return file;
        }

        string? unc = null;
        try
        {
            unc = network?.GetNetworkPath(path);
        }
        catch (Exception ex)
        {
            // A drive that can't be asked about is treated as local; the type only, never the path.
            DiagnosticLog.Warn($"Looking up a mapped drive for sharing failed ({ex.GetType().Name}).");
        }

        if (DocumentPaths.Normalize(unc) is { } networkPath && networkPath.StartsWith(@"\\", StringComparison.Ordinal))
        {
            file.NetworkPath = networkPath;
            file.Location = SharedFileLocation.Network;
        }

        return file;
    }

    /// <summary>
    /// Where <paramref name="file"/> is on this PC, tried in this order: the
    /// same path as on the sender's PC (a local or mapped drive, never a
    /// network share); the recipient's own synced copy of its library; then
    /// its network path, only when <paramref name="checkNetwork"/> is true,
    /// because looking a server up contacts it, and the server is named by
    /// the file's sender. What is left is online only when it has a web
    /// address, and otherwise missing.
    /// </summary>
    public static SharedFileMatch Resolve(SharedSessionFile file, IShell shell, IReadOnlyList<CloudSyncRoot> roots, bool checkNetwork)
    {
        var path = DocumentPaths.Normalize(file.Path);
        if (path is not null && !path.StartsWith(@"\\", StringComparison.Ordinal) && Exists(shell, path))
            return new SharedFileMatch(SharedFileStatus.SamePath, path);

        var syncedPath = CloudPaths.ToLocal(file.Url, roots);
        if (syncedPath is not null && DocumentPaths.Normalize(syncedPath) is { } synced && Exists(shell, synced))
            return new SharedFileMatch(SharedFileStatus.SyncedLibrary, synced);

        var unc = file.NetworkPath ?? (path is not null && path.StartsWith(@"\\", StringComparison.Ordinal) ? path : null);
        if (unc is not null)
        {
            if (!checkNetwork)
                return new SharedFileMatch(SharedFileStatus.NetworkNotChecked, null);
            if (Exists(shell, unc))
                return new SharedFileMatch(SharedFileStatus.Network, unc);
        }

        if (CloudPaths.IsWebUrl(file.Url))
            return new SharedFileMatch(syncedPath is null ? SharedFileStatus.OnlineOnly : SharedFileStatus.NotInSyncedLibrary, null);

        return new SharedFileMatch(SharedFileStatus.Missing, null);
    }

    /// <summary>The \\server\share path a shared file would be looked for at, or null.</summary>
    public static string? NetworkPathOf(SharedSessionFile file)
        => file.NetworkPath ?? (file.Path.StartsWith(@"\\", StringComparison.Ordinal) ? file.Path : null);

    /// <summary>The server of a \\server\share path: "files" for "\\files\projects\A.pdf".</summary>
    public static string ServerOf(string uncPath)
    {
        var rest = uncPath.TrimStart('\\');
        var cut = rest.IndexOf('\\');
        return cut < 0 ? rest : rest[..cut];
    }

    /// <summary>
    /// The folder swap that turns <paramref name="original"/> (the sender's
    /// path) into <paramref name="chosen"/> (where the recipient found the
    /// file), keeping the folders the two paths end with alike: from
    /// "C:\Users\alice\Jobs\Tower B\A.pdf" and "D:\Work\Jobs\Tower B\A.pdf",
    /// "C:\Users\alice" becomes "D:\Work". Applied to the session's other
    /// files, one Locate finds the rest. Null when the two are the same
    /// place or share nothing to keep.
    /// </summary>
    public static FolderSwap? SwapBetween(string original, string chosen)
    {
        var a = DocumentPaths.Normalize(original);
        var b = DocumentPaths.Normalize(chosen);
        if (a is null || b is null)
            return null;

        a = DocumentPaths.Folder(a);
        b = DocumentPaths.Folder(b);
        while (true)
        {
            var parentA = DocumentPaths.Folder(a);
            var parentB = DocumentPaths.Folder(b);
            if (!IsRooted(parentA) || !IsRooted(parentB) ||
                !string.Equals(DocumentPaths.FileName(a), DocumentPaths.FileName(b), StringComparison.OrdinalIgnoreCase))
                break;

            a = parentA;
            b = parentB;
        }

        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase) ? null : new FolderSwap(a, b);
    }

    /// <summary>A path a drive root ("C:\") or a share ("\\server\share") or anything under one starts with.</summary>
    private static bool IsRooted(string folder)
    {
        if (folder.Length >= 3 && folder[1] == ':' && folder[2] == '\\')
            return true;

        return folder.StartsWith(@"\\", StringComparison.Ordinal) &&
               folder[2..].Split('\\', StringSplitOptions.RemoveEmptyEntries).Length >= 2;
    }

    private static bool Exists(IShell shell, string path)
    {
        try
        {
            return shell.FileExists(path);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Warn($"Checking for a shared session's file failed ({ex.GetType().Name}).");
            return false;
        }
    }
}

/// <summary>Where a shared file was found on this PC, if anywhere.</summary>
/// <param name="LocalPath">The path to save in the session, or null when the file wasn't found.</param>
public sealed record SharedFileMatch(SharedFileStatus Status, string? LocalPath)
{
    public bool IsFound => LocalPath is not null;
}

/// <summary>How a shared file was, or wasn't, found on this PC.</summary>
public enum SharedFileStatus
{
    /// <summary>At the same path as on the sender's PC.</summary>
    SamePath,

    /// <summary>In this PC's own synced copy of the file's OneDrive or SharePoint library.</summary>
    SyncedLibrary,

    /// <summary>At its network path.</summary>
    Network,

    /// <summary>Where the user pointed with Locate, or by the folder swap one Locate taught.</summary>
    Located,

    /// <summary>On a network share that hasn't been looked at yet: the user is asked first.</summary>
    NetworkNotChecked,

    /// <summary>In a library this PC doesn't sync: it can be opened online, or the library synced.</summary>
    OnlineOnly,

    /// <summary>This PC syncs its library, but the file isn't in it (moved, renamed, or not downloaded yet).</summary>
    NotInSyncedLibrary,

    /// <summary>Nowhere this PC can reach.</summary>
    Missing,
}

/// <summary>One folder put in place of another: <see cref="From"/> and everything under it moves to <see cref="To"/>.</summary>
public sealed record FolderSwap(string From, string To)
{
    /// <summary><paramref name="path"/> with <see cref="From"/> swapped for <see cref="To"/>, or null when it isn't under From.</summary>
    public string? Apply(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return null;

        var from = From.TrimEnd('\\');
        if (string.Equals(path, from, StringComparison.OrdinalIgnoreCase))
            return To;
        if (!path.StartsWith(from + "\\", StringComparison.OrdinalIgnoreCase))
            return null;

        return DocumentPaths.Normalize(To.TrimEnd('\\') + path[from.Length..]);
    }
}
