using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using QuickerPlaces.Services.Revit.Handlers;
using QuickerPlaces.Services.Revit.Opening;
using QuickerPlaces.Tests.Fakes;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>
/// The open runner against a temporary protocol folder, a fake clock and a
/// fake launcher. The handler's part is played by hand from the pause
/// between polls: each pause advances the clock 500 ms and runs a script
/// keyed by the poll number. No test sleeps.
/// </summary>
public sealed class RevitOpenerTests : IDisposable
{
    private const string Release = "2025";
    private const string Handler = "contoso";
    private const string Central = @"\\server\projects\Tower_Central.rvt";
    private const string Exe = @"C:\Rvt\2025\Revit.exe";

    private static readonly DateTimeOffset Start = new(2026, 10, 9, 9, 0, 0, TimeSpan.Zero);

    private readonly TempDirectory _dir = new();
    private readonly RevitProtocolFolder _folder;
    private readonly ManualTimeProvider _time = new(Start);
    private readonly FakeProcessProbe _probe = new();
    private readonly FakeLauncher _launcher = new();
    private readonly FakeProcessLister _lister = new();
    private readonly FakeInstallSource _installs = new();
    private readonly RevitOpener _opener;
    private readonly RevitHandlerRegistry _registry;
    private readonly RevitRequestQueue _queue;
    private readonly Dictionary<int, Action> _script = [];
    private readonly ListProgress _progress = new();
    private int _polls;

    public RevitOpenerTests()
    {
        _folder = new RevitProtocolFolder(Path.Combine(_dir.Path, "revit"));
        _registry = new RevitHandlerRegistry(_folder, _probe, _time);
        _queue = new RevitRequestQueue(_folder, _time, null, (interval, token) =>
        {
            token.ThrowIfCancellationRequested();
            _polls++;
            _time.Advance(interval);
            if (_script.TryGetValue(_polls, out var action))
                action();
            return Task.CompletedTask;
        });
        _opener = new RevitOpener(_folder, _queue, _registry, new RevitMachine
        {
            InstallSource = _installs, Processes = _lister, Launcher = _launcher, Probe = _probe,
        }, _time);
        Register();
    }

    public void Dispose() => _dir.Dispose();

    private static RevitOpenPlan HandlerPlan() => new()
    {
        Kind = RevitOpenKind.HandlerRequest, FilePath = Central, Release = Release, ExePath = Exe, HandlerId = Handler,
        HandlerName = "Contoso", LocalFolder = @"C:\REVIT_LOCAL2025", Worksets = "lastViewed",
    };

    private DateTime Now => _time.GetUtcNow().UtcDateTime;

    private void Register()
    {
        var registration = new HandlerRegistration
        {
            Protocol = 1, HandlerId = Handler, DisplayName = "Contoso", RevitRelease = Release,
            Actions = [HandlerProtocol.ActionOpenNewLocal], WrittenUtc = Now,
        };
        RevitProtocolFolder.WriteAtomic(_folder.RegistrationFile(Release, Handler), HandlerJson.ToUtf8(registration));
    }

    /// <summary>A Revit 2025 that is running (probe and process list), started at <paramref name="started"/>.</summary>
    private void RevitRuns(int pid, DateTime started)
    {
        _probe.Running[pid] = started;
        _lister.Processes.Add(new RevitProcessInfo(pid, started, Exe, 25));
    }

    private void RevitEnds(int pid)
    {
        _probe.Running.Remove(pid);
        _lister.Processes.RemoveAll(p => p.ProcessId == pid);
    }

    private void HandlerLoads(int pid, bool ready)
    {
        var instance = new HandlerInstance
        {
            Protocol = 1, HandlerId = Handler, RevitRelease = Release, ProcessId = pid,
            ProcessStartUtc = _probe.Running[pid], LoadedUtc = Now, ReadyUtc = ready ? Now : null,
        };
        RevitProtocolFolder.WriteAtomic(_folder.InstanceFile(Release, Handler, pid), HandlerJson.ToUtf8(instance));
    }

    private string WaitingFile(string requestId) => _folder.RequestFile(Release, Handler, requestId);

    private string OnlyRequestId()
    {
        var file = Directory.EnumerateFiles(_folder.RequestFolder(Release, Handler), "*.json").Single(f => !f.EndsWith(".result.json"));
        return Path.GetFileNameWithoutExtension(file);
    }

    private void Claim(int pid)
    {
        var id = OnlyRequestId();
        File.Move(WaitingFile(id), _folder.ClaimedFile(Release, Handler, id, pid));
    }

