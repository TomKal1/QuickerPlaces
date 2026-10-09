using System;
using System.Collections.Generic;
using System.Linq;
using QuickerPlaces.Models;
using QuickerPlaces.Services.Revit;
using QuickerPlaces.Services.Revit.Handlers;
using QuickerPlaces.Services.Revit.Opening;
using QuickerPlaces.Tests.Fakes;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>Every branch of the open planner: what is refused, opened directly, or asked of a handler.</summary>
public sealed class RevitOpenPlannerTests
{
    private const string Central = @"\\server\projects\Tower_Central.rvt";
    private static readonly DateTime Now = new(2026, 10, 9, 9, 0, 0, DateTimeKind.Utc);

    private readonly List<RevitInstall> _installs = [new(2025, @"C:\Rvt\2025\Revit.exe"), new(2024, @"C:\Rvt\2024\Revit.exe")];
    private readonly List<RunningRevit> _running = [];
    private readonly List<RegisteredHandler> _handlers2025 = [Handler("contoso", loadedIn: null)];
    private readonly AppSettings _settings = new() { RevitReleases = new() { ["2025"] = new() { HandlerId = "contoso" } } };
    private readonly FakeNetworkDrives _network = new();

    private static RegisteredHandler Handler(string id, int? loadedIn, bool supportsAction = true, bool ready = true) =>
        new(new HandlerRegistration
        {
            Protocol = 1, HandlerId = id, DisplayName = id + " name", RevitRelease = "2025",
            Actions = supportsAction ? [HandlerProtocol.ActionOpenNewLocal] : ["other"], WrittenUtc = Now,
        },
        loadedIn is { } pid ? [new LiveHandlerInstance(pid, Now, ready ? Now : null)] : [], null);

    private static RevitFileInfo Info(int? release = 2025, RevitWorksharing sharing = RevitWorksharing.Central,
        string? central = Central, RevitFileProblem problem = RevitFileProblem.None) =>
        new(release, "build", sharing, central, null, 14, problem);

    private RevitOpenPlan Plan(RevitFileInfo info, string path = Central, RevitOpenOverrides? overrides = null, bool network = true)
    {
        IReadOnlyList<ReleaseHandlers> handlers = [new ReleaseHandlers("2025", _handlers2025)];
        return RevitOpenPlanner.Plan(path, info, _installs, _running, handlers, _settings, Now, network ? _network : null, overrides);
    }

    private static RunningRevit Revit(int pid, TimeSpan age, int release = 2025) => new(pid, Now - age, release, null);

    // --- Refusals ----------------------------------------------------------------

    [Theory]
    [InlineData(RevitFileProblem.NotRevitFile, "doesn't look like a Revit file")]
    [InlineData(RevitFileProblem.NotFound, "isn't there")]
    [InlineData(RevitFileProblem.ReleaseNotRecorded, "doesn't say which Revit release")]
    [InlineData(RevitFileProblem.InUse, "couldn't be read")]
    [InlineData(RevitFileProblem.UnrecognisedLayout, "couldn't be read")]
    [InlineData(RevitFileProblem.TimedOut, "couldn't be read")]
    public void UnreadableOrNotRevit_IsRefused_WithAReason(RevitFileProblem problem, string expected)
    {
        var plan = Plan(RevitFileInfo.Failed(problem));

        Assert.Equal(RevitOpenKind.Refuse, plan.Kind);
        Assert.Contains(expected, plan.RefusalReason);
    }

    [Fact]
    public void TooOld_IsRefused_NamingTheRelease()
    {
        var plan = Plan(Info(2021, problem: RevitFileProblem.TooOld));

        Assert.Equal(RevitOpenKind.Refuse, plan.Kind);
        Assert.Contains("Revit 2021", plan.RefusalReason);
        Assert.Contains("2022 and later", plan.RefusalReason);
    }

    [Fact]
    public void TooOld_WithNoReleaseRecorded_IsRefused()
    {
        var plan = Plan(Info(null, problem: RevitFileProblem.TooOld));

        Assert.Contains("before Revit 2019", plan.RefusalReason);
    }

    [Fact]
    public void NoReleaseRecorded_IsRefused_NeverTheNewestRevit()
    {
        var plan = Plan(Info(release: null));

        Assert.Equal(RevitOpenKind.Refuse, plan.Kind);
        Assert.Null(plan.ExePath);
    }

    [Fact]
    public void WorksharingUnknown_IsRefused()
    {
        var plan = Plan(Info(sharing: RevitWorksharing.Unknown));

        Assert.Equal(RevitOpenKind.Refuse, plan.Kind);
        Assert.Contains("central or a local", plan.RefusalReason);
    }

