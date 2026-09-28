using System;
using System.Collections.Generic;

namespace QuickerPlaces.Models.Sessions;

/// <summary>
/// One saved project session (sessions plan §3): a named, tagged set of PDF
/// files that were open together, to be seen and reopened later. Stored in
/// sessions.json by SessionStore; never part of places.json or a places
/// export.
///
/// A session holds file paths only. It records nothing about which viewer
/// had a file open, the page, or the window layout: reopening a session asks
/// Windows to open each file with its default application, as a place is
/// opened.
/// </summary>
public sealed class ProjectSession
{
    /// <summary>Stable identity, a GUID in "N" form. Never shown.</summary>
    public string Id { get; set; } = "";

    /// <summary>What the user called the session. Unique, ignoring case.</summary>
    public string Name { get; set; } = "";

    /// <summary>The session's tags, each unique ignoring case, in the order the user gave them.</summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>Fully qualified PDF paths, each unique ignoring case, in the order they were saved.</summary>
    public List<string> Files { get; set; } = new();

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the name, tags or files last changed.</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>When the session was last reopened from QuickerPlaces, or null if never.</summary>
    public DateTimeOffset? LastOpenedAt { get; set; }
}
