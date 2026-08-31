namespace QuickerPlaces.Models;

/// <summary>
/// Which fields Views.PlaceFormDialog shows and which PlacesService
/// operation OK commits to — SI §6.1 resolved this as one combined dialog
/// (Alias + Resource together) for Add, since the template had no existing
/// data-entry-dialog precedent to follow instead.
/// </summary>
public enum PlaceFormMode
{
    /// <summary>Alias + folder path, both required. Commits via PlacesService.TryAdd.</summary>
    AddFolder,

    /// <summary>Alias + URL, both required. Commits via PlacesService.TryAdd.</summary>
    AddUrl,

    /// <summary>Alias only, prefilled with the current value. Commits via PlacesService.TryRenameAlias.</summary>
    RenameAlias,

    /// <summary>Path/URL only, prefilled with the current value. Commits via PlacesService.TryEditResource.</summary>
    EditResource
}
