using System;
using System.Collections.Generic;

namespace QuickerPlaces.Models.Sessions;

/// <summary>
/// The root of a .qpsession file (session sharing plan §3): one saved
/// session as it is handed to another QuickerPlaces user. It carries the
/// name, the tags and, for each file, every way another PC might reach it:
/// the path on the sender's PC, the file's web address when it is in a
/// OneDrive or SharePoint folder the sender syncs, and the network path when
/// it is on a share. Never the files themselves, and nothing of the sender's
/// history (when it was saved or reopened).
/// </summary>
public sealed class SharedSessionDocument
{
    /// <summary>What every .qpsession file says it is, so another JSON file is never taken for one.</summary>
    public const string FormatName = "quickerplaces-session";

    /// <summary>
    /// <see cref="FormatName"/> in every file; SharedSessionFormat sets it on
    /// writing. Empty by default, so a JSON file without it is never taken
    /// for a shared session.
    /// </summary>
    public string Format { get; set; } = "";

    /// <summary>SharedSessionFormat writes its CurrentSchemaVersion and refuses a newer one.</summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>When the file was made, in UTC: shown to the recipient as "Shared on …".</summary>
    public DateTimeOffset SharedAt { get; set; }

    public string Name { get; set; } = "";

    public List<string> Tags { get; set; } = new();

    public List<SharedSessionFile> Files { get; set; } = new();
}

/// <summary>One file of a shared session and the ways to find it.</summary>
public sealed class SharedSessionFile
{
    /// <summary>The full path on the sender's PC. It can name the sender's user folder.</summary>
    public string Path { get; set; } = "";

    /// <summary>Where the file lives, as the sender's PC saw it.</summary>
    public SharedFileLocation Location { get; set; }

    /// <summary>
    /// The file's web address (https only) when it is in a OneDrive or
    /// SharePoint library the sender syncs, or null. The recipient's PC maps
    /// it back into their own synced copy of that library, if they have one.
    /// </summary>
    public string? Url { get; set; }

    /// <summary>The \\server\share path, when the file is on a network share or a mapped drive, or null.</summary>
    public string? NetworkPath { get; set; }
}

/// <summary>Where a shared file lives, which decides how the recipient can reach it.</summary>
public enum SharedFileLocation
{
    /// <summary>Only on the sender's PC (a local drive, outside any synced library). Usually unreachable for anyone else.</summary>
    ThisPc,

    /// <summary>On a network share, directly or through a mapped drive.</summary>
    Network,

    /// <summary>In a SharePoint or Teams library the sender syncs with OneDrive. Others with access can sync it too.</summary>
    CloudLibrary,

    /// <summary>In someone's personal OneDrive: others can open it only if it was shared with them.</summary>
    PersonalCloud,
}
