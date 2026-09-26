using System;
using System.Linq;
using QuickerPlaces.Models.Activity;
using QuickerPlaces.Services.Activity;
using QuickerPlaces.Tests.Fakes;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>
/// The tracker's accounting (Phase 9 plan §7, first list): the dwell
/// threshold, foreground-only and idle gating, lock, suspend and the poll
/// cap, rollups, dropped paths and ambiguous tabs, all against scripted
/// snapshots and a fake clock. Day boundaries are in
/// FolderActivityTrackerDayTests.
///
/// Unless a test says otherwise the root is C:\Jobs with the defaults: a
/// RootChild rollup, a 5 s dwell and a 5 min idle timeout. Steps are 1.5 s,
/// so a folder in the foreground from the first tick (t = 0) crosses its
/// dwell at 5 s and is first credited at the t = 6 tick, with 1 s.
/// </summary>
public sealed class FolderActivityTrackerTests
{
    private const string Acme = @"C:\Jobs\Acme";
    private const string Beta = @"C:\Jobs\Beta";
    private const string Elsewhere = @"D:\Elsewhere";

    private static TrackedRootConfig Jobs => new("jobs", @"C:\Jobs");

    private static TimeSpan Seconds(double s) => TimeSpan.FromSeconds(s);

    /// <summary>Acme in the foreground from t = 0 to t = 9: its visit counted, 4 s credited.</summary>
    private static TrackerHarness WithACountedVisitToAcme(TrackedRootConfig? root = null)
    {
        var h = new TrackerHarness(root ?? Jobs);
        h.Probe.ShowForeground(Acme);
        h.Tick();
        h.Steps(6);
        Assert.Equal(Seconds(4), h.TimeIn(Acme));
        return h;
    }

    // ---- D9: the dwell threshold ----

    [Fact]
    public void AVisitShorterThanTheDwellThreshold_RecordsNothing()
    {
        var h = new TrackerHarness(Jobs);
        h.Probe.ShowForeground(Acme);
        h.Tick();
        h.Steps(2);                            // seen at 0, 1.5 and 3
        h.Probe.ShowForeground(Elsewhere);
        h.Steps(4);

        Assert.Empty(h.Recorded);
    }

    [Fact]
    public void AVisitThatCrossesTheDwellThreshold_RecordsFromTheCrossingPoint()
    {
        var h = new TrackerHarness(Jobs);
        h.Probe.ShowForeground(Acme);
        h.Tick();
        h.Steps(3);                            // t = 4.5, under the threshold
        Assert.Empty(h.Recorded);

        var crossing = h.Step();               // t = 6: 1 s past the 5 s crossing

        var interval = Assert.Single(crossing);
        Assert.Equal(new ActivityInterval("jobs", Acme, new DateOnly(2026, 9, 25), Seconds(1), true, h.Time.UtcNow), interval);

        h.Steps(2);                            // t = 9
        Assert.Equal(Seconds(4), h.TimeIn(Acme));
        Assert.Equal(1, h.VisitsTo(Acme));
    }

    [Fact]
    public void AVisitThatLeavesBeforeBeingSeenPastTheThreshold_RecordsNothing()
    {
        // Seen at 0 to 4.5, gone at 6: it may have left at 4.6, so it is not
        // credited as having stayed (D9 prefers under-counting).
        var h = new TrackerHarness(Jobs);
        h.Probe.ShowForeground(Acme);
        h.Tick();
        h.Steps(3);
        h.Probe.ShowForeground(Elsewhere);
        h.Steps(4);

        Assert.Empty(h.Recorded);
    }

    [Fact]
    public void TheDwellThreshold_IsTheRootsOwn()
    {
        var h = new TrackerHarness(Jobs with { DwellThreshold = TimeSpan.Zero });
        h.Probe.ShowForeground(Acme);
        h.Tick();
        h.Steps(2);

        Assert.Equal(Seconds(3), h.TimeIn(Acme));
        Assert.Equal(1, h.VisitsTo(Acme));
    }

    [Fact]
    public void NavigatingWithinTheCreditedFolder_IsOneVisit()
    {
        var h = new TrackerHarness(Jobs);
        h.Probe.ShowForeground(Acme + @"\Drawings");
        h.Tick();
        h.Steps(2);
        h.Probe.ShowForeground(Acme + @"\Specs");
        h.Steps(4);                            // t = 9

        Assert.Equal(Seconds(4), h.TimeIn(Acme));
        Assert.Equal(1, h.VisitsTo(Acme));
    }

