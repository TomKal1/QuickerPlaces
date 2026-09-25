using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
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

    /// <summary>
    /// How places.json is parsed before binding. Duplicate property names
    /// are refused at parse time, as a JsonException: JsonObject cannot
    /// hold them, and would otherwise throw ArgumentException on first
    /// access — a type LoadFromDisk's catch does not classify (D6), so it
    /// would escape the constructor. QuickerPlaces never writes a duplicate
    /// key, so a file that has one is Damaged like any other malformed file.
    /// </summary>
    private static readonly JsonDocumentOptions StoreDocumentOptions = new() { AllowDuplicateProperties = false };

    private readonly IPlacesStorage _storage;
    private readonly TimeProvider _time;
    private readonly List<Place> _places;

    /// <summary>Builds the production service over the real, roaming AppData store and the system clock — unchanged from before the storage seam existed, so App.xaml.cs needs no changes.</summary>
    public PlacesService() : this(FilePlacesStorage.ForDefaultLocation(), TimeProvider.System)
    {
    }

    /// <summary>
    /// Builds the service over any IPlacesStorage — the seam a test uses to
    /// exercise load/save behaviour without touching a real disk — and,
    /// optionally, any clock (D12). Every timestamp this service stamps or
    /// compares comes from <paramref name="timeProvider"/>, never from
    /// DateTime.Now directly, so a test can pin both the instant and the
    /// local time zone instead of inheriting the machine's.
    /// </summary>
    public PlacesService(IPlacesStorage storage, TimeProvider? timeProvider = null)
    {
        _storage = storage;
        // Assigned before LoadFromDisk: loading is the first thing that
        // may need the clock.
        _time = timeProvider ?? TimeProvider.System;
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
    /// Bump only alongside a migration branch in LoadFromDisk (Phase 1 plan
    /// 5.3). Version 2 (Phase 2) made DateAdded a UTC DateTimeOffset and
    /// added DeletedAt; a version 1 store is migrated in memory on load by
    /// PlacesStoreMigration (D11) and reaches disk only with the next
    /// successful save.
    /// </summary>
    public const int CurrentSchemaVersion = 2;

    /// <summary>
    /// What happened the last time the store was loaded — see
    /// <see cref="StoreLoadOutcome"/> for what each value means and, for
    /// the three failure values, what the application is and is not
    /// allowed to do about it. Replaces the old LoadFailed boolean, which
    /// could not distinguish a damaged file from one that could not be
    /// opened (D6).
    /// </summary>
    public StoreLoadOutcome LoadOutcome { get; private set; }

    /// <summary>The clock purge decisions use — the Recently Deleted dialog's countdown reads it too, so the two agree (D12).</summary>
    public DateTimeOffset UtcNow => _time.GetUtcNow();

    /// <summary>Full path to places.json — handy for a "Reveal in Explorer" menu item.</summary>
    public string PlacesFilePath => _storage.StoreFilePath;

    /// <summary>
    /// Snapshot of the active places — everything not in Recently Deleted
    /// — in stored order (D8). A fresh list on every call, so don't index
    /// it inside a loop that also calls into this service. Callers that
    /// need live updates should go through MainViewModel's
    /// ObservableCollection instead.
    /// </summary>
    public IReadOnlyList<Place> Places => Active.ToList();

    /// <summary>Snapshot of the places in Recently Deleted, most recently removed first (D8). A fresh list on every call.</summary>
    public IReadOnlyList<Place> RecentlyDeleted => _places
        .Where(p => p.DeletedAt is not null)
        .OrderByDescending(p => p.DeletedAt)
        .ToList();

    /// <summary>
    /// The stored places that are not in Recently Deleted, in list order.
    /// A removed place stays in _places, in its slot, with DeletedAt set
    /// (D7), so every enumeration of _places has to decide whether it means
    /// active places; this makes the usual answer one word (plan 5.3).
    /// </summary>
    private IEnumerable<Place> Active => _places.Where(p => p.DeletedAt is null);

    // ---------------------------------------------------------------
    // Validation — shared by both live inline dialog validation and the
    // Try* commit methods below, so the rules can never drift apart.
    // ---------------------------------------------------------------

    /// <summary>
    /// Case-insensitive uniqueness check against the active places' aliases
    /// (SI §6.2), excluding <paramref name="excluding"/> itself when
    /// editing. A place in Recently Deleted never blocks an alias (roadmap
    /// §4.10, D15): restoring it later is what has to deal with the clash.
    /// </summary>
    public ValidationResult ValidateAlias(string? alias, Place? excluding = null)
    {
        if (string.IsNullOrWhiteSpace(alias))
            return ValidationResult.Fail("Alias can't be empty.");

        var trimmed = alias.Trim();
        var collides = Active.Any(p =>
            !ReferenceEquals(p, excluding) &&
            string.Equals(p.Alias, trimmed, StringComparison.OrdinalIgnoreCase));

        return collides
            ? ValidationResult.Fail($"\"{trimmed}\" is already in use — pick a different alias.")
            : ValidationResult.Ok();
    }

    /// <summary>
    /// Format validation plus the case-insensitive exact-match duplicate
    /// check against other active places of the same Type (SI §6.2 —
    /// deliberately not normalized: "C:\Foo" and "C:\Foo\" are different
    /// values, as are http/https variants of a URL). Places in Recently
    /// Deleted are not checked, as for aliases (§4.10, D15).
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

        var collides = Active.Any(p =>
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

        // A relative path ("Projects", "..\Docs") would be resolved
        // against whatever the process's working directory happens to be
        // at Open time, so it'd open a different folder depending on how
        // the app was launched. Require a drive-rooted or UNC path instead.
        if (!Path.IsPathFullyQualified(path))
            return ValidationResult.Fail("Enter a full folder path, including the drive (e.g. C:\\Projects) or network share.");

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
            DateAdded = _time.GetUtcNow()
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

        if (IsInRecentlyDeleted(place, out var deleted))
        {
            persistence = PersistenceResult.Ok();
            return deleted;
        }

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

        if (IsInRecentlyDeleted(place, out var deleted))
        {
            persistence = PersistenceResult.Ok();
            return deleted;
        }

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

    /// <summary>
    /// Plan 5.3 row 6: rename and edit refuse a place in Recently Deleted.
    /// Nothing in the UI can reach one (the grid shows active places only),
    /// so only a bug gets here — but it must not edit a record the user
    /// cannot see, nor change what its restore would bring back.
    /// </summary>
    private static bool IsInRecentlyDeleted(Place place, out ValidationResult refusal)
    {
        refusal = place.DeletedAt is null
            ? ValidationResult.Ok()
            : ValidationResult.Fail($"\"{place.Alias}\" is in Recently Deleted. Restore it first.");
        return place.DeletedAt is not null;
    }

    /// <summary>Turns favouriting on/off. Turning on appends to the end of the favourite order; turning off renumbers the remaining favourites so FavouriteOrder stays a dense 0..n-1 sequence. Does nothing, and writes nothing, for a place in Recently Deleted, whose favourite fields are a remembered slot (D9).</summary>
    public PersistenceResult ToggleFavourite(Place place)
    {
        if (IsMutationBlocked(out var blocked))
            return blocked;

        // Plan 5.3 row 7: not reachable from the UI, and nothing changed,
        // so there is nothing to save or report.
        if (place.DeletedAt is not null)
            return PersistenceResult.Ok();

        if (place.IsFavourite)
        {
            place.IsFavourite = false;
            place.FavouriteOrder = null;
            RenumberFavourites();
        }
        else
        {
            place.IsFavourite = true;
            place.FavouriteOrder = Active.Where(p => p.IsFavourite).Count() - 1;
            // The above counts `place` itself (already flagged), so the
            // count-1 lands it at the end — equivalent to Max(existing)+1
            // without needing a separate "any favourites yet" branch.
            // Active only: a deleted favourite's remembered slot (D9)
            // would otherwise leave a gap in the bubble numbering.
        }

        return Persist();
    }

    /// <summary>Reassigns FavouriteOrder for every current favourite to match <paramref name="orderedFavourites"/> (0-based, dense). Used after a bubble drag-reorder. Any place in Recently Deleted in the list is skipped: the bubbles the caller passes are active places.</summary>
    public PersistenceResult SetFavouriteOrder(IReadOnlyList<Place> orderedFavourites)
    {
        if (IsMutationBlocked(out var blocked))
            return blocked;

        // Plan 5.3 row 8: numbered among active places only, so a deleted
        // record neither takes a bubble number nor loses its remembered
        // slot (D9).
        var order = 0;
        foreach (var place in orderedFavourites)
        {
            if (place.DeletedAt is null)
                place.FavouriteOrder = order++;
        }

        return Persist();
    }

    /// <summary>
    /// Moves <paramref name="place"/> to Recently Deleted: the same record
    /// stays in the same list slot with DeletedAt stamped from the clock
    /// (D7), so a restore — this session's Undo, or Recently Deleted after
    /// a restart — can put it back exactly. <paramref name="removed"/> is
    /// false when nothing changed: blocked by recovery (D3), not in the
    /// store, or already deleted.
    /// </summary>
    public PersistenceResult Remove(Place place, out bool removed)
    {
        removed = false;

        if (IsMutationBlocked(out var blocked))
            return blocked;

        // Not in the store, or already removed: nothing to change, so
        // nothing is written and the caller has nothing to offer Undo for.
        if (!_places.Contains(place) || place.DeletedAt is not null)
            return PersistenceResult.Ok();

        place.DeletedAt = _time.GetUtcNow();
        removed = true;

        // IsFavourite and FavouriteOrder are left as they were: they are now
        // the remembered bubble slot a restore returns it to (D9). Only the
        // active favourites are renumbered, closing the gap it leaves.
        if (place.IsFavourite)
            RenumberFavourites();

        // A failed save leaves it deleted in memory with the banner up (D1).
        return Persist();
    }

    /// <summary>
    /// Un-deletes <paramref name="place"/> exactly as it was: the same
    /// record, in the list slot it never left (D7), and — if it was a
    /// favourite — at its remembered bubble slot, clamped to the current
    /// row, with later bubbles shifted right (D9). Everything else about it
    /// (alias, DateAdded...) is unchanged.
    ///
    /// Refuses without changing anything if it is no longer in Recently
    /// Deleted (purged, or permanently deleted), is already active, or its
    /// alias or path/URL is now used by an active place — restoring it
    /// would create the very duplicate the validation rules forbid, so it
    /// stays in Recently Deleted for the conflict flow (D15).
    ///
    /// Like every other mutation this is a forward change, not a rollback
    /// (D1): if the save fails, the restored place stays active in memory
    /// and the unsaved-changes banner offers Retry.
    /// </summary>
    public ValidationResult TryRestore(Place place, out PersistenceResult persistence)
    {
        if (IsMutationBlocked(out persistence))
            return ValidationResult.Fail(BlockedMessage());

        persistence = PersistenceResult.Ok();

        if (!_places.Contains(place))
            return ValidationResult.Fail($"\"{place.Alias}\" is no longer in Recently Deleted.");

        if (place.DeletedAt is null)
            return ValidationResult.Fail($"\"{place.Alias}\" is already back in the list.");

        if (!ValidateAlias(place.Alias).Success)
            return ValidationResult.Fail($"Can't restore \"{place.Alias}\": that alias is now used by another place.");

        if (!ValidateResource(place.Resource, place.Type).Success)
            return ValidationResult.Fail($"Can't restore \"{place.Alias}\": its path/URL is now stored under another alias.");

        Undelete(place);
        persistence = Persist();
        return ValidationResult.Ok();
    }

    /// <summary>
    /// Clears DeletedAt and, for a favourite, returns it to its remembered
    /// bubble slot (D9): active favourites at or after that slot shift right
    /// by one, then RenumberFavourites closes any gap left by favourites
    /// that went in the meantime — so a slot past the end of today's row
    /// lands at its end. Plan 5.3 row 10: only active favourites shift; a
    /// deleted record's slot is its own memory and is never moved by
    /// another place's restore. The record is already in _places, so it is
    /// excluded by reference.
    /// </summary>
    private void Undelete(Place place)
    {
        place.DeletedAt = null;

        if (!place.IsFavourite)
            return;

        if (place.FavouriteOrder is { } slot)
        {
            foreach (var other in Active.Where(p => p.IsFavourite && !ReferenceEquals(p, place) && p.FavouriteOrder >= slot))
                other.FavouriteOrder++;
        }

        // A favourite with no remembered slot (only a hand-edited file can
        // hold one) sorts last in RenumberFavourites: a favourite always
        // comes back as a favourite (D9).
        RenumberFavourites();
    }

    /// <summary>Renumbers the active favourites to a dense 0..n-1. A deleted record keeps its FavouriteOrder untouched: it is the remembered slot a restore returns it to (D9).</summary>
    private void RenumberFavourites()
    {
        var favourites = Active.Where(p => p.IsFavourite).OrderBy(p => p.FavouriteOrder ?? int.MaxValue).ToList();
        for (var i = 0; i < favourites.Count; i++)
            favourites[i].FavouriteOrder = i;
    }

    // ---------------------------------------------------------------
    // Export / Import (SI §6.5 / §6.6)
    // ---------------------------------------------------------------

    /// <summary>
    /// Writes the given places to <paramref name="filePath"/> as a
    /// standalone PlacesStore JSON document at the current schema version,
    /// replacing any existing file atomically. Returns an error message on
    /// failure, or null on success.
    ///
    /// Places in Recently Deleted are left out even if a caller passes them
    /// (D16): an export is a list the user chose to keep or share, and
    /// Recently Deleted is a safety net nobody chose. So an export never
    /// carries a deletedAt key either (Place writes it only when set).
    /// </summary>
    public string? Export(IEnumerable<Place> places, string filePath)
    {
        try
        {
            var export = new PlacesStore
            {
                SchemaVersion = CurrentSchemaVersion,
                Places = places.Where(p => p is not null && p.DeletedAt is null).ToList()
            };
            var json = JsonSerializer.Serialize(export, JsonOptions);
            // Atomic for the same reason as places.json: exporting over an
            // earlier backup must never leave a half-written file in its place.
            WriteAtomically(filePath, json);
            return null;
        }
        catch (Exception ex)
        {
            return $"Couldn't write the export file: {ex.Message}";
        }
    }

    /// <summary>
    /// Writes <paramref name="contents"/> to a temp file beside
    /// <paramref name="path"/>, then moves it over <paramref name="path"/>
    /// in one filesystem operation. Used for exports; places.json itself
    /// goes through IPlacesStorage.Write, which also flushes to disk and
    /// keeps a .bak copy. On failure the temp file is removed and the
    /// exception rethrown.
    /// </summary>
    private static void WriteAtomically(string path, string contents)
    {
        var tempPath = path + ".tmp";
        try
        {
            File.WriteAllText(tempPath, contents);
            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            try
            {
                File.Delete(tempPath);
            }
            catch
            {
                // Best effort — the original error is the one worth reporting.
            }

            throw;
        }
    }

    /// <summary>
    /// Reads a previously-exported file and returns only the candidates
    /// that do NOT collide with anything already stored (SI §6.6 — an
    /// incoming item whose alias or resource collides is excluded before
    /// the user ever sees it as an option). Returns an error message
    /// instead of candidates if the file can't be read/parsed, or was
    /// written by a newer version (D17).
    ///
    /// The version decides how the file is read (D17): missing is treated
    /// as 1, as import always has; 1 goes through the same
    /// PlacesStoreMigration as the store, with this service's clock and
    /// zone, so its dates follow the same rule; 2 is read as is; anything
    /// newer is refused. Import stays lenient about a missing version where
    /// the store is strict because the risks differ: the store's gate stops
    /// a foreign file being loaded and then overwritten, while import is
    /// additive, reviewed row by row, and never writes the source file.
    ///
    /// Records carrying deletedAt are never offered — whether from a
    /// hand-edited file or a copied places.json, importing one into
    /// Recently Deleted is meaningless and importing it as active would
    /// resurrect something deleted elsewhere — and collisions are checked
    /// against active places only (D15), so a place matching only something
    /// in Recently Deleted is still offered.
    /// </summary>
    public (List<Place> candidates, string? errorMessage) GetImportCandidates(string filePath)
    {
        try
        {
            var json = File.ReadAllText(filePath);
            if (JsonNode.Parse(json, documentOptions: StoreDocumentOptions) is not JsonObject root)
                return (new List<Place>(), NotAnExportMessage);

            var version = 1;
            if (root["schemaVersion"] is { } versionNode)
            {
                if (versionNode is not JsonValue versionValue ||
                    versionValue.GetValueKind() != JsonValueKind.Number ||
                    !versionValue.TryGetValue(out version) ||
                    version < 1)
                {
                    // Below 1 is numeric but no build ever wrote it — the same
                    // reasoning as the store's gate (plan 5.1).
                    return (new List<Place>(), NotAnExportMessage);
                }

                if (version > CurrentSchemaVersion)
                    return (new List<Place>(), "That file was exported by a newer version of QuickerPlaces. Update QuickerPlaces to import it.");
            }

            // Import never logs the migration: it changes nothing stored, and
            // the file itself is never written back.
            if (version == 1)
                PlacesStoreMigration.MigrateV1ToV2(root, _time.LocalTimeZone, _time.GetUtcNow());

            var store = root.Deserialize<PlacesStore>(JsonOptions);
            var incoming = (store?.Places ?? new List<Place>())
                .Where(p => p is not null && p.DeletedAt is null)
                .ToList();

            var candidates = incoming
                .Where(p => !string.IsNullOrWhiteSpace(p.Alias) && !string.IsNullOrWhiteSpace(p.Resource))
                .Where(p => ValidateAlias(p.Alias).Success && ValidateResource(p.Resource, p.Type).Success)
                .ToList();

            // The file can collide with itself too (hand-edited, or two
            // exports merged): keep only the first of any repeated alias or
            // resource, so the checklist never offers an item CommitImport
            // would then silently skip.
            var seenAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenResources = new HashSet<(PlaceType, string)>(ResourceKeyComparer.Instance);
            var distinct = new List<Place>();
            foreach (var candidate in candidates)
            {
                var alias = candidate.Alias.Trim();
                var resourceKey = (candidate.Type, candidate.Resource.Trim());
                if (seenAliases.Contains(alias) || seenResources.Contains(resourceKey))
                    continue;

                seenAliases.Add(alias);
                seenResources.Add(resourceKey);
                distinct.Add(candidate);
            }

            return (distinct, null);
        }
        catch (Exception ex)
        {
            return (new List<Place>(), $"Couldn't read that file: {ex.Message}");
        }
    }

    private const string NotAnExportMessage = "That file isn't a QuickerPlaces export.";

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
                DateAdded = _time.GetUtcNow()
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
            // JsonNode rather than JsonDocument, so a version 1 document can
            // be migrated before it is bound to Place (D11). The version is
            // read from the same tree, so the gate below and the migration
            // can never disagree about what the file says.
            var root = JsonNode.Parse(json, documentOptions: StoreDocumentOptions) as JsonObject;

            // The absent/non-numeric-version case is deliberately Damaged,
            // not "assume version 1" (plan 5.3): every store this
            // application has ever written includes schemaVersion, so a
            // document without one — or with one that isn't a number — is
            // not an old-but-valid v1 store. It is damaged, or it is a
            // file this application never wrote at all.
            if (root?["schemaVersion"] is not JsonValue versionValue ||
                versionValue.GetValueKind() != JsonValueKind.Number ||
                !versionValue.TryGetValue<int>(out var version))
            {
                DiagnosticLog.Warn($"Places store at {_storage.StoreFilePath} has no usable schemaVersion; treating as damaged.");
                return (new List<Place>(), StoreLoadOutcome.Damaged);
            }

            // No build ever wrote a version below 1, so it is not a known
            // migration (roadmap §4.3) — before Phase 2 the "< current"
            // branch would have let 0 through as if it were current.
            if (version < 1)
            {
                DiagnosticLog.Warn($"Places store at {_storage.StoreFilePath} has schemaVersion {version}, which no version of QuickerPlaces writes; treating as damaged.");
                return (new List<Place>(), StoreLoadOutcome.Damaged);
            }

            if (version > CurrentSchemaVersion)
            {
                DiagnosticLog.Warn($"Places store at {_storage.StoreFilePath} has schemaVersion {version}, newer than this build's {CurrentSchemaVersion}.");
                return (new List<Place>(), StoreLoadOutcome.WrittenByNewerVersion);
            }

            if (version == 1)
            {
                // In memory only: this method never calls _storage.Write, so
                // the migrated store reaches disk with the next successful
                // save through Persist() (roadmap §4.3, Phase 1 test 15). If
                // that save fails, the file is still the untouched v1
                // original and the next launch migrates it again from the
                // same source values — which is why DateAdded is converted
                // exactly once (D11). A value the migration cannot convert
                // throws JsonException, caught below as Damaged (D6).
                var report = PlacesStoreMigration.MigrateV1ToV2(root, _time.LocalTimeZone, _time.GetUtcNow());

                // Counts and a zone id only, never an alias or a destination
                // (DiagnosticLog's privacy rule). The zone is logged because
                // it is the assumption roadmap §4.7 asks to be recorded.
                DiagnosticLog.Info(
                    $"Migrating places store at {_storage.StoreFilePath} from schemaVersion 1 to {CurrentSchemaVersion} in memory " +
                    $"(written with the next successful save): {report.Records} place(s); dateAdded converted to UTC exactly for " +
                    $"{report.ExactOffsets} that carried an offset, interpreted as local time in time zone \"{report.ZoneId}\" for " +
                    $"{report.InterpretedAsLocal} that had none, and set to the current time for {report.MissingDates} that were missing.");
            }

            var store = root.Deserialize<PlacesStore>(JsonOptions);

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

            // A hand-edited file can contain a bare `null` in the array;
            // drop it here rather than let it NRE the first grid binding.
            // Places in Recently Deleted are kept (plan 5.3 row 15): they
            // are part of the store, in their list slots (D7).
            var places = store.Places.Where(p => p is not null).ToList();

            // A hand-edited file can carry any offset; normalise so that
            // only UTC is ever held in memory, and so written back (§3).
            foreach (var place in places)
            {
                place.DateAdded = place.DateAdded.ToUniversalTime();
                place.DeletedAt = place.DeletedAt?.ToUniversalTime();
            }

            var deleted = places.Count(p => p.DeletedAt is not null);
            DiagnosticLog.Info($"Loaded {places.Count - deleted} place(s) and {deleted} in Recently Deleted from {_storage.StoreFilePath} (schemaVersion {version}).");
            return (places, StoreLoadOutcome.Ok);
        }
        catch (JsonException ex)
        {
            DiagnosticLog.Error($"Places store at {_storage.StoreFilePath} is not valid JSON, or could not be migrated.", ex);
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
            // Local time from the injected clock (D12): the file name is
            // for a person reading a folder listing, and a test can pin it.
            var quarantinedPath = _storage.Quarantine(_time.GetLocalNow());
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
            // Every record, deleted ones included: this is how Recently
            // Deleted persists (plan 5.3 row 17). The version is set here,
            // not left to PlacesStore's initialiser.
            var store = new PlacesStore { SchemaVersion = CurrentSchemaVersion, Places = _places };
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
            // second, less-protected file. Active and deleted are counted
            // separately, so the line matches what the user sees.
            var deleted = _places.Count(p => p.DeletedAt is not null);
            DiagnosticLog.Error(
                $"Failed to save {_places.Count - deleted} place(s) and {deleted} in Recently Deleted to {_storage.StoreFilePath}",
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

/// <summary>Matches import-dedupe keys the same way ValidateResource matches duplicates: same Type, case-insensitive exact Resource.</summary>
internal sealed class ResourceKeyComparer : IEqualityComparer<(PlaceType Type, string Resource)>
{
    public static readonly ResourceKeyComparer Instance = new();

    public bool Equals((PlaceType Type, string Resource) x, (PlaceType Type, string Resource) y)
        => x.Type == y.Type && string.Equals(x.Resource, y.Resource, StringComparison.OrdinalIgnoreCase);

    public int GetHashCode((PlaceType Type, string Resource) key)
        => HashCode.Combine(key.Type, StringComparer.OrdinalIgnoreCase.GetHashCode(key.Resource));
}
