using System;
using QuickerPlaces.Services.Activity;

namespace QuickerPlaces.Tests.Fakes;

/// <summary>An IMonotonicClock a test advances by hand, independently of the wall clock (D6).</summary>
public sealed class FakeMonotonicClock : IMonotonicClock
{
    public TimeSpan Elapsed { get; private set; }

    public void Advance(TimeSpan by) => Elapsed += by;
}
