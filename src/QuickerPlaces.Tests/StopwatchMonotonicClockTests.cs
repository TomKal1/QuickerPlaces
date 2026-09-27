using System;
using System.Threading;
using QuickerPlaces.Services.Activity;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>
/// The production IMonotonicClock (Phase 9 plan D6). The one test in the
/// suite that reads a real clock, because a Stopwatch is what it wraps; it
/// only checks that time starts near zero and moves forward.
/// </summary>
public sealed class StopwatchMonotonicClockTests
{
    [Fact]
    public void StartsNearZero_AndOnlyMovesForward()
    {
        var clock = new StopwatchMonotonicClock();
        var first = clock.Elapsed;

        Assert.InRange(first, TimeSpan.Zero, TimeSpan.FromSeconds(1));
        Assert.True(SpinWait.SpinUntil(() => clock.Elapsed > first, TimeSpan.FromSeconds(5)));
        Assert.True(clock.Elapsed >= first);
    }
}
