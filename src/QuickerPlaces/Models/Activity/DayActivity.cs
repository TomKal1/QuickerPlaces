using System;
using System.Collections.Generic;

namespace QuickerPlaces.Models.Activity;

/// <summary>One local day's time per folder under a root (Phase 9 plan 5.2).</summary>
public sealed class DayActivity
{
    /// <summary>
    /// Keyed by the credited folder, compared ignoring case: the tracker keeps
    /// Explorer's casing below the root, so one folder can arrive spelled two
    /// ways. ActivityStore rebuilds this with the comparer after every load,
    /// since deserializing replaces it with a case-sensitive one.
    /// </summary>
    public Dictionary<string, FolderTotal> Folders { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
