using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using QuickerPlaces.Services.Documents;

namespace QuickerPlaces.Models.RecentFiles;

/// <summary>
/// The root of recent-files.json (documents plan §5): whether Recent Files
/// tracking is on and what it covers, and what it has recorded. A
/// machine-local file of its own, like activity.json, never part of
/// places.json, sessions.json or an export.
///
/// Version 2 (history plan §6) keeps it to what activity.json keeps for
/// folders: each open for 62 days, a count per day for a year (for the
/// year strip), and per file its last open and how many there were (for
/// the list with no period). Everything older is in the activity history.
/// Version 1 kept every open for a year; it is read and converted at load.
/// </summary>
public sealed class RecentFilesDocument
{
    /// <summary>RecentFilesStore sets this from its CurrentSchemaVersion on every write and checks it on every load.</summary>
    public int SchemaVersion { get; set; } = 1;

    public RecentFilesSettings Settings { get; set; } = new();

    public List<RecentFileRecord> Files { get; set; } = new();

    /// <summary>Opens and files per kind per local day, kept a year. Empty in a version 1 file, which the store fills from its opens.</summary>
    public Dictionary<DateOnly, RecentFilesDay> Days { get; set; } = new();
}

/// <summary>Recent Files tracking's switches. Off until the user turns it on.</summary>
public sealed class RecentFilesSettings
{
    public bool Enabled { get; set; }

    /// <summary>The kinds recorded. All three by default.</summary>
    public List<DocumentKind> Kinds { get; set; } = new(DocumentKinds.All);

    public RecentFilesScope Scope { get; set; } = RecentFilesScope.TrackedFolders;

    /// <summary>When tracking was first turned on: the year view shows no history before it.</summary>
    public DateTimeOffset? TrackingStartedAt { get; set; }

    /// <summary>When tracking was last turned on. Opens before it — while it was off — are never recorded.</summary>
    public DateTimeOffset? ResumedAt { get; set; }
}

/// <summary>Where Recent Files looks.</summary>
public enum RecentFilesScope
{
    /// <summary>Only files under a folder tracked in Recents (the default, and the same consent as folder tracking).</summary>
    TrackedFolders,

    /// <summary>Files anywhere.</summary>
    Everywhere,
}

/// <summary>One file: its recent opens, its last open and how many it has had.</summary>
public sealed class RecentFileRecord
{
    public string Path { get; set; } = "";

    /// <summary>Each recorded open, oldest first, in UTC: the last 62 days (a year in version 1).</summary>
    public List<DateTimeOffset> Opens { get; set; } = new();

    /// <summary>The last recorded open, in UTC, kept after the open itself ages out of <see cref="Opens"/>.</summary>
    public DateTimeOffset? LastOpenedAt { get; set; }

    /// <summary>How many opens have been recorded while the file stayed in the list (it leaves a year after its last open).</summary>
    public int OpenCount { get; set; }
}

/// <summary>One local day's opens per kind. Short names: one of these per day for a year.</summary>
public sealed class RecentFilesDay
{
    [JsonPropertyName("pdf")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RecentFilesKindCount? Pdf { get; set; }

    [JsonPropertyName("word")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RecentFilesKindCount? Word { get; set; }

    [JsonPropertyName("excel")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RecentFilesKindCount? Excel { get; set; }

    /// <summary>The count for <paramref name="kind"/>, or null when nothing of that kind was opened.</summary>
    public RecentFilesKindCount? Get(DocumentKind kind) => kind switch
    {
        DocumentKind.Pdf => Pdf,
        DocumentKind.Word => Word,
        _ => Excel,
    };

    /// <summary>Adds <paramref name="opens"/> opens of <paramref name="files"/> more distinct files of <paramref name="kind"/>.</summary>
    public void Add(DocumentKind kind, int opens, int files)
    {
        var count = Get(kind) ?? new RecentFilesKindCount();
        count.Opens += opens;
        count.Files += files;
        switch (kind)
        {
            case DocumentKind.Pdf: Pdf = count; break;
            case DocumentKind.Word: Word = count; break;
            default: Excel = count; break;
        }
    }
}

/// <summary>A day's opens of one kind, and how many distinct files they were.</summary>
public sealed class RecentFilesKindCount
{
    [JsonPropertyName("o")]
    public int Opens { get; set; }

    [JsonPropertyName("f")]
    public int Files { get; set; }
}
