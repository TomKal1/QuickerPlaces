using System;
using QuickerPlaces.Services.Activity;

namespace QuickerPlaces.Services.Documents;

/// <summary>
/// How project sessions and Recent Files spell and compare document paths
/// (sessions plan D4). Splits on both separators itself and never calls
/// Path.*, so the Windows paths the tests use behave the same on Linux
/// (Phase 9 hand-off §3).
///
/// UI-free and linked into the test project.
/// </summary>
public static class DocumentPaths
{
    /// <summary>
    /// <paramref name="path"/> spelled the one way a session or Recent Files
    /// keeps it (as RootPathMatcher spells a folder: backslashes, "." and
    /// ".." resolved, casing as given), or null when it is not a drive or UNC
    /// path to a PDF, Office, text, Revit or AutoCAD file (<see cref="DocumentKinds"/>).
    /// Surrounding quotes, as a command line or a copied path carries them,
    /// are removed first.
    /// </summary>
    public static string? Normalize(string? path)
    {
        var trimmed = path?.Trim().Trim('"').Trim();
        if (string.IsNullOrEmpty(trimmed) || DocumentKinds.FromPath(trimmed) is null)
            return null;

        var normalized = RootPathMatcher.Normalize(trimmed);
        if (normalized is null || DocumentKinds.FromPath(normalized) is null)
            return null;

        // A drive root or a bare share can't be a file: a file needs a segment past "C:" or "\\server\share".
        var segments = normalized.Split('\\', StringSplitOptions.RemoveEmptyEntries).Length;
        var head = normalized.StartsWith(@"\\", StringComparison.Ordinal) ? 2 : 1;
        var name = FileName(normalized);
        return segments > head && name.LastIndexOf('.') > 0 ? normalized : null;
    }

    /// <summary>True when <paramref name="a"/> and <paramref name="b"/> name the same file, ignoring case as Windows does.</summary>
    public static bool Same(string a, string b)
        => string.Equals(Normalize(a) ?? a, Normalize(b) ?? b, StringComparison.OrdinalIgnoreCase);

    /// <summary>The last segment of <paramref name="path"/>: "A-101.pdf" for "C:\Jobs\A-101.pdf".</summary>
    public static string FileName(string path)
    {
        var trimmed = path.TrimEnd('\\', '/');
        var cut = trimmed.LastIndexOfAny(new[] { '\\', '/' });
        return cut < 0 ? trimmed : trimmed[(cut + 1)..];
    }

    /// <summary>The file name without its extension: "Report" for "C:\Jobs\Report.docx".</summary>
    public static string Stem(string path)
    {
        var name = FileName(path);
        var dot = name.LastIndexOf('.');
        return dot > 0 ? name[..dot] : name;
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
}
