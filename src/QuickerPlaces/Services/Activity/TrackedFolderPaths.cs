using System;
using System.Collections.Generic;
using System.Linq;

namespace QuickerPlaces.Services.Activity;

/// <summary>
/// Where a path sits among Recents' tracked folders (Desk layout design §4):
/// how deep below a root it is, which root holds it, and the Recents
/// window's labels for the depth. Paths compare without case, as Windows
/// does. Pure logic; UI-free and linked into the test project.
/// </summary>
public static class TrackedFolderPaths
{
    /// <summary>The group for items no tracked folder holds.</summary>
    public const string NotTracked = "Not in a tracked folder";

    /// <summary>How many folders below <paramref name="rootPath"/> <paramref name="path"/> is: 0 for the root itself; null when it isn't inside it.</summary>
    public static int? LevelBelow(string rootPath, string path)
    {
        var root = rootPath.TrimEnd('\\', '/');
        if (root.Length == 0 || !path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            return null;
        if (path.Length > root.Length && path[root.Length] is not ('\\' or '/'))
            return null;

        var below = path[root.Length..].Trim('\\', '/');
        return below.Length == 0 ? 0 : below.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries).Length;
    }

    /// <summary>The innermost of <paramref name="rootPaths"/> that holds <paramref name="path"/>, or null.</summary>
    public static string? RootFor(IEnumerable<string> rootPaths, string path)
        => rootPaths
            .Where(root => LevelBelow(root, path) is not null)
            .OrderByDescending(root => root.TrimEnd('\\', '/').Length)
            .FirstOrDefault();

    /// <summary>"Root folder", "Level 1 · directly below root", "Level 2 · below root"…</summary>
    public static string LevelLabel(int level) => level switch
    {
        0 => "Root folder",
        1 => "Level 1 · directly below root",
        _ => $"Level {level} · below root",
    };
}
