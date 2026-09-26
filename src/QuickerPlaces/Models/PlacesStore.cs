using System.Collections.Generic;

namespace QuickerPlaces.Models;

/// <summary>
/// The root JSON document persisted by PlacesService — the full set of
/// stored Places plus a schema version for future migrations. Kept as its
/// own document (own file, own schema version) separate from AppSettings'
/// window chrome, since the two have very different persistence needs
/// (write-through-on-every-change vs. save-once-on-exit) and different
/// natural "does this matter on another machine" defaults.
/// </summary>
public sealed class PlacesStore
{
    /// <summary>
    /// Version 3 since Phase 3 (id, lastOpenedAt, openCount); 2 since Phase 2
    /// (UTC dateAdded, deletedAt). This initialiser
    /// never decides what reaches disk: PlacesService sets it from
    /// CurrentSchemaVersion on every write, and reads it from the file
    /// before binding (PlacesStoreMigration).
    /// </summary>
    public int SchemaVersion { get; set; } = 3;

    /// <summary>Every stored place in list order, including those in Recently Deleted (D7): the file order is the list order.</summary>

    public List<Place> Places { get; set; } = new();
}
