using System;

namespace QuickerPlaces.Tests.Fakes;

/// <summary>
/// A clock a test sets by hand (D12). Overrides only the two members
/// PlacesService reads — GetUtcNow() and LocalTimeZone — so GetLocalNow()
/// is the base class's own conversion, exactly as in production.
/// Microsoft.Extensions.TimeProvider.Testing does the same, but is a
/// package for fifteen lines.
/// </summary>
public sealed class ManualTimeProvider : TimeProvider
{
    public ManualTimeProvider(DateTimeOffset utcNow, TimeZoneInfo? localTimeZone = null)
    {
        UtcNow = utcNow;
        LocalZone = localTimeZone ?? TestZones.PlusTen;
    }

    /// <summary>A fixed instant, 2026-09-25 00:00 UTC, for tests that need a clock but not a particular time.</summary>
    public ManualTimeProvider()
        : this(new DateTimeOffset(2026, 9, 25, 0, 0, 0, TimeSpan.Zero))
    {
    }

    private DateTimeOffset _utcNow;

    /// <summary>
    /// What GetUtcNow() returns. Stays put until set or advanced. Stored
    /// with a zero offset whatever it is set with: the base class's
    /// GetLocalNow() adds the zone's offset to GetUtcNow().DateTime, so an
    /// offset left on this value would be counted twice.
    /// </summary>
    public DateTimeOffset UtcNow
    {
        get => _utcNow;
        set => _utcNow = value.ToUniversalTime();
    }

    /// <summary>
    /// The zone GetLocalNow() and the migration's offset-less branch use.
    /// Defaults to <see cref="TestZones.PlusTen"/>, never the machine's own.
    /// (A separate settable property because an override of the get-only
    /// LocalTimeZone cannot add a setter.)
    /// </summary>
    public TimeZoneInfo LocalZone { get; set; }

    public override DateTimeOffset GetUtcNow() => UtcNow;

    public override TimeZoneInfo LocalTimeZone => LocalZone;

    public void Advance(TimeSpan by) => UtcNow += by;
}
