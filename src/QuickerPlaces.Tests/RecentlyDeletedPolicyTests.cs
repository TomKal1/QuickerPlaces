using System;
using QuickerPlaces.Services;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>
/// Test 40 from the Phase 2 plan's section 7: "seven full days" is 168
/// hours of elapsed UTC time (D13), and the days-remaining number and text
/// the Recently Deleted dialog shows. Pure: no service, no clock.
/// </summary>
public sealed class RecentlyDeletedPolicyTests
{
    /// <summary>D, the instant of removal. Deliberately in a non-zero offset: only the instant matters.</summary>
    private static readonly DateTimeOffset D = new(2026, 3, 27, 21, 45, 0, TimeSpan.FromHours(1));

    private static readonly TimeSpan Tick = TimeSpan.FromTicks(1);

    /// <summary>
    /// Test 40: expired from exactly D + 7 d, not a tick earlier. D falls two
    /// days before Central Europe's spring-forward change; the answer is
    /// still 168 hours, because only elapsed time is counted, never local
    /// calendar days.
    /// </summary>
    [Fact]
    public void IsExpired_FromExactlySevenDaysOfElapsedTime()
    {
        Assert.Equal(TimeSpan.FromHours(168), RecentlyDeletedPolicy.RetentionPeriod);
        Assert.Equal(D + TimeSpan.FromHours(168), RecentlyDeletedPolicy.ExpiresAt(D));

        Assert.False(RecentlyDeletedPolicy.IsExpired(D, D));
        Assert.False(RecentlyDeletedPolicy.IsExpired(D, D + TimeSpan.FromDays(7) - Tick));
        Assert.True(RecentlyDeletedPolicy.IsExpired(D, D + TimeSpan.FromDays(7)));
        Assert.True(RecentlyDeletedPolicy.IsExpired(D, (D + TimeSpan.FromDays(7)).ToOffset(TimeSpan.FromHours(-5))));
    }

    /// <summary>Test 40: days remaining are the ceiling of the time left, clamped to 0..7 — 7, 7, 1, 0 at D, D + 1 tick, D + 6 d + 1 tick, D + 7 d.</summary>
    [Fact]
    public void DaysRemaining_IsTheCeilingOfTheTimeLeft_ClampedToZeroToSeven()
    {
        Assert.Equal(7, RecentlyDeletedPolicy.DaysRemaining(D, D));
        Assert.Equal(7, RecentlyDeletedPolicy.DaysRemaining(D, D + Tick));
        Assert.Equal(6, RecentlyDeletedPolicy.DaysRemaining(D, D + TimeSpan.FromDays(1)));
        Assert.Equal(1, RecentlyDeletedPolicy.DaysRemaining(D, D + TimeSpan.FromDays(6) + Tick));
        Assert.Equal(1, RecentlyDeletedPolicy.DaysRemaining(D, D + TimeSpan.FromDays(7) - Tick));
        Assert.Equal(0, RecentlyDeletedPolicy.DaysRemaining(D, D + TimeSpan.FromDays(7)));
        Assert.Equal(0, RecentlyDeletedPolicy.DaysRemaining(D, D + TimeSpan.FromDays(30)));
    }

    /// <summary>Test 40: a DeletedAt in the future (another machine's skewed clock) clamps to 7 and simply waits.</summary>
    [Fact]
    public void DaysRemaining_ForAFutureDeletedAt_ClampsToSeven()
    {
        Assert.Equal(7, RecentlyDeletedPolicy.DaysRemaining(D + TimeSpan.FromDays(3), D));
        Assert.False(RecentlyDeletedPolicy.IsExpired(D + TimeSpan.FromDays(3), D));
    }

    /// <summary>Test 40: the texts read "7 days", "2 days", "1 day", and "Expiring" once past expiry.</summary>
    [Fact]
    public void DaysRemainingText_ReadsDaysDayOrExpiring()
    {
        Assert.Equal("7 days", RecentlyDeletedPolicy.DaysRemainingText(D, D));
        Assert.Equal("2 days", RecentlyDeletedPolicy.DaysRemainingText(D, D + TimeSpan.FromDays(5)));
        Assert.Equal("1 day", RecentlyDeletedPolicy.DaysRemainingText(D, D + TimeSpan.FromDays(6) + Tick));
        Assert.Equal("Expiring", RecentlyDeletedPolicy.DaysRemainingText(D, D + TimeSpan.FromDays(7)));
    }
}