    private void Finish(int pid, bool ok, string? localPath, string? code = null, string? message = null, params HandlerDialog[] dialogs)
    {
        var id = Directory.EnumerateFiles(_folder.RequestFolder(Release, Handler), "*.claimed-*").Select(f => Path.GetFileName(f)[..32]).Single();
        var result = new HandlerResult
        {
            Protocol = 1, RequestId = id, HandlerId = Handler, RevitRelease = Release, ProcessId = pid, FinishedUtc = Now,
            Ok = ok, LocalPath = localPath, ErrorCode = code, Message = message, Dialogs = dialogs,
        };
        RevitProtocolFolder.WriteAtomic(_folder.ResultFile(Release, Handler, id), HandlerJson.ToUtf8(result));
    }

    private bool RequestFilesExist() =>
        Directory.Exists(_folder.RequestFolder(Release, Handler)) && Directory.EnumerateFiles(_folder.RequestFolder(Release, Handler)).Any();

    // --- Direct opens and refusals ----------------------------------------------------

    [Fact]
    public async Task DirectOpen_LaunchesTheExe_WithTheQuotedPath_AndNoEnvironment()
    {
        var plan = new RevitOpenPlan { Kind = RevitOpenKind.DirectOpen, FilePath = @"C:\My Jobs\Tower me.rvt", Release = Release, ExePath = Exe };

        var outcome = await _opener.RunAsync(plan);

        var launch = Assert.Single(_launcher.Launches);
        Assert.Equal(Exe, launch.ExePath);
        Assert.Equal("\"C:\\My Jobs\\Tower me.rvt\"", launch.Arguments);
        Assert.Empty(launch.Environment);
        Assert.Equal(RevitOpenResult.Launched, outcome.Result);
        Assert.True(outcome.Ok);
        Assert.Equal(7000, outcome.LaunchedProcessId);
        Assert.False(RequestFilesExist());
    }

    [Fact]
    public async Task DirectOpen_WhenRevitCantBeStarted_SaysSo()
    {
        _launcher.Fails = true;

        var outcome = await _opener.RunAsync(new RevitOpenPlan { Kind = RevitOpenKind.DirectOpen, FilePath = "a.rvt", Release = Release, ExePath = Exe });

        Assert.Equal(RevitOpenResult.LaunchFailed, outcome.Result);
        Assert.False(outcome.Ok);
        Assert.Contains("boom", outcome.Message);
    }

    [Fact]
    public async Task ARefusedPlan_DoesNothing()
    {
        var outcome = await _opener.RunAsync(RevitOpenPlan.Refused("a.rvt", Release, "No."));

        Assert.Equal(RevitOpenResult.Refused, outcome.Result);
        Assert.Equal("No.", outcome.Message);
        Assert.Empty(_launcher.Launches);
        Assert.False(RequestFilesExist());
    }

    // --- Handler requests: launching ---------------------------------------------------

    [Fact]
    public async Task NoRevitRunning_WritesTheRequestFirst_ThenLaunchesWithTheEnvironment()
    {
        string? idAtLaunch = null;
        _launcher.OnLaunch = _ =>
        {
            idAtLaunch = OnlyRequestId(); // the request is already on disk when Revit starts
        };

        var outcome = await _opener.RunAsync(HandlerPlan(), wait: false);

        var launch = Assert.Single(_launcher.Launches);
        Assert.Equal(Exe, launch.ExePath);
        Assert.Null(launch.Arguments);
        Assert.Equal(idAtLaunch, launch.Environment[RevitOpener.RequestVariable]);
        Assert.Equal(_folder.Root, launch.Environment[RevitOpener.RootVariable]); // a test folder isn't the default
        Assert.Equal(RevitOpenResult.Launched, outcome.Result);
        Assert.Equal(idAtLaunch, outcome.RequestId);
        Assert.Equal(7000, outcome.LaunchedProcessId);
        Assert.True(File.Exists(WaitingFile(idAtLaunch!)));
    }

    [Fact]
    public void TheRootVariable_IsSetOnlyForANonDefaultFolder()
    {
        var id = new string('a', 32);

        var custom = RevitOpener.LaunchEnvironment(new RevitProtocolFolder(@"C:\Test\revit"), id);
        var normal = RevitOpener.LaunchEnvironment(RevitProtocolFolder.Default(), id);

        Assert.Equal(@"C:\Test\revit", custom[RevitOpener.RootVariable]);
        Assert.Equal(id, normal[RevitOpener.RequestVariable]);
        Assert.False(normal.ContainsKey(RevitOpener.RootVariable));
    }

