namespace QuickerPlaces.Models;

/// <summary>The kind of resource a <see cref="Place"/> points at, which drives both its validation rules and its Open action.</summary>
public enum PlaceType
{
    /// <summary>An absolute folder path, opened in File Explorer.</summary>
    Folder,

    /// <summary>A URL, opened in the system default browser.</summary>
    Url
}
