using System.Collections.Generic;

namespace QuickerPlaces.Models.Sessions;

/// <summary>
/// The root of sessions.json (sessions plan §3): every saved project
/// session, with the file's own schema version. Kept beside places.json in
/// roaming application data, because a session is portable user data like a
/// place, but in a file of its own so places.json's schema is untouched.
/// </summary>
public sealed class SessionsDocument
{
    /// <summary>SessionStore sets this from its CurrentSchemaVersion on every write and checks it on every load.</summary>
    public int SchemaVersion { get; set; } = 1;

    public List<ProjectSession> Sessions { get; set; } = new();
}
