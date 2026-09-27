using System;

namespace QuickerPlaces.Services.Activity;

/// <summary>
/// A clock that only moves forward, for measuring how long a folder was on
/// screen (Phase 9 plan D6). The app's implementation is a
/// <c>Stopwatch</c>; the wall clock, which the user or a time sync can
/// move, only decides which day an interval belongs to (D19).
/// </summary>
public interface IMonotonicClock
{
    /// <summary>Time since an arbitrary, fixed starting point.</summary>
    TimeSpan Elapsed { get; }
}