    [Fact]
    public void MovingToAnotherFolder_StartsANewVisitWithItsOwnDwell()
    {
        var h = WithACountedVisitToAcme();     // t = 9
        h.Probe.ShowForeground(Beta);
        h.Step();                              // t = 10.5: Beta arrives, Acme keeps 9 to 10.5 (D6)
        h.Steps(3);                            // t = 15, under Beta's threshold
        Assert.Equal(TimeSpan.Zero, h.TimeIn(Beta));

        h.Step();                              // t = 16.5: 1 s past Beta's 15.5 crossing

        Assert.Equal(Seconds(5.5), h.TimeIn(Acme));
        Assert.Equal(Seconds(1), h.TimeIn(Beta));
        Assert.Equal(1, h.VisitsTo(Beta));
    }

    [Fact]
    public void ReturningToAFolderAfterLeavingIt_IsASecondVisit()
    {
        var h = WithACountedVisitToAcme();     // t = 9
        h.Probe.ShowForeground(Elsewhere);
        h.Step();                              // t = 10.5
        h.Probe.ShowForeground(Acme);
        h.Steps(5);                            // back at 12, crossing at 17, t = 18

        Assert.Equal(2, h.VisitsTo(Acme));
        Assert.Equal(Seconds(4 + 1.5 + 1), h.TimeIn(Acme));
    }

    [Fact]
    public void ReturningFromAnotherAppToSameFolder_ContinuesVisitWithoutCreditingTimeAway()
    {
        var h = WithACountedVisitToAcme();     // t = 9, 4 s
        h.Probe.Windows[0] = h.Probe.Windows[0] with { IsForeground = false };
        h.Step();                              // t = 10.5, last foreground interval
        h.Step(TimeSpan.FromMinutes(2));       // another app stays foreground
        var beforeReturn = h.TimeIn(Acme);

        h.Probe.Windows[0] = h.Probe.Windows[0] with { IsForeground = true };
        h.Step();                              // re-establish foreground baseline
        Assert.Equal(beforeReturn, h.TimeIn(Acme));
        h.Step();

        Assert.Equal(1, h.VisitsTo(Acme));
        Assert.Equal(beforeReturn + Seconds(1.5), h.TimeIn(Acme));
    }

    [Fact]
    public void ClosingExplorer_EndsTheVisitEvenIfTheSameFolderIsReopened()
    {
        var h = WithACountedVisitToAcme();
        h.Probe.CloseAll();
        h.Step();
        h.Probe.ShowForeground(Acme);
        h.Steps(5);

        Assert.Equal(2, h.VisitsTo(Acme));
    }

    // ---- D7: foreground only, and only while present ----

    [Fact]
    public void ABackgroundWindow_AccruesNothingWhileAnotherIsForeground()
    {
        var h = new TrackerHarness(Jobs);
        h.Probe.Windows.Add(new ShellWindowSnapshot(Acme, 2, false));
        h.Probe.Windows.Add(new ShellWindowSnapshot(Beta, 1, true));
        h.Tick();
        h.Steps(20);

        Assert.Equal(TimeSpan.Zero, h.TimeIn(Acme));
        Assert.Equal(Seconds(25), h.TimeIn(Beta));   // from its 5 s crossing to t = 30
    }

    [Fact]
    public void BackgroundWindowsAlone_AccrueNothing()
    {
        var h = new TrackerHarness(Jobs);
        h.Probe.Windows.Add(new ShellWindowSnapshot(Acme, 1, false));
        h.Probe.Windows.Add(new ShellWindowSnapshot(Beta, 2, false));
        h.Tick();
        h.Steps(20);

        Assert.Empty(h.Recorded);
    }

    [Fact]
    public void IdlePastTheTimeout_SuspendsAccrual_AndItResumesOnTheNextInputWithoutANewVisit()
    {
        var h = WithACountedVisitToAcme();     // t = 9, 4 s
        h.Presence.IdleFor = TimeSpan.FromMinutes(5);
        h.Steps(4);
        Assert.Equal(Seconds(4), h.TimeIn(Acme));

        h.Presence.IdleFor = TimeSpan.Zero;
        h.Step();

        Assert.Equal(Seconds(5.5), h.TimeIn(Acme));
        Assert.Equal(1, h.VisitsTo(Acme));
    }

