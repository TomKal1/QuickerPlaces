using System;
using System.Collections.Generic;
using System.Linq;
using QuickerPlaces.Models;
using QuickerPlaces.Services.Activity;
using QuickerPlaces.Services.Documents;
using QuickerPlaces.Services.RecentFiles;
using QuickerPlaces.Services.Sessions;

namespace QuickerPlaces.Services.Library;

/// <summary>What a Library item is: the five kinds the Library splits by.</summary>
public enum LibraryKind
{
    Folder,
    Link,
    Pdf,
    Word,
    Excel,
}

public static class LibraryKinds
{
    public static IReadOnlyList<LibraryKind> All { get; } = new[] { LibraryKind.Folder, LibraryKind.Link, LibraryKind.Pdf, LibraryKind.Word, LibraryKind.Excel };

    public static LibraryKind From(DocumentKind kind) => kind switch
    {
        DocumentKind.Pdf => LibraryKind.Pdf,
        DocumentKind.Word => LibraryKind.Word,
        _ => LibraryKind.Excel,
    };

    /// <summary>The document kind, or null for a folder or link.</summary>
    public static DocumentKind? ToDocumentKind(this LibraryKind kind) => kind switch
    {
        LibraryKind.Pdf => DocumentKind.Pdf,
        LibraryKind.Word => DocumentKind.Word,
        LibraryKind.Excel => DocumentKind.Excel,
        _ => null,
    };

    /// <summary>"Folders", "Links", "PDFs", "Word", "Excel": filter chips and group headings.</summary>
    public static string PluralLabel(this LibraryKind kind) => kind switch
    {
        LibraryKind.Folder => "Folders",
        LibraryKind.Link => "Links",
        LibraryKind.Pdf => "PDFs",
        LibraryKind.Word => "Word",
        _ => "Excel",
    };

    /// <summary>"Folder", "Link", "PDF", "Word", "Excel": the Type column.</summary>
    public static string Label(this LibraryKind kind) => kind switch
    {
        LibraryKind.Folder => "Folder",
        LibraryKind.Link => "Link",
        LibraryKind.Pdf => "PDF",
        LibraryKind.Word => "Word",
        _ => "Excel",
    };
}

/// <summary>
/// One thing in the Library (documents plan §6): a folder, link or document,
/// with everything the four sources say about it merged into one row —
/// saved as a place, saved in sessions (and their tags), visited in Recents,
/// opened in Recent Files.
/// </summary>
/// <param name="Kind">What it is.</param>
/// <param name="Name">A saved place's alias, otherwise the folder's or file's own name, or the link.</param>
/// <param name="Location">The full path or URL.</param>
/// <param name="Place">The saved place, when it is one: opening it then counts as a place open.</param>
/// <param name="Sessions">The names of the sessions that hold it.</param>
/// <param name="Tags">Its sessions' tags, each once.</param>
/// <param name="RecentCount">Visits (folders) or opens (files) in the period, 0 if none were recorded.</param>
/// <param name="LastUsedAt">The latest of: place last opened, session last used, last visited or opened.</param>
public sealed record LibraryItem(
    LibraryKind Kind,
    string Name,
    string Location,
    Place? Place,
    IReadOnlyList<string> Sessions,
    IReadOnlyList<string> Tags,
    int RecentCount,
    DateTimeOffset? LastUsedAt)
{
    /// <summary>Who this item is, whichever source named it (<see cref="ResourceIdentity"/>): stable across refreshes.</summary>
    public string Key => ResourceIdentity.Key(Kind, Location);

    public bool IsSavedPlace => Place is not null;

    public bool IsInSession => Sessions.Count > 0;

    /// <summary>Saved as a place or in a session.</summary>
    public bool IsSaved => IsSavedPlace || IsInSession;

    /// <summary>Recorded by Recents (folders) or Recent Files (documents).</summary>
    public bool IsRecent => RecentCount > 0;

    /// <summary>The folder a file or folder is in; "" for a link.</summary>
    public string Folder => Kind == LibraryKind.Link ? "" : DocumentPaths.Folder(Location);

    /// <summary>Time spent in a folder in the period (Recents); zero for anything else.</summary>
    public TimeSpan RecentTime { get; init; }

    /// <summary>What places it among tracked folders: a folder itself, or the folder a file is in; "" for a link.</summary>
    public string TreePath => Kind == LibraryKind.Folder ? Location : Folder;

    /// <summary>"Saved place · In Tower B, Admin · Opened 3 times", for the Where column.</summary>
    public string SourceText
    {
        get
        {
            var parts = new List<string>();
            if (IsSavedPlace)
                parts.Add("Saved place");
            if (IsInSession)
                parts.Add($"In {string.Join(", ", Sessions)}");
            if (IsRecent)
            {
                var verb = Kind == LibraryKind.Folder ? "Visited" : "Opened";
                parts.Add(RecentCount == 1 ? $"{verb} once" : $"{verb} {RecentCount} times");
            }

            return string.Join(" · ", parts);
        }
    }

    /// <summary>
    /// "Saved place · In Tower B, Admin · Recent", for the Where from column: which
    /// sources know the item. The count of opens or visits has its own Opens column.
    /// </summary>
    public string WhereFromText
    {
        get
        {
            var parts = new List<string>();
            if (IsSavedPlace)
                parts.Add("Saved place");
            if (IsInSession)
                parts.Add($"In {string.Join(", ", Sessions)}");
            if (IsRecent)
                parts.Add("Recent");

            return string.Join(" · ", parts);
        }
    }
}

