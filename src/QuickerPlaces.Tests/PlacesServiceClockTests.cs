using System;
using QuickerPlaces.Models;
using QuickerPlaces.Services;
using QuickerPlaces.Tests.Fakes;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>
/// Test 1 from the Phase 2 plan's section 7: the injected clock (D12) is
/// really wired through. The quarantine timestamp is the one place that
/// used the clock before Phase 2, so it is the first seam proven.
/// </summary>
public sealed class PlacesServiceClockTests
{
    /// <summary>Test 1: the quarantine file is named from the injected clock's local time, not the machine's.</summary>
    [Fact]
    public void QuarantineFile_IsNamedFromTheInjectedClocksLocalTime()
    {
        var storage = new FakePlacesStorage { ContentsToReturn = "{ not valid json" };
        // 05:06:07 UTC is 15:06:07 in PlusTen. Whatever zone the machine
        // running the test is in, only the injected one may appear.
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero), TestZones.PlusTen);
        var service = new PlacesService(storage, clock);
        Assert.Equal(StoreLoadOutcome.Damaged, service.LoadOutcome);

        var result = service.QuarantineAndStartEmpty();

        Assert.True(result.Saved, result.UserMessage);
        Assert.Equal(@"C:\fake\places.corrupt-20260304-150607.json", storage.QuarantinedPath);
    }

    /// <summary>UtcNow is the injected clock's instant, so the dialog's countdown and the purge read the same time (D12).</summary>
    [Fact]
    public void UtcNow_ReadsTheInjectedClock()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 9, 25, 12, 0, 0, TimeSpan.Zero));
        var service = new PlacesService(new FakePlacesStorage(), clock);

        Assert.Equal(clock.UtcNow, service.UtcNow);

        clock.Advance(TimeSpan.FromHours(1));

        Assert.Equal(new DateTimeOffset(2026, 9, 25, 13, 0, 0, TimeSpan.Zero), service.UtcNow);
    }
}
