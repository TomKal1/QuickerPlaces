using System;
using QuickerPlaces.Services.Activity;
using Xunit;

namespace QuickerPlaces.Tests.Fakes;

/// <summary>
/// Shorthand for the activity store's tests. The default clock is
/// ManualTimeProvider's 2026-09-25 00:00 UTC in UTC+10, so "today" is
/// 2026-09-25 local.
/// </summary>
public static class ActivityFixtures
{
    public static readonly DateOnly Today = new(2026, 9, 25);

    public const string Acme = @"C:\Jobs\Acme";

    public const string Beta = @"C:\Jobs\Beta";

    public static ActivityInterval Interval(
        string rootId, string folder, DateOnly date, double seconds, bool startsVisit = false, DateTimeOffset? lastSeenAt = null)
        => new(rootId, folder, date, TimeSpan.FromSeconds(seconds), startsVisit,
            lastSeenAt ?? new DateTimeOffset(date.ToDateTime(new TimeOnly(1, 0)), TimeSpan.Zero));

    /// <summary>Adds <paramref name="path"/> as a root and asserts it was accepted and saved.</summary>
    public static ActivityRootSnapshot AddRoot(ActivityStore store, string path = @"C:\Jobs")
    {
        var result = store.TryAddRoot(path, null, out var root, out var persistence);
        Assert.True(result.Success, result.ErrorMessage);
        Assert.True(persistence.Saved, persistence.UserMessage);
        return root!;
    }
}