    [Fact]
    public void IdleUnderTheTimeout_StillAccrues()
    {
        var h = WithACountedVisitToAcme();
        h.Presence.IdleFor = TimeSpan.FromMinutes(4);
        h.Steps(2);

        Assert.Equal(Seconds(7), h.TimeIn(Acme));
    }

    [Fact]
    public void TheIdleTimeout_IsTheRootsOwn()
    {
        var h = WithACountedVisitToAcme(Jobs with { IdleTimeout = TimeSpan.FromMinutes(1) });
        h.Presence.IdleFor = TimeSpan.FromMinutes(2);
        h.Steps(2);

        Assert.Equal(Seconds(4), h.TimeIn(Acme));
    }

    [Fact]
    public void IdleDuringTheDwell_RestartsIt()
    {
        var h = new TrackerHarness(Jobs);
        h.Probe.ShowForeground(Acme);
        h.Tick();
        h.Presence.IdleFor = TimeSpan.FromMinutes(10);
        h.Steps(6);                            // t = 9, away the whole time
        h.Presence.IdleFor = TimeSpan.Zero;
        h.Step();                              // t = 10.5: back, the dwell starts again
        h.Steps(3);                            // t = 15, under the new 15.5 crossing
        Assert.Empty(h.Recorded);

        h.Step();                              // t = 16.5

        Assert.Equal(Seconds(1), h.TimeIn(Acme));
    }

    // ---- D6: lock, suspend, and the cap ----

    [Fact]
    public void ALockedSession_DiscardsTheGap_AndIsNotSampled()
    {
        var h = WithACountedVisitToAcme();     // t = 9, 4 s
        var samples = h.Probe.Samples;
        h.Presence.SessionLocked = true;
        h.Step(TimeSpan.FromHours(1));
        h.Step(TimeSpan.FromHours(1));
        Assert.Equal(samples, h.Probe.Samples);

        h.Presence.SessionLocked = false;
        h.Step();                              // re-establishes the baseline
        Assert.Equal(Seconds(4), h.TimeIn(Acme));

        h.Step();
        Assert.Equal(Seconds(5.5), h.TimeIn(Acme));
        Assert.Equal(1, h.VisitsTo(Acme));
    }

    [Fact]
    public void ASuspend_DiscardsTheGap()
    {
        var h = WithACountedVisitToAcme();     // t = 9, 4 s
        h.Tracker.DiscardGap();                // PowerModeChanged: suspend, then resume
        h.Step(TimeSpan.FromHours(60));        // the long weekend
        Assert.Equal(Seconds(4), h.TimeIn(Acme));

        h.Step();
        Assert.Equal(Seconds(5.5), h.TimeIn(Acme));
    }

    [Fact]
    public void AGapInsideTheDwell_DoesNotCompleteIt()
    {
        var h = new TrackerHarness(Jobs);
        h.Probe.ShowForeground(Acme);
        h.Tick();
        h.Step();
        h.Tracker.DiscardGap();
        h.Step(TimeSpan.FromMinutes(10));      // the dwell starts again here
        h.Steps(3);
        Assert.Empty(h.Recorded);

        h.Step();

        Assert.Equal(Seconds(1), h.TimeIn(Acme));
    }

    [Fact]
    public void ALongIntervalWithNoEvent_IsCappedAtTwiceThePollInterval()
    {
        var h = WithACountedVisitToAcme();
        Assert.Equal(Seconds(1.5), h.Tracker.PollInterval);

        var intervals = h.Step(TimeSpan.FromHours(1));

        Assert.Equal(Seconds(3), Assert.Single(intervals).Duration);
    }

    [Fact]
    public void AWallClockJump_DoesNotAddTime()
    {
        var h = WithACountedVisitToAcme();
        h.Time.Advance(TimeSpan.FromHours(3)); // the user, or a time sync, moves the clock

        var intervals = h.Step();

        Assert.Equal(Seconds(1.5), Assert.Single(intervals).Duration);
    }

