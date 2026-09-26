using System;
using System.Globalization;
using QuickerPlaces.Models;
using QuickerPlaces.Services;

namespace QuickerPlaces.ViewModels;

/// <summary>
/// One row of the Recently Deleted dialog (plan 5.5): a deleted place and
/// the texts shown for it at one instant. Immutable, because the dialog
/// rebuilds every row from PlacesService.RecentlyDeleted after each action,
/// with the service's own clock (D12) — so the countdown a user reads and
/// the purge that follows it come from the same clock and the same
/// <see cref="RecentlyDeletedPolicy"/>, and cannot disagree.
///
/// UI-free and linked into the test project (D21): every text the dialog
/// shows for a row is decided here, and the view only binds.
/// </summary>
public sealed class RecentlyDeletedRowViewModel
{
    private readonly TimeZoneInfo _localZone;

    /// <param name="place">A place in Recently Deleted; its DeletedAt must be set.</param>
    /// <param name="now">The instant the countdown is measured from — PlacesService.UtcNow.</param>
    /// <param name="localZone">The zone dates are shown in. Defaults to the machine's own; tests pass a fixed one.</param>
    public RecentlyDeletedRowViewModel(Place place, DateTimeOffset now, TimeZoneInfo? localZone = null)
    {
        ArgumentNullException.ThrowIfNull(place);
        if (place.DeletedAt is not { } deletedAt)
            throw new ArgumentException($"\"{place.Alias}\" is not in Recently Deleted.", nameof(place));

        Place = place;
        DeletedAt = deletedAt;
        Now = now;
        _localZone = localZone ?? TimeZoneInfo.Local;
    }

    public Place Place { get; }

    /// <summary>When it was removed, in UTC — the Deleted and Days remaining columns sort on this.</summary>
    public DateTimeOffset DeletedAt { get; }

    public DateTimeOffset Now { get; }

    public string Alias => Place.Alias;

    /// <summary>"Folder" or "URL", as in the main grid.</summary>
    public string TypeLabel => Place.Type == PlaceType.Folder ? "Folder" : "URL";

    /// <summary>The same type glyph the main grid shows beside the alias.</summary>
    public string TypeGlyph => PlaceViewModel.GlyphFor(Place.Type);

    public string Resource => Place.Resource;

    /// <summary>When it was removed, as a wall-clock time in the local zone. Stored in UTC (§3), so showing it raw would be hours out.</summary>
    public DateTime DeletedLocal => TimeZoneInfo.ConvertTime(DeletedAt, _localZone).DateTime;

    /// <summary>The Deleted column: local date and short time in the user's own format ("g").</summary>
    public string DeletedText => DeletedLocal.ToString("g", CultureInfo.CurrentCulture);

    /// <summary>The instant it becomes due for purging (D13).</summary>
    public DateTimeOffset ExpiresAt => RecentlyDeletedPolicy.ExpiresAt(DeletedAt);

    public bool IsExpired => RecentlyDeletedPolicy.IsExpired(DeletedAt, Now);

    public int DaysRemaining => RecentlyDeletedPolicy.DaysRemaining(DeletedAt, Now);

    /// <summary>The Days remaining column: "7 days" … "1 day", or "Expiring".</summary>
    public string DaysRemainingText => RecentlyDeletedPolicy.DaysRemainingText(DeletedAt, Now);

    /// <summary>
    /// The Days remaining cell's tooltip: the exact expiry in local time.
    /// It says "at least" because a place is purged at the first start or
    /// save after that instant, not at it (D14) — and once that instant
    /// has passed, that it can still be restored until then.
    /// </summary>
    public string ExpiryToolTip
    {
        get
        {
            var expiresLocal = TimeZoneInfo.ConvertTime(ExpiresAt, _localZone).DateTime.ToString("g", CultureInfo.CurrentCulture);
            return IsExpired
                ? $"Its 7 days ended {expiresLocal}. It will be deleted for good the next time QuickerPlaces starts or saves; restore it now to keep it."
                : $"Kept until at least {expiresLocal}, then deleted for good the next time QuickerPlaces starts or saves.";
        }
    }
}
