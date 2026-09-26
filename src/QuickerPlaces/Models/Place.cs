using System;
using System.Text.Json.Serialization;

namespace QuickerPlaces.Models;

/// <summary>
/// One remembered "place" — a folder path or URL filed under a memorable
/// Alias. This is the persisted record (see PlacesStore/PlacesService);
/// ViewModels/PlaceViewModel.cs wraps it for data binding.
/// </summary>
public sealed class Place
{
    /// <summary>
    /// Stable identity for this place (Phase 3 D27). Assigned once — by the v2 → v3
    /// migration, TryAdd, or import — and never changed by rename, edit, remove or
    /// restore. Declared first so it is written first. Phase 5 keys per-file
    /// application choices by it; nothing in Phase 3 displays it.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>User-facing unique name. Uniqueness is case-insensitive ("Docs" and "docs" collide) — enforced by PlacesService, not by this type.</summary>
    public required string Alias { get; set; }

    /// <summary>Whether <see cref="Resource"/> is a folder path or a URL. Determines validation rules and the Open action.</summary>
    public PlaceType Type { get; set; }

    /// <summary>Absolute folder path, or a URL. Duplicate-checked as an exact case-insensitive string match against other places of the same Type — deliberately not normalized (see SI §6.2).</summary>
    public required string Resource { get; set; }

    /// <summary>Whether this place renders as a one-click bubble above the grid. For a place in Recently Deleted, whether it was one when removed (D9).</summary>
    public bool IsFavourite { get; set; }

    /// <summary>User-controlled manual ordering for the favourite bubbles. Null when not a favourite; assigned/renumbered by PlacesService whenever the favourite set or its order changes. Only active places take part in that numbering; for a place in Recently Deleted this is the bubble slot it is restored to (D9).</summary>
    public int? FavouriteOrder { get; set; }

    /// <summary>
    /// When this place was first added, in UTC. Converted from a local
    /// DateTime by the v1 → v2 migration (PlacesStoreMigration). Stamped by
    /// PlacesService from its injected clock; this initialiser only matters
    /// for a v2 record hand-edited to lose the field.
    /// </summary>
    public DateTimeOffset DateAdded { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Null for an active place. Set (UTC) when the place is removed: it is then in
    /// Recently Deleted until restored, permanently deleted, or purged seven full days
    /// later (RecentlyDeletedPolicy). Set and cleared only by PlacesService. While set,
    /// IsFavourite/FavouriteOrder are a remembered bubble slot, not a live position (D9).
    /// Not written at all while null (D16), which keeps an active record's on-disk
    /// shape as close to v1's as it can be.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? DeletedAt { get; set; }

    /// <summary>
    /// When QuickerPlaces last launched this place and Windows accepted it (Phase 3 D24),
    /// in UTC; null if never. Written only by PlacesService.RecordOpen. Not written to
    /// JSON while null, so a never-opened record carries no date it doesn't have.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? LastOpenedAt { get; set; }

    /// <summary>How many times QuickerPlaces has launched this place and Windows accepted it (D24). Saturates at int.MaxValue.</summary>
    public int OpenCount { get; set; }
}
