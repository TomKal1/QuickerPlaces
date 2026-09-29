using System;
using System.Collections.Generic;
using System.Linq;
using QuickerPlaces.Services.Activity;

namespace QuickerPlaces.Services.Documents;

/// <summary>
/// Turns the paths Windows gives for files a program holds open
/// (GetFinalPathNameByHandle) into the paths a session keeps (held-files
/// plan H2, H3): the "\\?\" prefix removed, a share spelled with the
/// user's mapped drive letter when one points there, and anything that
/// isn't a document the user opened left out — other file types, Office's
/// "~$" owner files, and files in program, system and per-user application
/// folders, such as Revu's Studio cache.
///
/// Pure logic: WindowsHeldFiles gathers the raw paths. UI-free and linked
/// into the test project.
/// </summary>
public static class HeldFilePaths
{
    /// <summary>
    /// The held documents among <paramref name="raw"/>, each once, in the
    /// order found, with the first program that held it.
    /// <paramref name="mappedDrives"/> maps a drive ("P:") to its share
    /// ("\\files\projects"); <paramref name="excludedFolders"/> are folders
    /// whose files are never the user's documents.
    /// </summary>
    public static IReadOnlyList<HeldFile> Resolve(IEnumerable<(string FinalPath, string AppName)> raw,
        IReadOnlyDictionary<string, string> mappedDrives, IReadOnlyList<string> excludedFolders)
    {
        var excluded = excludedFolders
            .Select(RootPathMatcher.Normalize)
            .OfType<string>()
            .Select(f => f.TrimEnd('\\') + '\\')
            .ToList();

        var files = new List<HeldFile>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (finalPath, appName) in raw)
        {
            if (FromFinalPath(finalPath) is not { } plain)
                continue;
            if (DocumentPaths.Normalize(ToMappedDrive(plain, mappedDrives)) is not { } path)
                continue;
            if (DocumentPaths.FileName(path).StartsWith("~$", StringComparison.Ordinal))
                continue;
            if (excluded.Any(folder => path.StartsWith(folder, StringComparison.OrdinalIgnoreCase)))
                continue;

            if (seen.Add(path))
                files.Add(new HeldFile(path, appName));
        }

        return files;
    }

    /// <summary>
    /// "C:\Jobs\A-101.pdf" for "\\?\C:\Jobs\A-101.pdf", and
    /// "\\server\share\x.pdf" for "\\?\UNC\server\share\x.pdf". A path
    /// without the prefix is returned as it is; a volume or device path
    /// ("\\?\Volume{…}\…") is null, as it has no drive or share to keep.
    /// </summary>
    public static string? FromFinalPath(string? finalPath)
    {
        if (string.IsNullOrEmpty(finalPath))
            return null;

        if (finalPath.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
            return @"\\" + finalPath[8..];

        if (finalPath.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            var rest = finalPath[4..];
            return rest.Length >= 3 && char.IsAsciiLetter(rest[0]) && rest[1] == ':' && rest[2] == '\\' ? rest : null;
        }

        return finalPath;
    }

    /// <summary>
    /// <paramref name="path"/> with the longest mapped share it lies under
    /// replaced by that drive ("P:\Tower A\A-101.pdf"), or unchanged. When
    /// two drives map the same share, the first letter wins.
    /// </summary>
    public static string ToMappedDrive(string path, IReadOnlyDictionary<string, string> mappedDrives)
    {
        string? drive = null;
        var rootLength = -1;
        foreach (var (letter, remote) in mappedDrives.OrderBy(d => d.Key, StringComparer.OrdinalIgnoreCase))
        {
            var root = remote.TrimEnd('\\');
            if (root.Length <= rootLength)
                continue;
            if (path.Length > root.Length && path[root.Length] == '\\' && path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                drive = letter.TrimEnd('\\');
                rootLength = root.Length;
            }
        }

        return drive is null ? path : drive + path[rootLength..];
    }
}

/// <summary>A document a running program holds open, found from that program's file handles.</summary>
/// <param name="Path">The document's path, as a session keeps it.</param>
/// <param name="AppName">A readable name for the program that holds it ("Bluebeam Revu").</param>
public sealed record HeldFile(string Path, string AppName);