    // ---- D2: the adaptive poll interval ----

    [Fact]
    public void ThePollInterval_IsFastWhileAnExplorerWindowIsForeground()
    {
        var h = new TrackerHarness(Jobs);
        h.Probe.ShowForeground(Elsewhere);
        h.Tick();

        Assert.Equal(FolderActivityTracker.ForegroundPollInterval, h.Tracker.PollInterval);
        Assert.Equal(Seconds(1.5), FolderActivityTracker.ForegroundPollInterval);
    }

    [Fact]
    public void ThePollInterval_IsSlowWhenNoExplorerWindowIsForeground()
    {
        var h = new TrackerHarness(Jobs);
        h.Probe.Windows.Add(new ShellWindowSnapshot(Acme, 1, false));
        h.Tick();

        Assert.Equal(FolderActivityTracker.BackgroundPollInterval, h.Tracker.PollInterval);
        Assert.Equal(Seconds(15), FolderActivityTracker.BackgroundPollInterval);
    }

    // ---- D8, D13, D22: what is credited ----

    [Fact]
    public void ADeepFolder_IsCreditedPerTheRootsRollup()
    {
        var h = new TrackerHarness(Jobs);
        h.Probe.ShowForeground(@"c:\jobs\Acme\Drawings\Rev3");
        h.Tick();
        h.Steps(6);

        Assert.Equal(Seconds(4), h.TimeIn(Acme));
        Assert.All(h.Recorded, i => Assert.Equal("jobs", i.RootId));
    }

    [Fact]
    public void AnExactRoot_CreditsTheFolderItself()
    {
        var h = new TrackerHarness(Jobs with { Rollup = RollupMode.Exact });
        h.Probe.ShowForeground(Acme + @"\Drawings");
        h.Tick();
        h.Steps(6);

        Assert.Equal(Seconds(4), h.TimeIn(Acme + @"\Drawings"));
    }