    [Fact]
    public void ReleaseNotInstalled_IsRefused_AndNeverOpenedInAnotherRelease()
    {
        _installs.RemoveAll(i => i.Release == 2025); // 2024 remains
        var plan = Plan(Info(sharing: RevitWorksharing.Local));

        Assert.Equal(RevitOpenKind.Refuse, plan.Kind);
        Assert.Equal("Revit 2025 is not installed. Tower_Central.rvt was saved in Revit 2025, and QuickerPlaces doesn't open it in another release.", plan.RefusalReason);
        Assert.Null(plan.ExePath);
    }

    // --- Direct opens ------------------------------------------------------------

    [Theory]
    [InlineData(RevitWorksharing.Local)]
    [InlineData(RevitWorksharing.NotWorkshared)]
    public void LocalOrUnshared_OpensDirectly_InThatReleasesRevit(RevitWorksharing sharing)
    {
        _settings.RevitReleases = null; // no handler needed
        var plan = Plan(Info(sharing: sharing), @"C:\REVIT_LOCAL2025\Tower_me.rvt");

        Assert.Equal(RevitOpenKind.DirectOpen, plan.Kind);
        Assert.Equal(@"C:\Rvt\2025\Revit.exe", plan.ExePath);
        Assert.Equal("2025", plan.Release);
        Assert.False(plan.NeedsLaunch);
    }

    [Fact]
    public void AFamily_NotWorkshared_OpensDirectly_InItsOwnRelease()
    {
        var plan = Plan(Info(2024, RevitWorksharing.NotWorkshared, central: null), @"C:\Lib\Door.rfa");

        Assert.Equal(RevitOpenKind.DirectOpen, plan.Kind);
        Assert.Equal(@"C:\Rvt\2024\Revit.exe", plan.ExePath);
    }

    // --- Centrals ----------------------------------------------------------------

    [Fact]
    public void ACentral_WithAChosenRegisteredHandler_IsARequest_WithDefaults()
    {
        var plan = Plan(Info());

        Assert.Equal(RevitOpenKind.HandlerRequest, plan.Kind);
        Assert.Equal("contoso", plan.HandlerId);
        Assert.Equal("contoso name", plan.HandlerName);
        Assert.Equal(@"C:\REVIT_LOCAL2025", plan.LocalFolder);
        Assert.Equal("lastViewed", plan.Worksets);
        Assert.Equal(@"C:\Rvt\2025\Revit.exe", plan.ExePath);
    }

    [Fact]
    public void TheSettingsFolder_AndTheOverrides_Win()
    {
        _settings.RevitReleases!["2025"].LocalFolder = @"D:\Locals";
        Assert.Equal(@"D:\Locals", Plan(Info()).LocalFolder);

        _handlers2025.Add(Handler("other", null));
        var plan = Plan(Info(), overrides: new RevitOpenOverrides("other", @"E:\X", "none"));

        Assert.Equal("other", plan.HandlerId);
        Assert.Equal(@"E:\X", plan.LocalFolder);
        Assert.Equal("none", plan.Worksets);
    }

    [Fact]
    public void AnUnknownWorksets_IsRefused()
    {
        var plan = Plan(Info(), overrides: new RevitOpenOverrides(Worksets: "most"));

        Assert.Equal(RevitOpenKind.Refuse, plan.Kind);
    }

    [Fact]
    public void ACentral_WithNoHandlerChosen_IsRefused_SayingWhatToDo()
    {
        _settings.RevitReleases = null;
        var plan = Plan(Info());

        Assert.Equal(RevitOpenKind.Refuse, plan.Kind);
        Assert.Contains("none is chosen for Revit 2025", plan.RefusalReason);
        Assert.Contains("Settings", plan.RefusalReason);
    }

    [Fact]
    public void ACentral_WhoseChosenHandlerIsNotRegistered_IsRefused()
    {
        _handlers2025.Clear();
        var plan = Plan(Info());

        Assert.Equal(RevitOpenKind.Refuse, plan.Kind);
        Assert.Contains("\"contoso\" has not registered for Revit 2025", plan.RefusalReason);
    }

    [Fact]
    public void ACentral_WhoseChosenHandlerLacksTheAction_IsRefused()
    {
        _handlers2025.Clear();
        _handlers2025.Add(Handler("contoso", null, supportsAction: false));
        var plan = Plan(Info());

        Assert.Equal(RevitOpenKind.Refuse, plan.Kind);
        Assert.Contains("doesn't support opening a central", plan.RefusalReason);
    }

    // --- Copies of centrals ------------------------------------------------------

    [Fact]
    public void ACopyOfACentral_IsRefused_NamingTheRecordedCentral()
    {
        var plan = Plan(Info(), @"\\server\archive\Copy of Tower_Central.rvt");

        Assert.Equal(RevitOpenKind.Refuse, plan.Kind);
        Assert.Contains($"records its central as {Central}", plan.RefusalReason);
        Assert.Contains("copy of a central", plan.RefusalReason);
    }

