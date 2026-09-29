using System;
using System.Collections.Generic;
using QuickerPlaces.Services.Documents;

namespace QuickerPlaces.Models.RecentFiles;

/// <summary>
/// The root of recent-files.json (documents plan §5): whether Recent Files
/// tracking is on and what it covers, and every open it has recorded. A
/// machine-local file of its own, like activity.json, never part of
/// places.json, sessions.json or an export.
/// </summary>
public sealed class RecentFilesDocument
{
    /// <summary>RecentFilesStore sets this from its CurrentSchemaVersion on every write and checks it on every load.</summary>
    public int SchemaVersion { get; set; } = 1;

    public RecentFilesSettings Settings { get; set; } = new();

    public List<RecentFileRecord> Files { get; set; } = new();
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

/// <summary>One file and the times it was seen opened.</summary>
public sealed class RecentFileRecord
{
    public string Path { get; set; } = "";

    /// <summary>Each recorded open, oldest first, in UTC.</summary>
    public List<DateTimeOffset> Opens { get; set; } = new();
}
