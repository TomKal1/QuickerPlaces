namespace QuickerPlaces.Services;

/// <summary>
/// The alias the Add dialog suggests for a destination, so the common case
/// needs no typing. UI-free and linked into the test project. Phase 4's file
/// places will add the file name without its extension here (roadmap §4.16).
/// </summary>
public static class AliasSuggestion
{
    private static readonly char[] Separators = { '\\', '/' };

    /// <summary>
    /// The deepest folder name in <paramref name="path"/>: "UFGS_M" for
    /// C:\Users\Thomas\Downloads\UFGS_M, "share" for \\server\share, and "C:"
    /// for a drive root. Trailing separators, surrounding spaces and the quotes
    /// that Explorer's "Copy as path" adds are ignored. Null when there is no
    /// name to suggest. Splits on both separators itself rather than using
    /// Path.GetFileName, which only knows '/' on Linux, where the tests also
    /// run.
    /// </summary>
    public static string? FromFolderPath(string? path)
    {
        var trimmed = path?.Trim().Trim('"').Trim().TrimEnd(Separators);
        if (string.IsNullOrEmpty(trimmed))
            return null;

        var name = trimmed[(trimmed.LastIndexOfAny(Separators) + 1)..];
        return name.Length > 0 ? name : null;
    }
}
