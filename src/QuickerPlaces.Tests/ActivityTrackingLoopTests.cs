using System;
using System.Runtime.InteropServices;
using QuickerPlaces.Services.Activity;
using QuickerPlaces.Tests.Fakes;
using Xunit;
using static QuickerPlaces.Tests.Fakes.ActivityFixtures;

namespace QuickerPlaces.Tests;

/// <summary>
/// What the tracking host does on each wake (Phase 9 plan D2, D6, D10,
/// D26): tick into the store, flush every five minutes, on lock, suspend,
/// idle and stop, and sleep without sampling while locked, idle, or with
/// nothing to track. The loop is the host's whole decision logic; the host
/// itself only waits on a thread for as long as NextWait says.
/// </summary>
public sealed class ActivityTrackingLoopTests
{
    private readonly FakeShellWindowProbe _probe = new();
    private readonly FakeUserPresence _presence = new();
    private readonly FakeMonotonicClock _clock = new();
    private readonly ManualTimeProvider _time = new();
    private readonly FakePlacesStorage _storage = new();
    private readonly ActivityStore _store;
    private readonly ActivityTrackingLoop _loop;

    public ActivityTrackingLoopTests()
    {
        _store = new ActivityStore(_storage, _time);
        var tracker = new FolderActivityTracker(_probe, _presence, _clock, _time);
        _loop = new ActivityTrackingLoop(tracker, _store, _presence, _clock);
        _probe.ShowForeground(Acme);
    }

    private void Advance(TimeSpan by)
    {
        _clock.Advance(by);
        _time.Advance(by);
    }

    /// <summary>Sleeps for as long as the loop asks, then wakes it with no signal.</summary>
    private void Run(int wakes)
    {
        for (var i = 0; i < wakes; i++)
        {
            Advance(_loop.NextWait ?? throw new InvalidOperationException("The loop is waiting for a signal."));
            _loop.Wake();
        }
    }

    private string StartTracking()
    {
        var root = AddRoot(_store).RootId;
        _loop.Wake(TrackingSignal.RootsChanged);
        return root;
    }

    private TimeSpan TimeInAcme(string root)
        => _store.QueryPeriod(root, Today, Today)!.Folders is { Count: > 0 } folders ? folders[0].Time : TimeSpan.Zero;

    [Fact]
    public void WithNothingToTrack_NothingIsSampled_AndItWaitsForAChange()
    {
        _loop.Start();
        _loop.Wake();

        Assert.Equal(0, _probe.Samples);
        Assert.Null(_loop.NextWait);
    }

    [Fact]
    public void AnUnavailableStore_IsNeverSampled()
    {
        var storage = new FakePlacesStorage { ContentsToReturn = "{}", ReadThrows = new System.IO.IOException() };
        var store = new ActivityStore(storage, _time);
        var loop = new ActivityTrackingLoop(new FolderActivityTracker(_probe, _presence, _clock, _time), store, _presence, _clock);

        loop.Start();
        loop.Wake(TrackingSignal.RootsChanged);

        Assert.Equal(0, _probe.Samples);
        Assert.Null(loop.NextWait);
    }

    [Fact]
    public void ANewRoot_StartsSampling_AtThePollInterval()
    {
        _loop.Start();
        StartTracking();

        Assert.Equal(1, _probe.Samples);
        Assert.Equal(FolderActivityTracker.ForegroundPollInterval, _loop.NextWait);
    }

    [Fact]
    public void ABriefExplorerVisitAfterAnotherApp_IsObservedAndCounted()
    {
        _loop.Start();
        _probe.CloseAll();
        var root = StartTracking();
        Run(2); // Another app has been in front; Explorer then gets ten seconds.
        _probe.ShowForeground(Acme);

        var elapsed = TimeSpan.Zero;
        var visitLength = TimeSpan.FromSeconds(10);
        while (_loop.NextWait is { } wait && elapsed + wait <= visitLength)
        {
            Advance(wait);
            _loop.Wake();
            elapsed += wait;
        }
        Advance(visitLength - elapsed);
        _probe.CloseAll();
        _loop.Wake();

        Assert.True(TimeInAcme(root) > TimeSpan.Zero);
        Assert.Equal(1, _store.QueryPeriod(root, Today, Today)!.Folders[0].Visits);
    }

    [Fact]
    public void TrayPause_FlushesAndSleepsWithoutSampling_UntilResume()
    {
        _loop.Start();
        StartTracking();
        Run(5);
        var beforePause = _probe.Samples;

        _loop.Wake(TrackingSignal.Paused);
        Assert.Null(_loop.NextWait);
        Assert.Equal(beforePause, _probe.Samples);
        Assert.False(_store.HasUnsavedChanges);

        Advance(TimeSpan.FromHours(1));
        _loop.Wake(TrackingSignal.TrackingResumed);
        Assert.Equal(beforePause + 1, _probe.Samples);
        Assert.Equal(FolderActivityTracker.ForegroundPollInterval, _loop.NextWait);
    }

    [Fact]
    public void Ticks_RecordIntoTheStore_WithoutWriting()
    {
        _loop.Start();
        var root = StartTracking();
        var writes = _storage.WriteCount;

        Run(10);                                        // 15 s: crossed the 5 s dwell at t = 6

        Assert.Equal(TimeSpan.FromSeconds(10), TimeInAcme(root));
        Assert.Equal(writes, _storage.WriteCount);
    }