/// <summary>
/// Merges saved places, saved sessions, Recents' folders and Recent Files
/// into Library items (documents plan §6). The four stay separate stores
/// with separate switches; this only reads them. One row per thing: the
/// same folder saved as a place and visited in Recents is one row, and a
/// file in two sessions and in Recent Files is one row with both sessions'
/// tags. What counts as the same thing is <see cref="ResourceIdentity"/>.
///
/// Pure logic; UI-free and linked into the test project.
/// </summary>
public static class LibraryIndex
{
    public static IReadOnlyList<LibraryItem> Build(
        IEnumerable<Place> places,
        IEnumerable<SessionSnapshot> sessions,
        IEnumerable<FolderActivity> recentFolders,
        IEnumerable<RecentFileSummary> recentFiles)
    {
        var items = new Dictionary<string, Builder>(ResourceIdentity.Comparer);

        Builder Get(LibraryKind kind, string location)
        {
            var key = ResourceIdentity.Key(kind, location);
            if (!items.TryGetValue(key, out var builder))
                items[key] = builder = new Builder(kind, location);
            return builder;
        }

        foreach (var place in places)
        {
            var item = Get(place.Type == PlaceType.Folder ? LibraryKind.Folder : LibraryKind.Link, place.Resource);
            item.Place = place;
            item.Use(place.LastOpenedAt);
        }

        foreach (var session in sessions)
        {
            foreach (var file in session.Files)
            {
                if (DocumentKinds.FromPath(file) is not { } kind)
                    continue;

                var item = Get(LibraryKinds.From(kind), file);
                item.AddSession(session.Name, session.Tags);
                item.Use(session.LastUsedAt);
            }
        }

        foreach (var folder in recentFolders)
        {
            var item = Get(LibraryKind.Folder, folder.Folder);
            item.RecentCount += Math.Max(1, folder.Visits);
            item.RecentTime += folder.Time;
            item.Use(folder.LastVisited);
        }

        foreach (var file in recentFiles)
        {
            var item = Get(LibraryKinds.From(file.Kind), file.Path);
            item.RecentCount += file.Opens;
            item.Use(file.LastOpenedAt);
        }

        return items.Values
            .Select(b => b.Build())
            .OrderByDescending(i => i.LastUsedAt ?? DateTimeOffset.MinValue)
            .ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>True when <paramref name="item"/> passes <paramref name="search"/>: every word in its name, location, tags or sessions.</summary>
    public static bool Matches(LibraryItem item, string? search)
    {
        var words = (search ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return words.All(word =>
            item.Name.Contains(word, StringComparison.CurrentCultureIgnoreCase) ||
            item.Location.Contains(word, StringComparison.CurrentCultureIgnoreCase) ||
            item.Tags.Any(t => t.Contains(word, StringComparison.CurrentCultureIgnoreCase)) ||
            item.Sessions.Any(s => s.Contains(word, StringComparison.CurrentCultureIgnoreCase)));
    }

    private sealed class Builder
    {
        private readonly List<string> _sessions = new();
        private readonly List<string> _tags = new();

        public Builder(LibraryKind kind, string location)
        {
            Kind = kind;
            Location = location;
        }

        public LibraryKind Kind { get; }
        public string Location { get; }
        public Place? Place { get; set; }
        public int RecentCount { get; set; }
        public TimeSpan RecentTime { get; set; }
        public DateTimeOffset? LastUsedAt { get; private set; }

        public void Use(DateTimeOffset? at)
        {
            if (at is { } instant && (LastUsedAt is null || instant > LastUsedAt))
                LastUsedAt = instant;
        }

        public void AddSession(string name, IEnumerable<string> tags)
        {
            if (!_sessions.Contains(name, StringComparer.OrdinalIgnoreCase))
                _sessions.Add(name);
            foreach (var tag in tags)
            {
                if (!_tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
                    _tags.Add(tag);
            }
        }

        public LibraryItem Build()
        {
            // A saved place keeps its location as saved; otherwise the first spelling seen.
            var location = Place?.Resource ?? Location;
            var name = Place?.Alias ?? (Kind == LibraryKind.Link ? location : DocumentPaths.FileName(location));
            return new LibraryItem(Kind, name, location, Place, _sessions.ToArray(), _tags.ToArray(), RecentCount, LastUsedAt)
            {
                RecentTime = RecentTime,
            };
        }
    }
}