    [Fact]
    public void AMappedDrive_IsResolvedToUnc_BeforeComparing_IgnoringCase()
    {
        _network.Map["W:"] = @"\\SERVER\Projects";

        var plan = Plan(Info(), @"w:\Tower_Central.rvt");

        Assert.Equal(RevitOpenKind.HandlerRequest, plan.Kind);
    }

    [Fact]
    public void AMappedDrive_ResolvingElsewhere_IsACopy()
    {
        _network.Map["W:"] = @"\\server\other";

        Assert.Equal(RevitOpenKind.Refuse, Plan(Info(), @"W:\Tower_Central.rvt").Kind);
    }

    [Fact]
    public void ALocalDiskCopy_OfAUncCentral_IsRefused()
    {
        Assert.Equal(RevitOpenKind.Refuse, Plan(Info(), @"C:\Users\me\Desktop\Tower_Central.rvt").Kind);
    }

    [Fact]
    public void ALocalDiskCopy_OfALocalDiskCentral_InAnotherFolder_IsRefused_AndInTheSameOneIsNot()
    {
        var info = Info(central: @"C:\Projects\Tower.rvt");

        Assert.Equal(RevitOpenKind.Refuse, Plan(info, @"C:\Users\me\Tower.rvt").Kind);
        Assert.Equal(RevitOpenKind.HandlerRequest, Plan(info, @"c:/projects/TOWER.rvt").Kind);
    }

    [Fact]
    public void WhenEitherPathCantBeResolved_NothingIsSaid()
    {
        // No resolver at all.
        Assert.Equal(RevitOpenKind.HandlerRequest, Plan(Info(), @"C:\Users\me\Tower_Central.rvt", network: false).Kind);
        // A resolver that throws.
        _network.Throws = true;
        Assert.Equal(RevitOpenKind.HandlerRequest, Plan(Info(), @"W:\Tower_Central.rvt").Kind);
        _network.Throws = false;
        // A recorded Revit Server path can't be compared; neither can a relative one.
        Assert.Equal(RevitOpenKind.HandlerRequest, Plan(Info(central: "RSN://server/Tower"), @"C:\a\Tower.rvt").Kind);
        Assert.Equal(RevitOpenKind.HandlerRequest, Plan(Info(central: @"sub\Tower.rvt"), @"C:\a\Tower.rvt").Kind);
        // No recorded central.
        Assert.Equal(RevitOpenKind.HandlerRequest, Plan(Info(central: null), @"X:\a.rvt").Kind);
    }

    [Fact]
    public void ACentral_AtItsRecordedPath_IsNotACopy_DespiteSlashesAndCase()
    {
        var plan = Plan(Info(central: Central.ToUpperInvariant()), Central.Replace('\\', '/').Replace("//", @"\\"));

        Assert.Equal(RevitOpenKind.HandlerRequest, plan.Kind);
    }

    // --- Is the handler loaded? --------------------------------------------------

    [Fact]
    public void NoRevitOfTheRelease_RunningMeansALaunchIsNeeded()
    {
        _running.Add(Revit(1, TimeSpan.FromHours(1), release: 2024)); // another release doesn't count
        var plan = Plan(Info());

        Assert.Equal(HandlerAvailability.NotRunning, plan.Availability);
        Assert.True(plan.NeedsLaunch);
        Assert.False(plan.HandlerNotLoaded);
    }

    [Fact]
    public void ALoadedInstance_NeedsNoLaunch()
    {
        _handlers2025[0] = Handler("contoso", loadedIn: 55);
        _running.Add(Revit(55, TimeSpan.FromHours(1)));
        var plan = Plan(Info());

        Assert.Equal(HandlerAvailability.Loaded, plan.Availability);
        Assert.False(plan.NeedsLaunch);
        Assert.False(plan.HandlerNotLoaded);
        Assert.Equal([55], plan.RunningProcessIds);
    }

    [Fact]
    public void ARevitRunningForAWhile_WithoutTheHandler_MeansNotLoaded()
    {
        _running.Add(Revit(1, TimeSpan.FromMinutes(2)));
        var plan = Plan(Info());

        Assert.Equal(HandlerAvailability.NotLoaded, plan.Availability);
        Assert.True(plan.HandlerNotLoaded);
        Assert.False(plan.NeedsLaunch);
    }

    [Fact]
    public void ARevitThatStartedJustNow_IsStillStarting_NotNotLoaded()
    {
        _running.Add(Revit(1, TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(59)));
        var plan = Plan(Info());

        Assert.Equal(HandlerAvailability.Starting, plan.Availability);
        Assert.False(plan.HandlerNotLoaded);
        Assert.False(plan.NeedsLaunch);
    }

    [Fact]
    public void AStartingRevit_AmongOldOnes_KeepsTheHandlerFromBeingNotLoaded()
    {
        _running.Add(Revit(1, TimeSpan.FromHours(3)));
        _running.Add(Revit(2, TimeSpan.FromSeconds(20)));

        Assert.Equal(HandlerAvailability.Starting, Plan(Info()).Availability);
    }
}
