namespace QuickerPlaces.Models;

/// <summary>
/// What happened the last time PlacesService tried to load places.json,
/// per the Phase 1 plan's D6 and section 5.3. This is what replaces the
/// old <c>LoadFailed</c> boolean: a single flag could not tell "the file
/// is damaged" from "the file could not be opened" from "the file is from
/// a newer build", and those three situations call for different messages
/// and — critically — different permissions about what the application is
/// allowed to do to the file on disk.
///
/// The three failure members below are deliberately documented with what
/// recovery may and may not do in response, because that asymmetry is the
/// single most important rule in Phase 1 (plan 5.4): the wrong response to
/// <see cref="Unreadable"/> or <see cref="WrittenByNewerVersion"/> would
/// destroy intact data. Do not add a "start with an empty list" option, or
/// any other path that renames, replaces, or writes the store file, to
/// either of those two outcomes — that is the mistake this documentation
/// exists to stop.
/// </summary>
public enum StoreLoadOutcome
{
    /// <summary>
    /// The store loaded and deserialized normally (schemaVersion equal to,
    /// or below and successfully migrated to,
    /// <c>PlacesService.CurrentSchemaVersion</c>). Normal operation; no
    /// recovery state is set.
    /// </summary>
    Ok,

    /// <summary>
    /// No store file exists yet. This is first run, not a failure: the
    /// service starts with an empty list and the first save creates the
    /// file. No recovery state is set.
    /// </summary>
    NotPresent,

    /// <summary>
    /// The file was opened and read, but its content is not a usable
    /// store: it isn't valid JSON, it doesn't parse to a PlacesStore, or
    /// its schemaVersion is missing or non-numeric. The application MAY
    /// offer to quarantine the file (rename it aside, preserving its
    /// bytes, per <see cref="IPlacesStorage.Quarantine"/> in
    /// Services/IPlacesStorage.cs) and start empty once the user chooses
    /// to. It MUST NOT quarantine, delete, or overwrite the file on its
    /// own initiative — only an explicit user choice does that — and it
    /// MUST NOT accept ordinary mutations while this is unresolved (D3).
    /// </summary>
    Damaged,

    /// <summary>
    /// The file could not be opened or read at all — permissions, another
    /// process holding it, or any other unexpected failure classified by
    /// the safe default in D6. This says nothing about whether the file's
    /// contents are intact; a sync client or antivirus holding it for a
    /// few seconds is more likely than damage. The application MUST NOT
    /// quarantine, rename, replace, or write the file in response to this
    /// outcome — doing so could destroy data that was never actually
    /// damaged. There is no "start with an empty list" option for this
    /// outcome, ever. The only actions allowed are retrying the load and
    /// revealing the file's location to the user.
    /// </summary>
    Unreadable,

    /// <summary>
    /// The file parsed and carries a schemaVersion greater than
    /// <c>PlacesService.CurrentSchemaVersion</c> — a newer build wrote it.
    /// The data is intact and a newer build can read it; this build
    /// cannot safely interpret fields it doesn't know about. The
    /// application MUST NOT quarantine, rename, replace, or write the
    /// file in response to this outcome, and MUST NOT offer a "start with
    /// an empty list" option: doing either would discard data this build
    /// simply isn't new enough to understand. The only actions allowed are
    /// exiting and revealing the file's location so the user can act on
    /// it themselves (e.g. after upgrading).
    /// </summary>
    WrittenByNewerVersion
}