    [Fact]
    public void TheRootItself_IsCredited()
    {
        var h = new TrackerHarness(Jobs);
        h.Probe.ShowForeground(@"C:\Jobs\");
        h.Tick();
        h.Steps(6);

        Assert.Equal(Seconds(4), h.TimeIn(@"C:\Jobs"));
    }

    [Theory]
    [InlineData("::{20D04FE0-3AEA-1069-A2D8-08002B30309D}")]
    [InlineData("ftp://example.com/Jobs/Acme")]
    [InlineData(@"Jobs\Acme")]
    [InlineData(@"C:\Other\Acme")]
    public void NonFilesystemRelativeAndOutsideRootPaths_AreDropped(string path)
    {
        var h = new TrackerHarness(Jobs);
        h.Probe.ShowForeground(path);
        h.Tick();
        h.Steps(10);

        Assert.Empty(h.Recorded);
    }

    [Fact]
    public void AFolderReachedByBothRoutes_IsOneFolderAndOneVisit()
    {
        var root = new TrackedRootConfig("jobs", @"J:\Jobs") { EquivalentPrefixes = new[] { @"\\fileserver\projects\Jobs" } };
        var h = new TrackerHarness(root);
        h.Probe.ShowForeground(@"J:\Jobs\Acme");
        h.Tick();
        for (var i = 0; i < 6; i++)
        {
            h.Probe.ShowForeground(i % 2 == 0 ? @"\\fileserver\projects\Jobs\Acme\Drawings" : @"J:\Jobs\Acme");
            h.Step();
        }

        Assert.Equal(Seconds(4), h.TimeIn(@"J:\Jobs\Acme"));
        Assert.Equal(1, h.VisitsTo(@"J:\Jobs\Acme"));
        Assert.All(h.Recorded, i => Assert.Equal(@"J:\Jobs\Acme", i.Folder));
    }

    [Fact]
    public void NestedRoots_AreEachCredited()
    {
        var h = new TrackerHarness(Jobs, new TrackedRootConfig("acme", Acme) { Rollup = RollupMode.Exact });
        h.Probe.ShowForeground(Acme + @"\Drawings");
        h.Tick();
        h.Steps(6);

        Assert.Equal(Seconds(4), h.TimeIn(Acme));
        Assert.Equal(Seconds(4), h.TimeIn(Acme + @"\Drawings"));
        Assert.Equal(new[] { "acme", "jobs" }, h.Recorded.Select(i => i.RootId).Distinct().OrderBy(id => id));
    }

    [Fact]
    public void ARemovedRoot_StopsBeingCredited()
    {
        var h = WithACountedVisitToAcme();
        h.Tracker.SetRoots(Array.Empty<TrackedRootConfig>());
        h.Steps(4);

        Assert.Equal(Seconds(4), h.TimeIn(Acme));
    }

    [Fact]
    public void SettingTheSameRootsAgain_KeepsTheVisitGoing()
    {
        var h = WithACountedVisitToAcme();
        h.Tracker.SetRoots(new[] { Jobs });
        h.Steps(2);

        Assert.Equal(Seconds(7), h.TimeIn(Acme));
        Assert.Equal(1, h.VisitsTo(Acme));
    }

    [Fact]
    public void ChangingDepth_StartsANewVisitWithoutCreditingTheOldGroupAgain()
    {
        var h = new TrackerHarness(Jobs);
        h.Probe.ShowForeground(Acme + @"\Drawings");
        h.Tick();
        h.Steps(6);
        Assert.Equal(Seconds(4), h.TimeIn(Acme));

        h.Tracker.SetRoots(new[] { Jobs with { Rollup = RollupMode.Depth, Depth = 2 } });
        h.Tick();
        h.Steps(6);

        Assert.Equal(Seconds(4), h.TimeIn(Acme));
        Assert.Equal(Seconds(4), h.TimeIn(Acme + @"\Drawings"));
        Assert.Equal(1, h.VisitsTo(Acme + @"\Drawings"));
    }

    // ---- D14: ambiguous tabs ----

    [Fact]
    public void TabsThatDisagree_OnTheForegroundHandle_AreSkipped()
    {
        var h = new TrackerHarness(Jobs);
        h.Probe.Windows.Add(new ShellWindowSnapshot(Acme, 1, true));
        h.Probe.Windows.Add(new ShellWindowSnapshot(Beta, 1, true));
        h.Tick();
        h.Steps(10);

        Assert.Empty(h.Recorded);
    }

    [Fact]
    public void ATabOutsideTheRoot_DisagreesWithOneInside()
    {
        var h = new TrackerHarness(Jobs);
        h.Probe.Windows.Add(new ShellWindowSnapshot(Acme, 1, true));
        h.Probe.Windows.Add(new ShellWindowSnapshot("::{20D04FE0-3AEA-1069-A2D8-08002B30309D}", 1, true));
        h.Tick();
        h.Steps(10);

        Assert.Empty(h.Recorded);
    }

    [Fact]
    public void TabsThatAgree_AreRecordedOnce()
    {
        var h = new TrackerHarness(Jobs);
        h.Probe.Windows.Add(new ShellWindowSnapshot(Acme + @"\Drawings", 1, true));
        h.Probe.Windows.Add(new ShellWindowSnapshot(@"c:\jobs\acme\Specs", 1, true));
        h.Tick();
        h.Steps(6);

        Assert.Equal(Seconds(4), h.TimeIn(Acme));
        Assert.Equal(1, h.VisitsTo(Acme));
    }

    [Fact]
    public void TabsThatAgreeUnderOneRollup_CanDisagreeUnderAnother()
    {
        var h = new TrackerHarness(Jobs with { Rollup = RollupMode.Exact });
        h.Probe.Windows.Add(new ShellWindowSnapshot(Acme + @"\Drawings", 1, true));
        h.Probe.Windows.Add(new ShellWindowSnapshot(Acme + @"\Specs", 1, true));
        h.Tick();
        h.Steps(10);

        Assert.Empty(h.Recorded);
    }

    [Fact]
    public void ACountedVisit_KeepsTheIntervalBeforeTabsBecameAmbiguous()
    {
        var h = WithACountedVisitToAcme();     // t = 9
        h.Probe.Windows.Add(new ShellWindowSnapshot(Beta, 1, true));
        h.Steps(4);

        // 9 to 10.5 is Acme's (D6: time goes to what was foreground before); nothing after.
        Assert.Equal(Seconds(5.5), h.TimeIn(Acme));
        Assert.Equal(TimeSpan.Zero, h.TimeIn(Beta));
    }
}
