using System;
using System.Linq;
using QuickerPlaces.Services.Activity;
using QuickerPlaces.Tests.Fakes;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>
/// Which local day the tracker's time belongs to (Phase 9 plan D19): the
/// injected clock's zone, never the machine's; a split at local midnight;
/// and daylight-saving days of 23 and 25 hours. The roots here have no
/// dwell threshold, so time accrues from the first step.
/// </summary>
public sealed class FolderActivityTrackerDayTests
{
    private const string Acme = @"C:\Jobs\Acme";

    private static TrackedRootConfig Jobs(double dwellSeconds = 0)
        => new("jobs", @"C:\Jobs") { DwellThreshold = TimeSpan.FromSeconds(dwellSeconds) };

    private static TrackerHarness InAcmeFrom(DateTimeOffset utc, TimeZoneInfo zone, double dwellSeconds = 0)
    {
        var h = new TrackerHarness(new ManualTimeProvider(utc, zone), Jobs(dwellSeconds));
        h.Probe.ShowForeground(Acme);
        h.Tick();
        return h;
    }

    private static DateTimeOffset Utc(int month, int day, int hour, int minute = 0, double second = 0)
        => new DateTimeOffset(2026, month, day, hour, minute, 0, TimeSpan.Zero).AddSeconds(second);

    [Theory]
    [InlineData(15, 26)]    // 15:00 UTC is 01:00 the next day at UTC+10
    [InlineData(13, 25)]    // 13:00 UTC is 23:00 the same day
    public void TheDay_IsTheLocalDateInTheInjectedZone(int utcHour, int localDay)
    {
        var h = InAcmeFrom(Utc(9, 25, utcHour), TestZones.PlusTen);
        h.Steps(2);

        Assert.Equal(TimeSpan.FromSeconds(3), h.TimeOn(new DateOnly(2026, 9, localDay), Acme));
    }

    [Fact]
    public void TheDay_FollowsTheZoneNotTheMachine_WestOfUtc()
    {
        var h = InAcmeFrom(Utc(9, 25, 3), TestZones.MinusFive);   // 22:00 on the 24th
        h.Steps(2);

        Assert.Equal(TimeSpan.FromSeconds(3), h.TimeOn(new DateOnly(2026, 9, 24), Acme));
    }

    [Fact]
    public void AnIntervalAcrossLocalMidnight_IsSplitBetweenTheTwoDays()
    {
        // Local midnight at UTC+10 is 14:00 UTC. From 13:59:50, the seventh
        // step's interval runs from 13:59:59 to 14:00:00.5.
        var h = InAcmeFrom(Utc(9, 25, 13, 59, 50), TestZones.PlusTen);
        h.Steps(6);

        var straddling = h.Step();

        Assert.Equal(
            new[]
            {
                new ActivityInterval("jobs", Acme, new DateOnly(2026, 9, 25), TimeSpan.FromSeconds(1), false, Utc(9, 25, 14)),
                new ActivityInterval("jobs", Acme, new DateOnly(2026, 9, 26), TimeSpan.FromSeconds(0.5), false, Utc(9, 25, 14, 0, 0.5)),
            },
            straddling);
        Assert.Equal(TimeSpan.FromSeconds(10), h.TimeOn(new DateOnly(2026, 9, 25), Acme));
        Assert.Equal(TimeSpan.FromSeconds(0.5), h.TimeOn(new DateOnly(2026, 9, 26), Acme));
    }

    [Fact]
    public void AVisitThatStartsInASplitInterval_IsCountedOnce_OnTheFirstDay()
    {
        // Arrives 13:59:50, crosses its 9.5 s dwell at 13:59:59.5, and is
        // first seen past it at 14:00:00.5.
        var h = InAcmeFrom(Utc(9, 25, 13, 59, 50), TestZones.PlusTen, dwellSeconds: 9.5);
        h.Steps(6);

        var straddling = h.Step();

        Assert.Equal(new[] { true, false }, straddling.Select(i => i.StartsVisit));
        Assert.Equal(new[] { TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(0.5) }, straddling.Select(i => i.Duration));
        Assert.Equal(1, h.VisitsTo(Acme));
    }

    [Fact]
    public void TheDayClocksGoForward_Accrues23Hours()
    {
        // 2026-03-29 in Central European Time runs from 23:00 UTC on the
        // 28th (UTC+1) to 22:00 UTC on the 29th (UTC+2). Starting half a
        // second past the hour puts both midnights inside a step, so both
        // are split.
        var h = InAcmeFrom(Utc(3, 28, 22, 0, 0.5), TestZones.CentralEuropean);
        h.Steps(25 * 2400);                   // to 23:00:00.5 UTC on the 29th

        Assert.Equal(TimeSpan.FromSeconds(3599.5), h.TimeOn(new DateOnly(2026, 3, 28), Acme));
        Assert.Equal(TimeSpan.FromHours(23), h.TimeOn(new DateOnly(2026, 3, 29), Acme));
        Assert.Equal(TimeSpan.FromSeconds(3600.5), h.TimeOn(new DateOnly(2026, 3, 30), Acme));
    }

    [Fact]
    public void TheDayClocksGoBack_Accrues25Hours()
    {
        // 2026-10-25 in Central European Time runs from 22:00 UTC on the
        // 24th (UTC+2) to 23:00 UTC on the 25th (UTC+1).
        var h = InAcmeFrom(Utc(10, 24, 21, 0, 0.5), TestZones.CentralEuropean);
        h.Steps(27 * 2400);                   // to 00:00:00.5 UTC on the 26th

        Assert.Equal(TimeSpan.FromSeconds(3599.5), h.TimeOn(new DateOnly(2026, 10, 24), Acme));
        Assert.Equal(TimeSpan.FromHours(25), h.TimeOn(new DateOnly(2026, 10, 25), Acme));
        Assert.Equal(TimeSpan.FromSeconds(3600.5), h.TimeOn(new DateOnly(2026, 10, 26), Acme));
    }
}
