using System;
using QuickerPlaces.Services.Activity;

namespace QuickerPlaces.Services.Sessions;

/// <summary>
/// How project sessions spell and compare PDF paths (sessions plan D4).
/// Splits on both separators itself and never calls Path.*, so the Windows
/// paths the tests use behave the same on Linux (Phase 9 hand-off §3).
///
/// UI-free and linked into the test project.
/// </summary>
public static class SessionPaths
{
    /// <summary>
    /// <paramref name="path"/> spelled the one way a session keeps it (as
    /// RootPathMatcher spells a folder: backslashes, "." and ".." resolved,
    /// casing as given), or null when it is not a drive or UNC path to a
    /// file whose name ends in ".pdf". Surrounding quotes, as a command line
    /// or a copied path carries them, are removed first.
    /// </summary>
    public static string? NormalizePdf(string? path)
    {
        var trimmed = path?.Trim().Trim('"').Trim();
        if (string.IsNullOrEmpty(trimmed) || !IsPdfName(trimmed))
            return null;

        var normalized = RootPathMatcher.Normalize(trimmed);
        if (normalized is null || !IsPdfName(normalized))
            return null;

        // A drive root or a bare share can't be a file: a file needs a segment past "C:" or "\\server\share".
        var segments = normalized.Split('\\', StringSplitOptions.RemoveEmptyEntries).Length;
        var head = normalized.StartsWith(@"\\", StringComparison.Ordinal) ? 2 : 1;
        return segments > head && FileName(normalized).Length > ".pdf".Length ? normalized : null;
    }

    /// <summary>True when <paramref name="a"/> and <paramref name="b"/> name the same file, ignoring case as Windows does.</summary>
    public static bool Same(string a, string b)
        => string.Equals(NormalizePdf(a) ?? a, NormalizePdf(b) ?? b, StringComparison.OrdinalIgnoreCase);

    /// <summary>The last segment of <paramref name="path"/>: "A-101.pdf" for "C:\Jobs\A-101.pdf".</summary>
    public static string FileName(string path)
    {
        var trimmed = path.TrimEnd('\\', '/');
        var cut = trimmed.LastIndexOfAny(new[] { '\\', '/' });
        return cut < 0 ? trimmed : trimmed[(cut + 1)..];
    }

    /// <summary>Everything before the last segment: "C:\Jobs" for "C:\Jobs\A-101.pdf", or "" when there is none.</summary>
    public static string Folder(string path)
    {
        var trimmed = path.TrimEnd('\\', '/');
        var cut = trimmed.LastIndexOfAny(new[] { '\\', '/' });
        if (cut < 0)
            return "";

        // Keep a drive root's separator: "C:\", not "C:".
        return cut == 2 && trimmed[1] == ':' ? trimmed[..3] : trimmed[..cut];
    }

    private static bool IsPdfName(string path) => path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
}
