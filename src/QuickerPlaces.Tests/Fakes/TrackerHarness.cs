using System;
using System.Collections.Generic;
using System.Linq;
using QuickerPlaces.Services.Activity;

namespace QuickerPlaces.Tests.Fakes;

/// <summary>
/// A FolderActivityTracker wired to scripted fakes, with both clocks
/// advanced together, and every interval it emitted kept for the
/// assertions. Steps default to 1.5 s, the foreground poll interval (D2).
/// </summary>
public sealed class TrackerHarness
{
    public TrackerHarness(params TrackedRootConfig[] roots)
        : this(new ManualTimeProvider(), roots)
    {
    }

    public TrackerHarness(ManualTimeProvider time, params TrackedRootConfig[] roots)
    {
        Time = time;
        Tracker = new FolderActivityTracker(Probe, Presence, Clock, Time);
        Tracker.SetRoots(roots);
    }

    public FakeShellWindowProbe Probe { get; } = new();

    public FakeUserPresence Presence { get; } = new();

    public FakeMonotonicClock Clock { get; } = new();

    public ManualTimeProvider Time { get; }

    public FolderActivityTracker Tracker { get; }

    /// <summary>Every interval the tracker has emitted, in order.</summary>
    public List<ActivityInterval> Recorded { get; } = new();

    public IReadOnlyList<ActivityInterval> Tick()
    {
        var intervals = Tracker.Tick();
        Recorded.AddRange(intervals);
        return intervals;
    }

    /// <summary>Advances the monotonic and wall clocks together, then ticks.</summary>
    public IReadOnlyList<ActivityInterval> Step(TimeSpan by)
    {
        Clock.Advance(by);
        Time.Advance(by);
        return Tick();
    }

    public IReadOnlyList<ActivityInterval> Step(double seconds = 1.5) => Step(TimeSpan.FromSeconds(seconds));

    /// <summary><paramref name="count"/> steps of <paramref name="seconds"/> each.</summary>
    public void Steps(int count, double seconds = 1.5)
    {
        for (var i = 0; i < count; i++)
            Step(seconds);
    }

    public TimeSpan TimeIn(string folder)
        => Sum(Recorded.Where(i => i.Folder == folder));

    public TimeSpan TimeOn(DateOnly date, string folder)
        => Sum(Recorded.Where(i => i.Folder == folder && i.Date == date));

    public int VisitsTo(string folder)
        => Recorded.Count(i => i.Folder == folder && i.StartsVisit);

    private static TimeSpan Sum(IEnumerable<ActivityInterval> intervals)
        => intervals.Aggregate(TimeSpan.Zero, (total, i) => total + i.Duration);
}
