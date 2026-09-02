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
        (_places, LoadFailed) = LoadFromDisk();
    }

    /// <summary>
    /// True if places.json existed but couldn't be read/parsed on startup
    /// (corrupt or from an incompatible future version). The service still
    /// starts with an empty list rather than crashing (SI §5) — MainWindow
    /// surfaces this once via a non-blocking MessageForm notice so the user
    /// knows their old data didn't silently vanish forever (the corrupt
    /// file is left on disk, untouched, until the next write overwrites it).
    /// </summary>
    public bool LoadFailed { get; }

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

    private (List<Place>, bool loadFailed) LoadFromDisk()
    {
        try
        {
            if (!_storage.Exists)
                return (new List<Place>(), false);

            var json = _storage.Read();
            var store = JsonSerializer.Deserialize<PlacesStore>(json, JsonOptions);
            return (store?.Places ?? new List<Place>(), false);
        }
        catch
        {
            // Corrupt or unreadable places file — start from an empty list
            // rather than crashing the app on launch (SI §5). The file on
            // disk is left as-is; LoadFailed lets the UI tell the user.
            //
            // This still lumps "damaged" and "could not be opened" into one
            // boolean — that distinction (D6) is a later step of this
            // phase, not this one, which is scoped to moving the existing
            // File.* calls behind IPlacesStorage with no behaviour change.
            return (new List<Place>(), true);
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
    /// resolves a startup recovery prompt (a corrupt file, or one written
    /// by a newer version). Nothing in this file sets this true yet: the
    /// classification that would (the schema-version gate and load-failure
    /// classification, D6) is a later step of this phase. The guard below
    /// is wired ahead of that step so every mutation already refuses to
    /// run while this is true, rather than a later step having to retrofit
    /// the check into seven methods and risk missing one.
    /// </summary>
    public bool IsRecoveryUnresolved { get; private set; }

    /// <summary>The message every mutation returns while <see cref="IsRecoveryUnresolved"/> is true. Set together with it.</summary>
    public string? RecoveryBlockedMessage { get; private set; }

    /// <summary>
    /// Test-only: simulates the version gate (a later step) having left
    /// recovery unresolved, so this step's guard — that a mutation while
    /// unresolved makes no in-memory change and writes nothing — can be
    /// proven before that gate exists. Production code must never call
    /// this; the real setter is the later step's load-failure
    /// classification.
    /// </summary>
    public void MarkRecoveryUnresolvedForTests(string message)
    {
        IsRecoveryUnresolved = true;
        RecoveryBlockedMessage = message;
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
