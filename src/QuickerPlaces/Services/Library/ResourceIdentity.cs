using System;
using QuickerPlaces.Services.Activity;
using QuickerPlaces.Services.Documents;

namespace QuickerPlaces.Services.Library;

/// <summary>
/// When two mentions of a folder, link or document are the same thing
/// (configurable canvas plan §2, §4): folders compare as Recents compares
/// them (ignoring case, separators and a trailing slash), documents as
/// sessions compare them, and links as saved, trimmed, ignoring case.
///
/// The Library merges its sources on this key, the File shelf keeps a row
/// selected across refreshes by it, and collections (M6) store references
/// by it, so all three agree on what "the same file" means. Pure logic;
/// UI-free and linked into the test project.
/// </summary>
public static class ResourceIdentity
{
    /// <summary>Compares keys: ordinal, ignoring case, as Windows paths do.</summary>
    public static StringComparer Comparer => StringComparer.OrdinalIgnoreCase;

    /// <summary>"Kind|normalized location": equal (by <see cref="Comparer"/>) for the same resource.</summary>
    public static string Key(LibraryKind kind, string location) => $"{kind}|{Normalize(kind, location)}";

    /// <summary>The location as it is compared; the location itself, trimmed, when it can't be normalized.</summary>
    public static string Normalize(LibraryKind kind, string location) => kind switch
    {
        LibraryKind.Link => location.Trim(),
        LibraryKind.Folder => RootPathMatcher.Normalize(location) ?? location.Trim(),
        _ => DocumentPaths.Normalize(location) ?? location.Trim(),
    };

    public static bool Same(LibraryKind kind, string a, string b) => Comparer.Equals(Normalize(kind, a), Normalize(kind, b));
}