    [Fact]
    public async Task ALoadedHandler_OrAStartingRevit_MeansNoLaunch()
    {
        RevitRuns(100, Now.AddHours(-1));
        HandlerLoads(100, ready: true);
        await _opener.RunAsync(HandlerPlan(), wait: false);
        Assert.Empty(_launcher.Launches);

        RevitEnds(100);
        File.Delete(_folder.InstanceFile(Release, Handler, 100));
        RevitRuns(101, Now.AddSeconds(-30)); // started 30 s ago, no instance yet
        await _opener.RunAsync(HandlerPlan(), wait: false);
        Assert.Empty(_launcher.Launches);
    }

    [Fact]
    public async Task HandlerNotLoaded_CancelsTheRequest_AndReturnsAtOnce()
    {
        RevitRuns(100, Now.AddMinutes(-10));

        var outcome = await _opener.RunAsync(HandlerPlan());

        Assert.Equal(RevitOpenResult.HandlerNotLoaded, outcome.Result);
        Assert.Contains("isn't loaded in Revit 2025", outcome.Message);
        Assert.False(RequestFilesExist());
        Assert.Empty(_launcher.Launches);
        Assert.Equal(0, _polls);
    }

    [Fact]
    public async Task ALaunchFailure_CancelsTheRequest()
    {
        _launcher.Fails = true;

        var outcome = await _opener.RunAsync(HandlerPlan());

        Assert.Equal(RevitOpenResult.LaunchFailed, outcome.Result);
        Assert.False(RequestFilesExist());
    }

    [Fact]
    public async Task ABadRequest_IsReportedWithoutLaunching()
    {
        var outcome = await _opener.RunAsync(HandlerPlan() with { LocalFolder = "" });

        Assert.Equal(RevitOpenResult.LaunchFailed, outcome.Result);
        Assert.Empty(_launcher.Launches);
    }

    // --- Waiting -------------------------------------------------------------------

    [Fact]
    public async Task Status_GoesWaiting_Busy_Opening_ThenOpened_WithDialogs()
    {
        _launcher.OnLaunch = _ => RevitRuns(7000, Now);
        _script[4] = () => HandlerLoads(7000, ready: true);              // ready 2 s in; busy 10 s later
        _script[30] = () => Claim(7000);                                  // 15 s in
        var dialog = new HandlerDialog { DialogId = "TaskDialog_X", Kind = "taskDialog", Message = "m", Answered = false };
        _script[34] = () => Finish(7000, true, @"C:\REVIT_LOCAL2025\Tower_me.rvt", dialogs: dialog);

        var outcome = await _opener.RunAsync(HandlerPlan(), _progress);

        Assert.Equal(
            [
                (RevitOpenPhase.WaitingForRevit, "Waiting for Revit 2025 to start", (int?)7000, (int?)null),
                (RevitOpenPhase.RevitBusy, "Revit 2025 is busy", 7000, null),
                (RevitOpenPhase.Opening, "Opening in Revit 2025", 7000, 7000),
            ],
            _progress.Items.Select(s => (s.Phase, s.Text, s.LaunchedProcessId, s.ClaimedByProcessId)));
        Assert.Equal(RevitOpenResult.Opened, outcome.Result);
        Assert.Equal(@"C:\REVIT_LOCAL2025\Tower_me.rvt", outcome.LocalPath);
        Assert.Equal("TaskDialog_X", Assert.Single(outcome.DialogList).DialogId);
        Assert.Equal(7000, outcome.LaunchedProcessId);
        Assert.False(RequestFilesExist()); // result and claim cleaned up
    }

    [Fact]
    public async Task BusyIsNotReported_BeforeTenSecondsOfReadiness()
    {
        _launcher.OnLaunch = _ => RevitRuns(7000, Now);
        _script[2] = () => HandlerLoads(7000, ready: true);
        _script[12] = () => Claim(7000); // 9 s after ready
        _script[13] = () => Finish(7000, true, @"C:\L.rvt");

        await _opener.RunAsync(HandlerPlan(), _progress);

        Assert.DoesNotContain(_progress.Items, s => s.Phase == RevitOpenPhase.RevitBusy);
    }

    [Fact]
    public async Task ALoadedButNotReadyHandler_StaysWaiting()
    {
        _launcher.OnLaunch = _ => RevitRuns(7000, Now);
        _script[2] = () => HandlerLoads(7000, ready: false);
        _script[40] = () => Claim(7000);
        _script[41] = () => Finish(7000, true, @"C:\L.rvt");

        await _opener.RunAsync(HandlerPlan(), _progress);

        Assert.Equal([RevitOpenPhase.WaitingForRevit, RevitOpenPhase.Opening], _progress.Items.Select(s => s.Phase));
    }

