using System;

namespace QuickerPlaces.Services;

/// <summary>
/// How long a removed place stays in Recently Deleted, and how that is
/// shown (roadmap §4.10, D13). "Seven full days" is 168 hours of elapsed
/// UTC time from the moment of removal: calendar-day counting would keep a
/// place anywhere from six to seven days depending on the time of day it
/// was removed, which fails "full", and counting in UTC makes
/// daylight-saving days irrelevant. The place is removed at the first
/// purge point at or after expiry (PlacesService, D14), so seven days is a
/// guaranteed minimum, not an exact deadline.
///
/// Pure and UI-free, linked into the test project (D21): the purge and the
/// Recently Deleted dialog's countdown both come from here, so they cannot
/// disagree.
/// </summary>
public static class RecentlyDeletedPolicy
{
    /// <summary>Seven days of elapsed time — 168 hours, whatever the calendar does in between (D13).</summary>
    public static readonly TimeSpan RetentionPeriod = TimeSpan.FromDays(7);

    private static readonly int RetentionDays = (int)RetentionPeriod.TotalDays;

    /// <summary>The instant a place removed at <paramref name="deletedAt"/> may be purged from.</summary>
    public static DateTimeOffset ExpiresAt(DateTimeOffset deletedAt) => deletedAt + RetentionPeriod;

    /// <summary>
    /// True from exactly <see cref="ExpiresAt"/> onwards. A clock set
    /// backwards only lengthens the wait; one set forwards shortens it,
    /// which is accepted (D13).
    /// </summary>
    public static bool IsExpired(DateTimeOffset deletedAt, DateTimeOffset now) => now >= ExpiresAt(deletedAt);

    /// <summary>
    /// Ceiling of the whole days left, clamped to 0..7 (D13): 7 just after
    /// removal, 1 within the last 24 hours, 0 once expired but not yet
    /// purged. A DeletedAt in the future — another machine's skewed clock,
    /// arriving through a roaming profile — shows 7 and simply waits.
    /// </summary>
    public static int DaysRemaining(DateTimeOffset deletedAt, DateTimeOffset now)
    {
        var left = ExpiresAt(deletedAt) - now;
        if (left <= TimeSpan.Zero)
            return 0;

        // Whole days rounded up, in ticks so a remainder of one tick still
        // counts as a day: "1 day" means "less than a day left", never "none".
        var days = (left.Ticks + TimeSpan.TicksPerDay - 1) / TimeSpan.TicksPerDay;
        return (int)Math.Min(days, RetentionDays);
    }

    /// <summary>"7 days" … "2 days", "1 day", or "Expiring" once past expiry and awaiting the next purge — still restorable until then.</summary>
    public static string DaysRemainingText(DateTimeOffset deletedAt, DateTimeOffset now) => DaysRemaining(deletedAt, now) switch
    {
        0 => "Expiring",
        1 => "1 day",
        var days => $"{days} days"
    };
}
