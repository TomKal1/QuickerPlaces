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

    private readonly string _placesFilePath;
    private readonly List<Place> _places;

    public PlacesService()
        : this(DefaultPlacesFilePath())
    {
    }

    /// <summary>
    /// Backs the service with an explicit file instead of the default
    /// %AppData% location — used by the unit tests to run against a
    /// throwaway temp file rather than the user's real places.json.
    /// </summary>
    public PlacesService(string placesFilePath)
    {
        _placesFilePath = placesFilePath;

        var folder = Path.GetDirectoryName(placesFilePath);
        if (!string.IsNullOrEmpty(folder))
            Directory.CreateDirectory(folder);

        (_places, LoadFailed) = LoadFromDisk();
    }

    private static string DefaultPlacesFilePath()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(root, AppInfo.Publisher, AppInfo.Name, "places.json");
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
    public string PlacesFilePath => _placesFilePath;

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

    public ValidationResult TryAdd(string alias, PlaceType type, string resource, out Place? created)
    {
        created = null;

        var aliasResult = ValidateAlias(alias);
        if (!aliasResult.Success)
            return aliasResult;

        var resourceResult = ValidateResource(resource, type);
        if (!resourceResult.Success)
            return resourceResult;

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
        SaveToDisk();

        created = place;
        return ValidationResult.Ok();
    }

    public ValidationResult TryRenameAlias(Place place, string newAlias)
    {
        var result = ValidateAlias(newAlias, excluding: place);
        if (!result.Success)
            return result;

        place.Alias = newAlias.Trim();
        SaveToDisk();
        return ValidationResult.Ok();
    }

    public ValidationResult TryEditResource(Place place, string newResource)
    {
        var result = ValidateResource(newResource, place.Type, excluding: place);
        if (!result.Success)
            return result;

        place.Resource = newResource.Trim();
        SaveToDisk();
        return ValidationResult.Ok();
    }

    /// <summary>Turns favouriting on/off. Turning on appends to the end of the favourite order; turning off renumbers the remaining favourites so FavouriteOrder stays a dense 0..n-1 sequence.</summary>
    public void ToggleFavourite(Place place)
    {
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

        SaveToDisk();
    }

    /// <summary>Reassigns FavouriteOrder for every current favourite to match <paramref name="orderedFavourites"/> (0-based, dense). Used after a bubble drag-reorder.</summary>
    public void SetFavouriteOrder(IReadOnlyList<Place> orderedFavourites)
    {
        for (var i = 0; i < orderedFavourites.Count; i++)
            orderedFavourites[i].FavouriteOrder = i;

        SaveToDisk();
    }

    public void Remove(Place place)
    {
        _places.Remove(place);
        if (place.IsFavourite)
            RenumberFavourites();
        SaveToDisk();
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
                .Where(p => p is not null && !string.IsNullOrWhiteSpace(p.Alias) && !string.IsNullOrWhiteSpace(p.Resource))
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

    /// <summary>
    /// Adds the user-selected import candidates as new Place records (never
    /// the candidate instances themselves — those came from a deserialized
    /// file and are never entered into the live store as-is) and returns
    /// the ones actually added. Re-validates each one against the current
    /// store at commit time (defensive — the store could in principle have
    /// changed since the preview was shown) and silently skips any that no
    /// longer pass.
    /// </summary>
    public List<Place> CommitImport(IEnumerable<Place> selectedCandidates)
    {
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

        if (imported.Count > 0)
            SaveToDisk();

        return imported;
    }

    // ---------------------------------------------------------------
    // Disk I/O
    // ---------------------------------------------------------------

    private (List<Place>, bool loadFailed) LoadFromDisk()
    {
        try
        {
            if (!File.Exists(_placesFilePath))
                return (new List<Place>(), false);

            var json = File.ReadAllText(_placesFilePath);
            var store = JsonSerializer.Deserialize<PlacesStore>(json, JsonOptions);
            // A hand-edited file can contain a bare `null` in the array;
            // drop it here rather than let it NRE the first grid binding.
            var places = store?.Places?.Where(p => p is not null).ToList() ?? new List<Place>();
            return (places, false);
        }
        catch
        {
            // Corrupt or unreadable places file — start from an empty list
            // rather than crashing the app on launch (SI §5). The file on
            // disk is left as-is; LoadFailed lets the UI tell the user.
            return (new List<Place>(), true);
        }
    }

    /// <summary>
    /// Writes places.json atomically: serialize to a temp file in the same
    /// directory, then replace the real file in one filesystem operation
    /// (File.Move with overwrite, which uses an atomic rename/replace on
    /// Windows) so a crash or power-loss mid-write can never leave a
    /// truncated or half-written places.json behind (SI §5).
    /// </summary>
    private void SaveToDisk()
    {
        try
        {
            var store = new PlacesStore { Places = _places };
            var json = JsonSerializer.Serialize(store, JsonOptions);

            var tempPath = _placesFilePath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, _placesFilePath, overwrite: true);
        }
        catch
        {
            // Best-effort persistence: a save failure (disk full, file
            // locked by another process, etc.) shouldn't crash the app or
            // block the in-memory change the user just made — it just
            // means that one change might not survive an unclean exit.
        }
    }
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
