using System;
using System.Collections.Generic;
using QuickerPlaces.Services.Documents;
using QuickerPlaces.Services.Library;

namespace QuickerPlaces.Services.Sessions;

/// <summary>
/// The documents the File shelf lists, ready to review as a new session
/// (configurable canvas plan M3): PDF, Office, text, Revit and AutoCAD files only, each once,
/// in the shelf's order. Folders and links are counted and left out, because
/// a session is a set of documents; the review step then decides what is
/// saved. Pure logic; UI-free and linked into the test project.
/// </summary>
public sealed record SessionFileSet(IReadOnlyList<string> Files, int FoldersLeftOut, int LinksLeftOut)
{
    public bool IsEmpty => Files.Count == 0;

    /// <summary>The documents among <paramref name="items"/>, each once by the sessions' own path rules.</summary>
    public static SessionFileSet From(IEnumerable<LibraryItem> items)
    {
        var files = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var folders = 0;
        var links = 0;

        foreach (var item in items)
        {
            switch (item.Kind)
            {
                case LibraryKind.Folder:
                    folders++;
                    continue;
                case LibraryKind.Link:
                    links++;
                    continue;
            }

            if (DocumentPaths.Normalize(item.Location) is { } path && seen.Add(path))
                files.Add(path);
        }

        return new SessionFileSet(files, folders, links);
    }

    /// <summary>What the review dialog says about where the list came from.</summary>
    public string Summary
    {
        get
        {
            var lead = Files.Count == 1
                ? "1 file from the File shelf. Check it, then Save."
                : $"{Files.Count} files from the File shelf. Untick any you don't want, then Save.";
            var left = (FoldersLeftOut, LinksLeftOut) switch
            {
                (0, 0) => null,
                (_, 0) => Count(FoldersLeftOut, "folder"),
                (0, _) => Count(LinksLeftOut, "link"),
                _ => $"{Count(FoldersLeftOut, "folder")} and {Count(LinksLeftOut, "link")}",
            };
            return left is null ? lead : $"{lead}\nLeft out: {left}. Sessions hold PDF, Office, text, Revit and AutoCAD files.";
        }
    }

    /// <summary>Why nothing can be saved, when the shelf lists no documents.</summary>
    public const string NothingToSave = "The File shelf lists no PDF, Office, text, Revit or AutoCAD files to save as a session. Choose another period or clear a filter.";

    private static string Count(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";
}