    [Fact]
    public async Task AFailedResult_BecomesAUserMessage_AndKeepsTheLocalOnOpenFailed()
    {
        _launcher.OnLaunch = _ => RevitRuns(7000, Now);
        _script[2] = () => Claim(7000);
        _script[3] = () => Finish(7000, false, @"C:\REVIT_LOCAL2025\Tower_me.rvt", "openFailed", "Dialog cancelled.");

        var outcome = await _opener.RunAsync(HandlerPlan());

        Assert.Equal(RevitOpenResult.Failed, outcome.Result);
        Assert.Equal("openFailed", outcome.ErrorCode);
        Assert.Equal(@"C:\REVIT_LOCAL2025\Tower_me.rvt", outcome.LocalPath);
        Assert.Contains(@"The local was created at C:\REVIT_LOCAL2025\Tower_me.rvt", outcome.Message);
        Assert.Contains("Dialog cancelled.", outcome.Message);
    }

    [Theory]
    [InlineData("notCentral", "isn't a central")]
    [InlineData("releaseMismatch", "different release")]
    [InlineData("somethingNew", "(internalError)")]
    public async Task OtherErrorCodes_ShowTheHandlersMessage(string code, string expected)
    {
        _launcher.OnLaunch = _ => RevitRuns(7000, Now);
        _script[2] = () => Claim(7000);
        _script[3] = () => Finish(7000, false, null, code, "Because.");

        var outcome = await _opener.RunAsync(HandlerPlan());

        Assert.Equal(RevitOpenResult.Failed, outcome.Result);
        Assert.Contains(expected, outcome.Message);
        Assert.Contains("Because.", outcome.Message);
    }

    [Fact]
    public async Task TheLaunchedRevitEndingBeforeItClaims_EndsTheWait_AndCleansUp()
    {
        _launcher.OnLaunch = _ => RevitRuns(7000, Now);
        _script[5] = () => RevitEnds(7000);

        var outcome = await _opener.RunAsync(HandlerPlan(), _progress);

        Assert.Equal(RevitOpenResult.RevitExited, outcome.Result);
        Assert.Contains("ended before it picked up the request", outcome.Message);
        Assert.False(RequestFilesExist());
        Assert.Equal(5, _polls); // stopped at the poll after the exit, not at expiry
    }

    [Fact]
    public async Task TheClaimingRevitDying_EndsTheWait_AndRemovesTheClaim()
    {
        _launcher.OnLaunch = _ => RevitRuns(7000, Now);
        _script[2] = () => Claim(7000);
        _script[6] = () => RevitEnds(7000);

        var outcome = await _opener.RunAsync(HandlerPlan());

        Assert.Equal(RevitOpenResult.ClaimerDied, outcome.Result);
        Assert.Contains("ended while it was opening", outcome.Message);
        Assert.False(RequestFilesExist());
    }

    [Fact]
    public async Task ARequestNobodyClaims_Expires_WithTheSpecsMessage()
    {
        _launcher.OnLaunch = _ => RevitRuns(7000, Now);

        var outcome = await _opener.RunAsync(HandlerPlan());

        Assert.Equal(RevitOpenResult.Expired, outcome.Result);
        Assert.Equal("Revit didn't pick up the request in time.", outcome.Message);
        Assert.False(RequestFilesExist());
        Assert.True(Now > Start.UtcDateTime.AddMinutes(10));
    }

    [Fact]
    public async Task CallerCancellation_CancelsAnUnclaimedRequest()
    {
        _launcher.OnLaunch = _ => RevitRuns(7000, Now);
        using var cts = new CancellationTokenSource();
        _script[3] = cts.Cancel;

        var outcome = await _opener.RunAsync(HandlerPlan(), cancellationToken: cts.Token);

        Assert.Equal(RevitOpenResult.Cancelled, outcome.Result);
        Assert.Equal("Cancelled.", outcome.Message);
        Assert.False(RequestFilesExist());
        Assert.Equal(7000, outcome.LaunchedProcessId);
    }

    [Fact]
    public async Task CallerCancellation_AfterTheClaim_LeavesTheClaim_AndSaysSo()
    {
        _launcher.OnLaunch = _ => RevitRuns(7000, Now);
        using var cts = new CancellationTokenSource();
        _script[2] = () => Claim(7000);
        _script[3] = cts.Cancel;

        var outcome = await _opener.RunAsync(HandlerPlan(), cancellationToken: cts.Token);

        Assert.Equal(RevitOpenResult.Cancelled, outcome.Result);
        Assert.Contains("already started opening", outcome.Message);
        Assert.True(RequestFilesExist());
    }

    private sealed class ListProgress : IProgress<RevitOpenStatus>
    {
        public List<RevitOpenStatus> Items { get; } = [];
        public void Report(RevitOpenStatus value) => Items.Add(value);
    }
}
