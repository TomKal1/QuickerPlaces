using System;

namespace QuickerPlaces.Tests.Fakes;

/// <summary>
/// Deterministic time zones for the tests that care about local time (D11,
/// D12). Built with TimeZoneInfo.CreateCustomTimeZone rather than looked
/// up by id, so no test depends on the host's time zone database — Windows
/// ids and IANA ids differ, and a CI runner may have neither — or on the
/// machine's own zone.
/// </summary>
public static class TestZones
{
    /// <summary>UTC+10:00 all year, no daylight saving (the shape of Brisbane).</summary>
    public static TimeZoneInfo PlusTen { get; } = TimeZoneInfo.CreateCustomTimeZone(
        "Test/PlusTen", TimeSpan.FromHours(10), "(UTC+10:00) Test", "Test Plus Ten");

    /// <summary>UTC−05:00 all year, no daylight saving.</summary>
    public static TimeZoneInfo MinusFive { get; } = TimeZoneInfo.CreateCustomTimeZone(
        "Test/MinusFive", TimeSpan.FromHours(-5), "(UTC-05:00) Test", "Test Minus Five");

    /// <summary>
    /// UTC+01:00 with a +1 h daylight-saving rule from the last Sunday of
    /// March at 02:00 to the last Sunday of October at 03:00 (the end time
    /// is daylight time, as in Windows' rules) — Central European Time. On
    /// 2026-03-29, 02:00–03:00 does not exist; on 2026-10-25, 02:00–03:00
    /// happens twice. Checked against the real Europe/Berlin zone on .NET
    /// 10: the same offsets on both sides of each transition, and the same
    /// invalid and ambiguous hours.
    /// </summary>
    public static TimeZoneInfo CentralEuropean { get; } = CreateCentralEuropean();

    private static TimeZoneInfo CreateCentralEuropean()
    {
        var start = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 3, 5, DayOfWeek.Sunday);
        var end = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 3, 0, 0), 10, 5, DayOfWeek.Sunday);
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            DateTime.MinValue.Date, DateTime.MaxValue.Date, TimeSpan.FromHours(1), start, end);

        return TimeZoneInfo.CreateCustomTimeZone(
            "Test/CentralEuropean", TimeSpan.FromHours(1), "(UTC+01:00) Test Central European",
            "Test Central European Standard Time", "Test Central European Summer Time", new[] { rule });
    }
}