    [Fact]
    public void TheStore_IsFlushedEveryFiveMinutes_AndNoWaitOverrunsTheFlush()
    {
        _loop.Start();
        StartTracking();
        var writes = _storage.WriteCount;

        Run(199);                                       // 298.5 s
        Assert.Equal(writes, _storage.WriteCount);

        Run(1);                                         // 300 s
        Assert.Equal(writes + 1, _storage.WriteCount);

        _probe.CloseAll();                              // active-user checks stay at 1.5 s
        Run(198);                                       // 597 s
        Assert.Equal(writes + 1, _storage.WriteCount);
        Assert.Equal(TimeSpan.FromSeconds(1.5), _loop.NextWait);

        Advance(TimeSpan.FromSeconds(1));
        _loop.Wake();                                   // 598 s: wait is capped at the 600 s flush
        Run(1);                                         // 599.5 s
        Assert.Equal(TimeSpan.FromSeconds(0.5), _loop.NextWait);
        Run(1);                                         // 600 s
        Assert.Equal(writes + 2, _storage.WriteCount);
    }

    [Fact]
    public void ALock_Flushes_StopsSampling_AndItsGapIsNeverCounted()
    {
        _loop.Start();
        var root = StartTracking();
        Run(10);
        var writes = _storage.WriteCount;
        var samples = _probe.Samples;

        _presence.SessionLocked = true;
        _loop.Wake(TrackingSignal.Locked);

        Assert.Equal(writes + 1, _storage.WriteCount);
        Assert.Null(_loop.NextWait);
        Assert.Equal(samples, _probe.Samples);

        Advance(TimeSpan.FromHours(1));
        _presence.SessionLocked = false;
        _loop.Wake(TrackingSignal.Unlocked);

        Assert.Equal(samples + 1, _probe.Samples);
        Assert.Equal(TimeSpan.FromSeconds(10), TimeInAcme(root));
        Run(1);
        Assert.Equal(TimeSpan.FromSeconds(11.5), TimeInAcme(root));
    }

    [Fact]
    public void ASuspend_Flushes_AndTheResumeCountsNothingForTheGap()
    {
        _loop.Start();
        var root = StartTracking();
        Run(10);
        var writes = _storage.WriteCount;

        var samples = _probe.Samples;
        _loop.Wake(TrackingSignal.Suspending);
        Assert.Equal(writes + 1, _storage.WriteCount);
        Assert.Null(_loop.NextWait);
        Assert.Equal(samples, _probe.Samples);

        Advance(TimeSpan.FromHours(60));
        _loop.Wake(TrackingSignal.Resumed);

        Assert.Equal(TimeSpan.FromSeconds(10), TimeInAcme(root));
    }

    [Fact]
    public void IdlePastEveryRootsTimeout_FlushesOnce_StopsSampling_AndLooksAgainEvery15Seconds()
    {
        _loop.Start();
        var root = StartTracking();
        Run(10);
        var writes = _storage.WriteCount;
        var samples = _probe.Samples;

        _presence.IdleFor = TimeSpan.FromMinutes(5);
        Run(1);
        Assert.Equal(writes + 1, _storage.WriteCount);
        Assert.Equal(TimeSpan.FromSeconds(15), _loop.NextWait);

        Run(40);                                        // ten minutes away
        Assert.Equal(writes + 1, _storage.WriteCount);
        Assert.Equal(samples, _probe.Samples);
        Assert.Equal(41, _loop.IdleWakes);

        _presence.IdleFor = TimeSpan.Zero;
        Run(1);                                         // back: the gap is discarded, not capped onto Acme
        Assert.Equal(samples + 1, _probe.Samples);
        Assert.Equal(TimeSpan.FromSeconds(10), TimeInAcme(root));
        Assert.Equal(FolderActivityTracker.ForegroundPollInterval, _loop.NextWait);
    }

    [Fact]
    public void Idle_IsJudgedAgainstTheLongestTimeout()
    {
        _loop.Start();
        StartTracking();
        AddRoot(_store, @"D:\Docs");
        _store.TryUpdateRoot(_store.Roots[1].Config with { IdleTimeout = TimeSpan.FromMinutes(30) }, out _);
        _loop.Wake(TrackingSignal.RootsChanged);
        var samples = _probe.Samples;

        _presence.IdleFor = TimeSpan.FromMinutes(10);   // past C:\Jobs' 5 minutes, inside D:\Docs' 30
        Run(3);

        Assert.Equal(samples + 3, _probe.Samples);
    }

    [Fact]
    public void AProbeFailure_IsSurvived_AndCountsNothingForItsGap()
    {
        _loop.Start();
        var root = StartTracking();
        Run(10);

        _probe.ThrowOnSample = new COMException("The RPC server is unavailable.", unchecked((int)0x800706BA));
        Run(2);
        Assert.Equal(2, _loop.ProbeFailures);

        _probe.ThrowOnSample = null;
        Run(1);                                         // re-establishes the baseline
        Assert.Equal(TimeSpan.FromSeconds(10), TimeInAcme(root));
        Run(1);
        Assert.Equal(TimeSpan.FromSeconds(11.5), TimeInAcme(root));
    }

    [Fact]
    public void Stopping_FlushesWhatIsLeft()
    {
        _loop.Start();
        StartTracking();
        Run(10);
        var writes = _storage.WriteCount;

        _loop.Stop();

        Assert.Equal(writes + 1, _storage.WriteCount);
        Assert.False(_store.HasUnsavedChanges);
    }

    [Fact]
    public void Wakes_AndTicks_AreCounted()
    {
        _loop.Start();
        StartTracking();
        _presence.SessionLocked = true;
        _loop.Wake(TrackingSignal.Locked);

        Assert.Equal(2, _loop.Wakes);
        Assert.Equal(1, _loop.Ticks);
    }
}
