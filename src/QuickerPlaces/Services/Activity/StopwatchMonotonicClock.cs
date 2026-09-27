using System;
using System.Diagnostics;

namespace QuickerPlaces.Services.Activity;

/// <summary>
/// The production IMonotonicClock (Phase 9 plan D6): time since this clock
/// was created, from the high-resolution performance counter, which the
/// user and time syncs cannot move.
/// </summary>
public sealed class StopwatchMonotonicClock : IMonotonicClock
{
    private readonly long _start = Stopwatch.GetTimestamp();

    public TimeSpan Elapsed => Stopwatch.GetElapsedTime(_start);
}
