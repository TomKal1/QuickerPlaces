using System;
using System.Collections.Generic;
using System.Linq;
using QuickerPlaces.Models.Activity;

namespace QuickerPlaces.Services.Activity;

/// <summary>
/// Which folder, if any, a path Explorer reports credits under a tracked
/// root (Phase 9 plan D8, D13, D15, D22). A pure function, shared by the
/// tracker and the Add Root flow, UI-free and linked into the test project.
///
/// Splits on both separators itself rather than using Path.*, which only
/// knows '/' on Linux, where the tests also run (as AliasSuggestion does).
/// </summary>
public static class RootPathMatcher
{
    private static readonly char[] Separators = { '\\', '/' };

    /// <summary>
    /// The folder <paramref name="observedPath"/> credits under
    /// <paramref name="root"/>, rolled up per the root's mode (D8), or null
    /// when the path is not a rooted filesystem path under the root or one of
    /// its equivalent prefixes (D13). Comparison ignores case, the separator
    /// used, repeated and trailing separators, and "." and ".." (D15). The
    /// result is always spelled with the root's own path followed by the rest
    /// as Explorer showed it, so one folder reached by two routes is one row
    /// (5.2, D22).
    /// </summary>
    public static string? Credit(string? observedPath, TrackedRootConfig root)
    {
        var observed = Parse(observedPath);
        var rootPath = Parse(root.Path);
        if (observed is null || rootPath is null)
            return null;

        var below = Below(observed, rootPath);
        foreach (var prefix in root.EquivalentPrefixes)
        {
            if (below is not null)
                break;
            if (Parse(prefix) is { } equivalent)
                below = Below(observed, equivalent);
        }

        if (below is null)
            return null;

        var keep = root.Rollup switch
        {
            RollupMode.Exact => below.Length,
            RollupMode.Depth => Math.Clamp(root.Depth, 1, Math.Max(1, below.Length)),
            _ => 1
        };

        return Join(rootPath, below.Take(keep));
    }

    /// <summary>
    /// A path split into segments. A drive path's first segment is the drive
    /// ("C:"); a UNC path's first two are the server and share.
    /// </summary>
    private sealed record ParsedPath(bool IsUnc, string[] Segments)
    {
        public int HeadLength => IsUnc ? 2 : 1;
    }

    private static ParsedPath? Parse(string? path)
    {
        var trimmed = path?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return null;

        // A colon anywhere but after a drive letter is a shell location
        // ("::{GUID}") or a URL ("ftp://", "file:///"), never a folder.
        var colon = trimmed.IndexOf(':');
        if (colon >= 0 && (colon != 1 || trimmed.IndexOf(':', 2) >= 0))
            return null;

        bool isUnc;
        List<string> segments;
        if (trimmed.Length >= 2 && IsSeparator(trimmed[0]) && IsSeparator(trimmed[1]))
        {
            isUnc = true;
            segments = Split(trimmed);
            // A share needs a server and a share name; "\\?\" and "\\.\" are device paths.
            if (segments.Count < 2 || segments[0] is "?" or ".")
                return null;
        }
        else if (trimmed.Length >= 3 && char.IsAsciiLetter(trimmed[0]) && trimmed[1] == ':' && IsSeparator(trimmed[2]))
        {
            isUnc = false;
            segments = Split(trimmed);
        }
        else
        {
            // Relative ("Jobs\Acme"), rooted without a drive ("\Jobs"), or
            // drive-relative ("C:Jobs").
            return null;
        }

        var head = isUnc ? 2 : 1;
        var resolved = new List<string>(segments.Count);
        for (var i = 0; i < segments.Count; i++)
        {
            if (i >= head && segments[i] == ".")
                continue;
            if (i >= head && segments[i] == "..")
            {
                if (resolved.Count > head)
                    resolved.RemoveAt(resolved.Count - 1);
                continue;
            }

            resolved.Add(segments[i]);
        }

        return new ParsedPath(isUnc, resolved.ToArray());
    }

    /// <summary>The segments of <paramref name="path"/> below <paramref name="prefix"/>, or null when it is not under it.</summary>
    private static string[]? Below(ParsedPath path, ParsedPath prefix)
    {
        if (path.IsUnc != prefix.IsUnc || path.Segments.Length < prefix.Segments.Length)
            return null;

        for (var i = 0; i < prefix.Segments.Length; i++)
        {
            if (!string.Equals(path.Segments[i], prefix.Segments[i], StringComparison.OrdinalIgnoreCase))
                return null;
        }

        return path.Segments[prefix.Segments.Length..];
    }

    private static string Join(ParsedPath root, IEnumerable<string> below)
    {
        var segments = root.Segments.Concat(below).ToList();
        var head = root.IsUnc
            ? @"\\" + segments[0] + @"\" + segments[1]
            : segments[0];

        // "J:" alone means "the current directory on J:", so a drive root keeps its separator.
        if (segments.Count == root.HeadLength)
            return root.IsUnc ? head : head + @"\";

        return head + @"\" + string.Join(@"\", segments.Skip(root.HeadLength));
    }

    private static List<string> Split(string path)
        => path.Split(Separators, StringSplitOptions.RemoveEmptyEntries).ToList();

    private static bool IsSeparator(char c) => c is '\\' or '/';
}
