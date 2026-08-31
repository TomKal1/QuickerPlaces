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
    public int SchemaVersion { get; set; } = 1;

    public List<Place> Places { get; set; } = new();
}
