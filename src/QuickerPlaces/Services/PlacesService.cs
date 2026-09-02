using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using QuickerPlaces.Models;

namespace QuickerPlaces.Services;

/// <summary>
/// A validation outcome that never throws to the caller — every add/rename/
/// edit path in QuickerPlaces returns one of these instead of raising an
/// exception on bad input, per the "no unhandled exceptions in normal
/// operation" convention (SI §9).
/// </summary>
public readonly struct ValidationResult
{
    public bool Success { get; }
    public string? ErrorMessage { get; }

    private ValidationResult(bool success, string? errorMessage)
    {
        Success = success;
        ErrorMessage = errorMessage;
    }

    public static ValidationResult Ok() => new(true, null);
    public static ValidationResult Fail(string errorMessage) => new(false, errorMessage);
}

/// <summary>
/// Owns the canonical, in-memory list of Places, backing it with a
/// continuously-written-through JSON file. Every mutating method here both
/// updates the in-memory list and persists immediately (SI §5) — there is
/// no separate "Save" step for a derived app to forget to call.
///
/// Stored at %AppData%\QuickerPlaces\places.json (roaming), deliberately
/// separate from AppSettings' window-chrome settings.json (which stays
/// local/machine-specific — see AppSettings' remarks): a place list is
/// exactly the kind of data a user would want to follow them on a
/// roaming-profile domain machine, unlike a remembered window position.
/// </summary>
public sealed class PlacesService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly IPlacesStorage _storage;
    private readonly List<Place> _places;

    /// <summary>Builds the production service over the real, roaming AppData store — unchanged from before the storage seam existed, so App.xaml.cs needs no changes.</summary>
    public PlacesService() : this(FilePlacesStorage.ForDefaultLocation())
    {
    }

    /// <summary>Builds the service over any IPlacesStorage — the seam a test uses to exercise load/save behaviour without touching a real disk.</summary>
    public PlacesService(IPlacesStorage storage)
    {
        _storage = storage;
        var (places, outcome) = LoadFromDisk();
        _places = places;
        LoadOutcome = outcome;

        // D3: a store that is damaged, could not be opened, or came from a
        // newer version starts empty and refuses mutations until the
        // recovery flow (App.xaml.cs) resolves it — see IsMutationBlocked
        // below. Ok and NotPresent need no recovery state at all.
        if (RequiresRecovery(outcome))
            SetRecoveryUnresolved(RecoveryMessageFor(outcome));
    }

    /// <summary>
    /// The current on-disk schema version this build writes and expects.
    /// Bump only alongside a migration branch in LoadFromDisk (plan 5.3) —
    /// there are no prior versions yet, so there is nothing to migrate
    /// today.
    /// </summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>
    /// What happened the last time the store was loaded — see
    /// <see cref="StoreLoadOutcome"/> for what each value means and, for
    /// the three failure values, what the application is and is not
    /// allowed to do about it. Replaces the old LoadFailed boolean, which
    /// could not distinguish a damaged file from one that could not be
    /// opened (D6).
    /// </summary>
    public StoreLoadOutcome LoadOutcome { get; private set; }

    /// <summary>Full path to places.json — handy for a "Reveal in Explorer" menu item.</summary>
    public string PlacesFilePath => _storage.StoreFilePath;

    /// <summary>Snapshot of all stored places, in stored order. Callers that need live updates should go through MainViewModel's ObservableCollection instead.</summary>
    public IReadOnlyList<Place> Places => _places;

    // ---------------------------------------------------------------
    // Validation — shared by both live inline dialog validation and the
    // Try* commit methods below, so the rules can never drift apart.
    // ---------------------------------------------------------------

    /// <summary>Case-insensitive uniqueness check against all existing aliases (SI §6.2), excluding <paramref name="excluding"/> itself when editing.</summary>
    public ValidationResult ValidateAlias(string? alias, Place? excluding = null)
    {
        if (string.IsNullOrWhiteSpace(alias))
            return ValidationResult.Fail("Alias can't be empty.");

        var trimmed = alias.Trim();
        var collides = _places.Any(p =>
            !ReferenceEquals(p, excluding) &&
            string.Equals(p.Alias, trimmed, StringComparison.OrdinalIgnoreCase));

        return collides
            ? ValidationResult.Fail($"\"{trimmed}\" is already in use — pick a different alias.")
            : ValidationResult.Ok();
    }

    /// <summary>
    /// Format validation plus the case-insensitive exact-match duplicate
    /// check against other places of the same Type (SI §6.2 — deliberately
    /// not normalized: "C:\Foo" and "C:\Foo\" are different values, as are
    /// http/https variants of a URL).
    /// </summary>
    public ValidationResult ValidateResource(string? resource, PlaceType type, Place? excluding = null)
    {
        if (string.IsNullOrWhiteSpace(resource))
            return ValidationResult.Fail(type == PlaceType.Folder ? "Folder path can't be empty." : "URL can't be empty.");

        var trimmed = resource.Trim();

        var formatResult = type == PlaceType.Folder
            ? ValidateFolderFormat(trimmed)
            : ValidateUrlFormat(trimmed);

        if (!formatResult.Success)
            return formatResult;

        var collides = _places.Any(p =>
            !ReferenceEquals(p, excluding) &&
            p.Type == type &&
            string.Equals(p.Resource, trimmed, StringComparison.OrdinalIgnoreCase));

        return collides
            ? ValidationResult.Fail("That path/URL is already stored under another alias.")
            : ValidationResult.Ok();
    }

    private static ValidationResult ValidateFolderFormat(string path)
    {
        // Syntactic validity only — SI §6.2 explicitly leaves
        // existence-on-disk checking optional ("either is acceptable"), so
        // this deliberately does not require the folder to exist yet: the
        // user may be pre-registering a path for a drive that isn't
        // currently mounted, a share that's offline, etc.
        try
        {
            _ = Path.GetFullPath(path);
        }
        catch
        {
            return ValidationResult.Fail("That doesn't look like a valid folder path.");
        }

        if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            return ValidationResult.Fail("That path contains characters that aren't allowed in a folder path.");

        return ValidationResult.Ok();
    }

    private static ValidationResult ValidateUrlFormat(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out _)
            ? ValidationResult.Ok()
            : ValidationResult.Fail("That doesn't look like a valid, complete URL (e.g. https://example.com).");

    // ---------------------------------------------------------------
    // Mutations — each one validates, mutates the in-memory list, then
    // writes through to disk immediately (SI §5).
    // ---------------------------------------------------------------

    public ValidationResult TryAdd(string alias, PlaceType type, string resource, out Place? created, out PersistenceResult persistence)
    {
        created = null;

        if (IsMutationBlocked(out persistence))
            return ValidationResult.Fail(BlockedMessage());

        var aliasResult = ValidateAlias(alias);
        if (!aliasResult.Success)
        {
            // Nothing was mutated, so there is nothing that failed to
            // persist — the caller's ValidationResult.Success is what
            // tells it to stop, not this.
            persistence = PersistenceResult.Ok();
            return aliasResult;
        }

        var resourceResult = ValidateResource(resource, type);
        if (!resourceResult.Success)
        {
            persistence = PersistenceResult.Ok();
            return resourceResult;
        }

        var place = new Place
        {
            Alias = alias.Trim(),
            Type = type,
            Resource = resource.Trim(),
            IsFavourite = false,
            FavouriteOrder = null,
            DateAdded = DateTime.Now
        };

        _places.Add(place);
        persistence = Persist();

        created = place;
        return ValidationResult.Ok();
    }

    public ValidationResult TryRenameAlias(Place place, string newAlias, out PersistenceResult persistence)
    {
        if (IsMutationBlocked(out persistence))
            return ValidationResult.Fail(BlockedMessage());

        var result = ValidateAlias(newAlias, excluding: place);
        if (!result.Success)
        {
            persistence = PersistenceResult.Ok();
            return result;
        }

        place.Alias = newAlias.Trim();
        persistence = Persist();
        return ValidationResult.Ok();
    }

    public ValidationResult TryEditResource(Place place, string newResource, out PersistenceResult persistence)
    {
        if (IsMutationBlocked(out persistence))
            return ValidationResult.Fail(BlockedMessage());

        var result = ValidateResource(newResource, place.Type, excluding: place);
        if (!result.Success)
        {
            persistence = PersistenceResult.Ok();
            return result;
        }

        place.Resource = newResource.Trim();
        persistence = Persist();
        return ValidationResult.Ok();
    }

    /// <summary>Turns favouriting on/off. Turning on appends to the end of the favourite order; turning off renumbers the remaining favourites so FavouriteOrder stays a dense 0..n-1 sequence.</summary>
    public PersistenceResult ToggleFavourite(Place place)
    {
        if (IsMutationBlocked(out var blocked))
            return blocked;

        if (place.IsFavourite)
        {
            place.IsFavourite = false;
            place.FavouriteOrder = null;
            RenumberFavourites();
        }
        else
        {
            place.IsFavourite = true;
            place.FavouriteOrder = _places.Where(p => p.IsFavourite).Count() - 1;
            // The above counts `place` itself (already flagged), so the
            // count-1 lands it at the end — equivalent to Max(existing)+1
            // without needing a separate "any favourites yet" branch.
        }

        return Persist();
    }

    /// <summary>Reassigns FavouriteOrder for every current favourite to match <paramref name="orderedFavourites"/> (0-based, dense). Used after a bubble drag-reorder.</summary>
    public PersistenceResult SetFavouriteOrder(IReadOnlyList<Place> orderedFavourites)
    {
        if (IsMutationBlocked(out var blocked))
            return blocked;

        for (var i = 0; i < orderedFavourites.Count; i++)
            orderedFavourites[i].FavouriteOrder = i;

        return Persist();
    }

    public PersistenceResult Remove(Place place)
    {
        if (IsMutationBlocked(out var blocked))
            return blocked;

        _places.Remove(place);
        if (place.IsFavourite)
            RenumberFavourites();
        return Persist();
    }

    private void RenumberFavourites()
    {
        var favourites = _places.Where(p => p.IsFavourite).OrderBy(p => p.FavouriteOrder ?? int.MaxValue).ToList();
        for (var i = 0; i < favourites.Count; i++)
            favourites[i].FavouriteOrder = i;
    }

    // ---------------------------------------------------------------
    // Export / Import (SI §6.5 / §6.6)
    // ---------------------------------------------------------------

    /// <summary>Writes the given places to <paramref name="filePath"/> as a standalone PlacesStore JSON document. Returns an error message on failure, or null on success.</summary>
    public string? Export(IEnumerable<Place> places, string filePath)
    {
        try
        {
            var export = new PlacesStore { Places = places.ToList() };
            var json = JsonSerializer.Serialize(export, JsonOptions);
            File.WriteAllText(filePath, json);
            return null;
        }
        catch (Exception ex)
        {
            return $"Couldn't write the export file: {ex.Message}";
        }
    }

    /// <summary>
    /// Reads a previously-exported file and returns only the candidates
    /// that do NOT collide with anything already stored (SI §6.6 — an
    /// incoming item whose alias or resource collides is excluded before
    /// the user ever sees it as an option). Returns an error message
    /// instead of candidates if the file can't be read/parsed.
    /// </summary>
    public (List<Place> candidates, string? errorMessage) GetImportCandidates(string filePath)
    {
        try
        {
            var json = File.ReadAllText(filePath);
            var store = JsonSerializer.Deserialize<PlacesStore>(json, JsonOptions);
            var incoming = store?.Places ?? new List<Place>();

            var candidates = incoming
                .Where(p => !string.IsNullOrWhiteSpace(p.Alias) && !string.IsNullOrWhiteSpace(p.Resource))
                .Where(p => ValidateAlias(p.Alias).Success && ValidateResource(p.Resource, p.Type).Success)
                .ToList();

            return (candidates, null);
        }
        catch (Exception ex)
        {
            return (new List<Place>(), $"Couldn't read that file: {ex.Message}");
        }
    }

    /// <summary>
    /// Adds the user-selected import candidates as new Place records (never
    /// the candidate instances themselves — those came from a deserialized
    /// file and are never entered into the live store as-is) and returns
    /// the ones actually added. Re-validates each one against the current
    /// store at commit time (defensive — the store could in principle have
    /// changed since the preview was shown) and silently skips any that no
    /// longer pass.
    ///
    /// Persists once for the whole batch (test 19), not once per record —
    /// D2's whole-store write makes per-record saves both wasteful and
    /// pointless. A failed persist still leaves every successfully-added
    /// record in memory (D1, test 20): the import is not rolled back just
    /// because the disk write that reports it failed.
    /// </summary>
    public (List<Place> imported, PersistenceResult persistence) CommitImport(IEnumerable<Place> selectedCandidates)
    {
        if (IsMutationBlocked(out var blocked))
            return (new List<Place>(), blocked);

        var imported = new List<Place>();

        foreach (var candidate in selectedCandidates)
        {
            if (!ValidateAlias(candidate.Alias).Success)
                continue;
            if (!ValidateResource(candidate.Resource, candidate.Type).Success)
                continue;

            var place = new Place
            {
                Alias = candidate.Alias.Trim(),
                Type = candidate.Type,
                Resource = candidate.Resource.Trim(),
                IsFavourite = false,
                FavouriteOrder = null,
                DateAdded = DateTime.Now
            };

            _places.Add(place);
            imported.Add(place);
        }

        var persistence = imported.Count > 0 ? Persist() : PersistenceResult.Ok();
        return (imported, persistence);
    }

    // ---------------------------------------------------------------
    // Disk I/O
    // ---------------------------------------------------------------

    /// <summary>
    /// Loads and classifies the store (plan 5.3, D6). Reading the file and
    /// parsing it are two separate try blocks, deliberately: a failure to
    /// open the file and a failure to make sense of its contents are
    /// different situations calling for different responses, and D6's
    /// classification exists only at each catch — once collapsed into one
    /// boolean (as the pre-Phase-1 LoadFailed did), the distinction cannot
    /// be recovered later.
    /// </summary>
    private (List<Place> places, StoreLoadOutcome outcome) LoadFromDisk()
    {
        if (!_storage.Exists)
        {
            DiagnosticLog.Info("No existing places store found; starting with an empty list.");
            return (new List<Place>(), StoreLoadOutcome.NotPresent);
        }

        string json;
        try
        {
            json = _storage.Read();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DiagnosticLog.Error($"Places store at {_storage.StoreFilePath} could not be opened.", ex);
            return (new List<Place>(), StoreLoadOutcome.Unreadable);
        }
        catch (Exception ex)
        {
            // D6's safe default: any exception type this read did not
            // specifically anticipate is still classified Unreadable, not
            // Damaged. Refusing to touch (quarantine, rename, replace) a
            // file whose failure mode we don't recognize is the safe
            // choice — see StoreLoadOutcome.Unreadable's remarks.
            DiagnosticLog.Error($"Places store at {_storage.StoreFilePath} could not be opened (unexpected exception type {ex.GetType().Name}).", ex);
            return (new List<Place>(), StoreLoadOutcome.Unreadable);
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            // The absent/non-numeric-version case is deliberately Damaged,
            // not "assume version 1" (plan 5.3): every store this
            // application has ever written includes schemaVersion, so a
            // document without one — or with one that isn't a number — is
            // not an old-but-valid v1 store. It is damaged, or it is a
            // file this application never wrote at all.
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("schemaVersion", out var versionElement) ||
                versionElement.ValueKind != JsonValueKind.Number ||
                !versionElement.TryGetInt32(out var version))
            {
                DiagnosticLog.Warn($"Places store at {_storage.StoreFilePath} has no usable schemaVersion; treating as damaged.");
                return (new List<Place>(), StoreLoadOutcome.Damaged);
            }

            if (version > CurrentSchemaVersion)
            {
                DiagnosticLog.Warn($"Places store at {_storage.StoreFilePath} has schemaVersion {version}, newer than this build's {CurrentSchemaVersion}.");
                return (new List<Place>(), StoreLoadOutcome.WrittenByNewerVersion);
            }

            var store = JsonSerializer.Deserialize<PlacesStore>(json, JsonOptions);

            // Both halves of this check matter. A document that is
            // literally "null" deserializes to a null store; one whose
            // "places" is explicitly null overwrites PlacesStore's
            // initializer with null. Neither is a usable store, and
            // neither throws JsonException — without this check the
            // Places dereference below would raise a
            // NullReferenceException straight out of the constructor and
            // crash the app on launch, which is the exact failure this
            // phase exists to stop.
            if (store?.Places is null)
            {
                DiagnosticLog.Warn($"Places store at {_storage.StoreFilePath} parsed but holds no usable place list; treating as damaged.");
                return (new List<Place>(), StoreLoadOutcome.Damaged);
            }

            if (version < CurrentSchemaVersion)
            {
                // No prior schema version exists yet (CurrentSchemaVersion
                // is still 1), so there is nothing to migrate today. This
                // branch is written now, deliberately empty apart from the
                // log line, so the next version bump has a documented
                // place to add a migration step rather than inventing the
                // gate from scratch. Whatever a future migration produces
                // here must not be written back to disk until a save
                // succeeds through the normal Persist() path (plan 5.3,
                // test 15) — this method never calls _storage.Write.
                DiagnosticLog.Info($"Migrating places store at {_storage.StoreFilePath} from schemaVersion {version} to {CurrentSchemaVersion} in memory (no-op: no prior versions exist yet).");
            }

            DiagnosticLog.Info($"Loaded {store.Places.Count} place(s) from {_storage.StoreFilePath} (schemaVersion {version}).");
            return (store.Places, StoreLoadOutcome.Ok);
        }
        catch (JsonException ex)
        {
            DiagnosticLog.Error($"Places store at {_storage.StoreFilePath} is not valid JSON.", ex);
            return (new List<Place>(), StoreLoadOutcome.Damaged);
        }
    }

    private static bool RequiresRecovery(StoreLoadOutcome outcome)
        => outcome is StoreLoadOutcome.Damaged or StoreLoadOutcome.Unreadable or StoreLoadOutcome.WrittenByNewerVersion;

    private static string RecoveryMessageFor(StoreLoadOutcome outcome) => outcome switch
    {
        StoreLoadOutcome.Damaged => "Your saved places file appears to be damaged. Resolve the recovery prompt before making changes.",
        StoreLoadOutcome.Unreadable => "Your saved places file could not be opened. Resolve the recovery prompt before making changes.",
        StoreLoadOutcome.WrittenByNewerVersion => "Your saved places were written by a newer version of QuickerPlaces. Update QuickerPlaces to make changes.",
        _ => "Your saved places need attention before changes can be saved."
    };

    /// <summary>
    /// The "Try again" recovery action for <see cref="StoreLoadOutcome.Unreadable"/>
    /// (plan 5.4): re-runs the whole load from scratch. On success — the
    /// file that couldn't be opened a moment ago now can be — the real
    /// places replace whatever empty/stale in-memory list recovery left
    /// behind, and the recovery state clears so mutations work normally
    /// again. On failure the state stays unresolved and the caller (the
    /// App.xaml.cs recovery loop) asks again. Both outcomes are logged so
    /// the diagnostic record shows the original failure and, if it
    /// happened, the successful recovery.
    /// </summary>
    public StoreLoadOutcome Reload()
    {
        var (places, outcome) = LoadFromDisk();

        _places.Clear();
        _places.AddRange(places);
        LoadOutcome = outcome;

        if (RequiresRecovery(outcome))
        {
            SetRecoveryUnresolved(RecoveryMessageFor(outcome));
            DiagnosticLog.Warn($"Reload of {_storage.StoreFilePath} did not resolve the recovery state (outcome: {outcome}).");
        }
        else
        {
            ClearRecoveryUnresolved();
            DiagnosticLog.Info($"Reload of {_storage.StoreFilePath} succeeded; recovery resolved.");
        }

        return outcome;
    }

    /// <summary>
    /// The "Start with an empty list" recovery action for
    /// <see cref="StoreLoadOutcome.Damaged"/> ONLY — never called for
    /// Unreadable or WrittenByNewerVersion, which must never be
    /// quarantined (see StoreLoadOutcome's remarks). Quarantines the
    /// damaged file via IPlacesStorage.Quarantine, logs the quarantine
    /// path (the one privacy-rule exception — plan 5.5), and resolves the
    /// recovery state so the empty in-memory store can now be saved
    /// normally.
    ///
    /// If the quarantine itself fails, the original file was NOT moved
    /// aside, so recovery must not be considered resolved: proceeding to a
    /// writable state here would risk the next save overwriting a damaged
    /// file that was never actually preserved. The failure is returned as
    /// a <see cref="PersistenceResult"/> (Saved: false, with a message)
    /// rather than a bare bool or a swallowed exception, so a caller
    /// cannot accidentally ignore it the way a discarded return value
    /// could be.
    /// </summary>
    public PersistenceResult QuarantineAndStartEmpty()
    {
        try
        {
            var quarantinedPath = _storage.Quarantine(DateTimeOffset.Now);
            DiagnosticLog.Warn($"Quarantined damaged places store to {quarantinedPath}.");

            _places.Clear();
            ClearRecoveryUnresolved();
            return PersistenceResult.Ok();
        }
        catch (Exception ex)
        {
            DiagnosticLog.Error($"Failed to quarantine damaged places store at {_storage.StoreFilePath}.", ex);

            // Recovery stays unresolved (IsRecoveryUnresolved is untouched
            // above on this path) — mutations remain blocked, and the
            // caller must show this failure rather than proceed.
            return PersistenceResult.Fail(
                $"Couldn't set aside the damaged file at \"{_storage.StoreFilePath}\". {ex.Message}");
        }
    }

    /// <summary>
    /// Writes places.json atomically via IPlacesStorage.Write, which
    /// serializes to a temp file in the same directory, flushes it to
    /// disk, then replaces the real file in one filesystem operation, so a
    /// crash or power-loss mid-write can never leave a truncated or
    /// half-written places.json behind (SI §5).
    ///
    /// On failure this deliberately does NOT roll back the in-memory
    /// change that triggered it (D1). Rolling back would throw away
    /// whatever the user just typed, and it would leave RetrySave with
    /// nothing to retry — the whole point of keeping the proposed state in
    /// memory is that Retry can re-serialize and rewrite it verbatim.
    /// HasUnsavedChanges is what stops the application from claiming a
    /// change is safely stored; it is not a rollback signal.
    /// </summary>
    private PersistenceResult Persist()
    {
        try
        {
            var store = new PlacesStore { Places = _places };
            var json = JsonSerializer.Serialize(store, JsonOptions);
            _storage.Write(json);

            HasUnsavedChanges = false;
            return PersistenceResult.Ok();
        }
        catch (Exception ex)
        {
            // Broad catch is intentional: the storage layer can throw
            // IOException (disk full, file locked by another process),
            // UnauthorizedAccessException (permissions), or anything else
            // a filesystem can raise. Whatever it is, the point of this
            // step is that it is never swallowed — it becomes a returned
            // PersistenceResult the caller must look at, and a logged
            // diagnostic entry.
            HasUnsavedChanges = true;

            // Privacy rule (DiagnosticLog remarks / plan 5.5): name the
            // store path and the record count, never a place's alias or
            // resource. The count and path are enough to diagnose "why
            // didn't my data save" without writing anyone's data to a
            // second, less-protected file.
            DiagnosticLog.Error(
                $"Failed to save {_places.Count} place(s) to {_storage.StoreFilePath}",
                ex);

            var message = $"Couldn't save your places to \"{_storage.StoreFilePath}\". {ex.Message}";
            return PersistenceResult.Fail(message);
        }
    }

    /// <summary>
    /// True once a mutation has changed the in-memory store but the change
    /// has not yet reached disk — set by a failed Persist(), cleared by the
    /// next successful one (including a successful RetrySave()). This is
    /// the seam a later step's banner reads; nothing here shows it to the
    /// user directly.
    /// </summary>
    public bool HasUnsavedChanges { get; private set; }

    /// <summary>
    /// Re-serializes and rewrites the whole in-memory store. Safe to call
    /// with no queue of pending operations to replay: D2's whole-store
    /// writes make every save idempotent — there is only ever "the current
    /// state", never a sequence of deltas — and D1 keeps the user's most
    /// recent change in memory, so there is something for Retry to
    /// actually retry.
    /// </summary>
    public PersistenceResult RetrySave()
    {
        if (IsMutationBlocked(out var blocked))
            return blocked;

        return Persist();
    }

    /// <summary>
    /// D3 — true when the store must not be written to until the user
    /// resolves a startup recovery prompt (a damaged file, one that could
    /// not be opened, or one written by a newer version). Set by the
    /// constructor and Reload() from the load classification
    /// (StoreLoadOutcome, D6), and cleared by Reload() on a successful
    /// retry or by QuarantineAndStartEmpty() on a successful quarantine —
    /// see SetRecoveryUnresolved/ClearRecoveryUnresolved below. Every
    /// mutation checks this first via IsMutationBlocked.
    /// </summary>
    public bool IsRecoveryUnresolved { get; private set; }

    /// <summary>The message every mutation returns while <see cref="IsRecoveryUnresolved"/> is true. Set together with it.</summary>
    public string? RecoveryBlockedMessage { get; private set; }

    /// <summary>
    /// The real setter behind <see cref="IsRecoveryUnresolved"/> —
    /// called by the constructor and Reload() when LoadFromDisk's
    /// classification (StoreLoadOutcome, D6) says the store is Damaged,
    /// Unreadable, or WrittenByNewerVersion. Replaces the earlier,
    /// test-only MarkRecoveryUnresolvedForTests now that a real caller
    /// exists.
    /// </summary>
    private void SetRecoveryUnresolved(string message)
    {
        IsRecoveryUnresolved = true;
        RecoveryBlockedMessage = message;
    }

    /// <summary>Clears the recovery-unresolved state — called only after a real successful Reload() or QuarantineAndStartEmpty(), never speculatively.</summary>
    private void ClearRecoveryUnresolved()
    {
        IsRecoveryUnresolved = false;
        RecoveryBlockedMessage = null;
    }

    /// <summary>
    /// D3's guard: every mutation calls this first. If recovery is
    /// unresolved, the mutation makes no in-memory change at all and
    /// returns a failure carrying the recovery message — the one case in
    /// this phase where a mutation is rejected outright rather than
    /// accepted and banner-flagged, because a damaged or foreign file must
    /// never be overwritten by a normal edit.
    /// </summary>
    private bool IsMutationBlocked(out PersistenceResult blocked)
    {
        if (IsRecoveryUnresolved)
        {
            blocked = PersistenceResult.Fail(BlockedMessage());
            return true;
        }

        blocked = default;
        return false;
    }

    /// <summary>
    /// The text a blocked mutation reports. Falls back to a generic
    /// sentence rather than dereferencing RecoveryBlockedMessage with a
    /// null-forgiving operator: the two properties are only ever set
    /// together today, but a later step adds the real setter, and a
    /// missed assignment there should degrade to a vague message rather
    /// than a NullReferenceException in front of a user whose data is
    /// already in trouble.
    /// </summary>
    private string BlockedMessage()
        => RecoveryBlockedMessage ?? "Your saved places need attention before changes can be saved.";
}
