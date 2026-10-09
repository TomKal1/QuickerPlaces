using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using QuickerPlaces.Models;
using QuickerPlaces.Services;
using QuickerPlaces.Services.Revit;
using QuickerPlaces.Services.Revit.AddIns;
using QuickerPlaces.Services.Revit.Dialogs;
using QuickerPlaces.Services.Revit.Handlers;
using QuickerPlaces.Services.Revit.Opening;
using QuickerPlaces.Tests.Fakes;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>
/// The Library's Revit open from end to end against a temporary protocol
/// folder, a fake clock, a fake launcher and a fake dialog detector. As in
/// the opener's tests, the handler's part is played from the pause between
/// polls: each pause advances the clock and runs the script for that poll.
/// The first look at the launched Revit's windows happens before the first
/// pause. No test sleeps.
/// </summary>
public sealed class RevitOpenCoordinatorTests : IDisposable
{
    private const string Release = "2025";
    private const string Handler = "contoso";
    private const string Central = @"\\server\projects\Tower_Central.rvt";
    private const string Dll = @"C:\Addins\Tool.dll";
    private const string Hash = "aaaa";
    private const int Launched = 7000;

    private static readonly DateTimeOffset Start = new(2026, 10, 9, 9, 0, 0, TimeSpan.Zero);

    private readonly TempDirectory _dir = new();
    private readonly RevitProtocolFolder _folder;
    private readonly ManualTimeProvider _time = new(Start);
    private readonly FakeProcessProbe _probe = new();
    private readonly FakeLauncher _launcher = new();
    private readonly FakeProcessLister _lister = new();
    private readonly FakeInstallSource _installs = new();
    private readonly FakeDialogDetector _detector = new();
    private readonly AppSettings _settings = new();
    private readonly SettingsService _settingsService;
    private readonly FakePrompts _prompts = new();
    private readonly Dictionary<int, Action> _script = [];
    private readonly List<string> _status = [];
    private readonly RevitReleaseCache _cache;
    private readonly string _revitExe;
    private SecurityPromptSignatures _signatures = SecurityPromptSignatures.Empty;
    private RevitFileInfo _info = new(2025, "b", RevitWorksharing.Central, Central, Central, 14, RevitFileProblem.None);
    private IReadOnlyList<AddInManifest> _manifests = [];
    private int _polls;

    public RevitOpenCoordinatorTests()
    {
        _folder = new RevitProtocolFolder(Path.Combine(_dir.Path, "revit"));
        _settingsService = new SettingsService(_dir.File("settings.json"));
        _revitExe = _installs.AddDefault(2025);
        _cache = new RevitReleaseCache(_ => _info, _ => new FileStamp(Start.UtcDateTime, 1), _time);
        _settings.RevitReleases!["2025"] = new RevitReleaseSettings { HandlerId = Handler };
        Register();
        _launcher.OnLaunch = _ => RevitRuns(Launched);
    }

    public void Dispose() => _dir.Dispose();

    private DateTime Now => _time.GetUtcNow().UtcDateTime;

    private RevitOpenCoordinator NewCoordinator(IDialogDetector? detector = null)
    {
        var queue = new RevitRequestQueue(_folder, _time, null, (interval, token) =>
        {
            token.ThrowIfCancellationRequested();
            _polls++;
            _time.Advance(interval);
            if (_script.TryGetValue(_polls, out var action))
                action();
            return Task.CompletedTask;
        });
        var machine = new RevitMachine { InstallSource = _installs, Processes = _lister, Launcher = _launcher, Probe = _probe };
        return new RevitOpenCoordinator(machine, _folder, _cache, detector ?? _detector, _settings, _settingsService, _prompts, _time, queue,
            () => _signatures, _ => _manifests, path => path == Dll ? Hash : null);
    }

    private Task<RevitOpenReport> Open(RevitOpenCoordinator coordinator, CancellationToken token = default)
        => coordinator.OpenAsync(Central, new ListStatus(_status), token);

    private void Register()
    {
        var registration = new HandlerRegistration
        {
            Protocol = 1, HandlerId = Handler, DisplayName = "Contoso", RevitRelease = Release,
            Actions = [HandlerProtocol.ActionOpenNewLocal], WrittenUtc = Now,
        };
        RevitProtocolFolder.WriteAtomic(_folder.RegistrationFile(Release, Handler), HandlerJson.ToUtf8(registration));
    }

    private void RevitRuns(int pid)
    {
        _probe.Running[pid] = Now;
        _lister.Processes.Add(new RevitProcessInfo(pid, Now, _revitExe, 25));
    }

    private void HandlerLoads(int pid)
    {
        var instance = new HandlerInstance
        {
            Protocol = 1, HandlerId = Handler, RevitRelease = Release, ProcessId = pid,
            ProcessStartUtc = _probe.Running[pid], LoadedUtc = Now, ReadyUtc = Now,
        };
        RevitProtocolFolder.WriteAtomic(_folder.InstanceFile(Release, Handler, pid), HandlerJson.ToUtf8(instance));
    }

    private string RequestId() => Path.GetFileNameWithoutExtension(
        Directory.EnumerateFiles(_folder.RequestFolder(Release, Handler), "*.json").Single(f => !f.EndsWith(".result.json")));

    private void Claim(int pid)
    {
        var id = RequestId();
        File.Move(_folder.RequestFile(Release, Handler, id), _folder.ClaimedFile(Release, Handler, id, pid));
    }

    private void Finish(int pid, string? localPath, params HandlerDialog[] dialogs)
    {
        var id = Directory.EnumerateFiles(_folder.RequestFolder(Release, Handler), "*.claimed-*").Select(f => Path.GetFileName(f)[..32]).Single();
        var result = new HandlerResult
        {
            Protocol = 1, RequestId = id, HandlerId = Handler, RevitRelease = Release, ProcessId = pid, FinishedUtc = Now,
            Ok = true, LocalPath = localPath, Dialogs = dialogs,
        };
        RevitProtocolFolder.WriteAtomic(_folder.ResultFile(Release, Handler, id), HandlerJson.ToUtf8(result));
    }

    /// <summary>Plays a normal cold start: the handler loads at the second pause, claims at the third and finishes at the fourth.</summary>
    private void NormalStart(string? localPath = @"C:\REVIT_LOCAL2025\Tower_Central_me.rvt")
    {
        _script[2] = () => HandlerLoads(Launched);
        _script[3] = () => Claim(Launched);
        _script[4] = () => Finish(Launched, localPath);
    }

    private static LoadOnceEntry Entry(string hash = Hash) => new()
    {
        Release = 2025, AddInId = "ID-1", Name = "Tool", DllPath = Dll, DllSha256 = hash, AllowedUtc = Start,
    };

    private static AddInManifest Manifest() =>
        new(2025, AddInKind.Application, "Tool", Dll, "ID-1", "ACME", "Tool.App", @"C:\m\Tool.addin", ManifestSource.UserAddins);

    private void ShowPrompt(out DialogWindow prompt)
    {
        prompt = DialogFixtures.SecurityPrompt("Tool", Dll);
        _detector.Windows[Launched] = [prompt];
    }

    private void RemovePrompt() => _detector.Windows[Launched] = [];

    private LoadOnceEntry[] SavedEntries() => (_settingsService.Load().LoadOnceEntries ?? []).ToArray();

    // --- The open itself ----------------------------------------------------------------

    [Fact]
    public async Task ACentral_IsOpenedThroughTheHandler_AndSaysWhichLocal()
    {
        NormalStart();

        var report = await Open(NewCoordinator());

        Assert.Equal(RevitOpenResult.Opened, report.Outcome.Result);
        Assert.False(report.IsError);
        Assert.Equal("Opened Tower_Central_me.rvt in Revit 2025.", report.Message);
        Assert.Single(_launcher.Launches);
        Assert.Equal(new[] { "Checking Tower_Central.rvt…", "Waiting for Revit 2025 to start", "Opening in Revit 2025" }, _status);
    }

    [Fact]
    public async Task ARefusal_IsAnErrorWithThePlannersReason_AndNothingStarts()
    {
        _settings.RevitReleases!.Clear();

        var report = await Open(NewCoordinator());

        Assert.Equal(RevitOpenResult.Refused, report.Outcome.Result);
        Assert.True(report.IsError);
        Assert.Contains("none is chosen for Revit 2025", report.Message);
        Assert.Empty(_launcher.Launches);
    }

    [Fact]
    public async Task AFileThatCantBeRead_IsRefusedWithoutThrowing()
    {
        _info = RevitFileInfo.Failed(RevitFileProblem.NotFound);

        var report = await Open(NewCoordinator());

        Assert.True(report.IsError);
        Assert.Contains("isn't there", report.Message);
    }

    [Fact]
    public async Task ALocal_IsOpenedDirectly_AndNothingIsWatched()
    {
        _info = new RevitFileInfo(2025, "b", RevitWorksharing.Local, Central, Central, 14, RevitFileProblem.None);

        var report = await Open(NewCoordinator());

        Assert.Equal(RevitOpenResult.Launched, report.Outcome.Result);
        Assert.False(report.IsError);
        Assert.Contains("Revit 2025 is opening", report.Message);
    }

    [Fact]
    public async Task OpenedTheLog_SaysHowManyDialogsRevitRaised()
    {
        NormalStart();
        _script[4] = () => Finish(Launched, @"C:\L\x.rvt",
            new HandlerDialog { DialogId = "TaskDialog_Missing_Third_Party_Updater", Kind = "taskDialog", Answered = false });

        await Open(NewCoordinator());

        var log = File.ReadAllText(DiagnosticLog.LogFilePath);
        Assert.Contains("1 dialog(s) raised: TaskDialog_Missing_Third_Party_Updater (taskDialog, left)", log);
    }

    [Fact]
    public async Task Cancelling_WhileWaiting_IsNotAnError()
    {
        using var cts = new CancellationTokenSource();
        _script[1] = cts.Cancel;

        var report = await Open(NewCoordinator(), cts.Token);

        Assert.Equal(RevitOpenResult.Cancelled, report.Outcome.Result);
        Assert.False(report.IsError);
        Assert.Equal("Cancelled.", report.Message);
    }

    [Fact]
    public async Task Cancelling_BeforeItStarts_IsNotAnError()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var report = await Open(NewCoordinator(), cts.Token);

        Assert.Equal(RevitOpenResult.Cancelled, report.Outcome.Result);
        Assert.False(report.IsError);
        Assert.Empty(_launcher.Launches);
    }

    // --- Dialogs ------------------------------------------------------------------------

    [Fact]
    public async Task ADialogInTheLaunchedRevit_BecomesTheStatus_UntilItGoes()
    {
        NormalStart();
        _detector.Windows[Launched] = [DialogFixtures.Dialog("Welcome", ["OK"], ["Hello"])];
        _script[1] = () => _detector.Windows[Launched] = [];

        await Open(NewCoordinator());

        Assert.Equal(new[]
        {
            "Checking Tower_Central.rvt…", "Revit is waiting on a dialog: Welcome", "Waiting for Revit 2025 to start", "Opening in Revit 2025",
        }, _status);
    }

    [Fact]
    public async Task WhenRevitWasAlreadyRunning_NoWindowsAreLookedAt()
    {
        _probe.Running[4321] = Now.AddHours(-1);
        _lister.Processes.Add(new RevitProcessInfo(4321, Now.AddHours(-1), _revitExe, 25));
        HandlerLoads(4321);
        var counting = new CountingDetector(_detector);
        _script[1] = () => Claim(4321);
        _script[2] = () => Finish(4321, @"C:\L\x.rvt");

        var report = await Open(NewCoordinator(counting));

        Assert.Equal(RevitOpenResult.Opened, report.Outcome.Result);
        Assert.Empty(_launcher.Launches);
        Assert.Equal(0, counting.Lists);
    }

    [Fact]
    public async Task ADetectorThatThrows_NeverEndsTheOpen()
    {
        NormalStart();

        var report = await Open(NewCoordinator(new ThrowingDetector()));

        Assert.Equal(RevitOpenResult.Opened, report.Outcome.Result);
    }

    [Fact]
    public async Task LookingStopsOnceTheHandlerHasAppeared()
    {
        NormalStart();
        var counting = new CountingDetector(_detector);

        await Open(NewCoordinator(counting));

        // Two looks per poll (the status, then the Load Once check) in the two polls before the handler loaded,
        // one as it appeared, and none after: not while it claimed or finished, nor in the final settle.
        Assert.Equal(5, counting.Lists);
    }

    // --- Load Once: pressing ------------------------------------------------------------

    private void AllowTool(bool switchOn = true, string hash = Hash)
    {
        _settings.AllowLoadOnce = switchOn;
        _settings.LoadOnceEntries!.Add(Entry(hash));
        _signatures = DialogFixtures.Table(DialogFixtures.Signature());
        _manifests = [Manifest()];
    }

    [Fact]
    public async Task AnAllowedAddIn_GetsLoadOnce_AndIsNotOfferedAgain()
    {
        NormalStart();
        AllowTool();
        ShowPrompt(out _);
        _script[1] = RemovePrompt;

        var report = await Open(NewCoordinator());

        var pressed = Assert.Single(_detector.Pressed);
        Assert.Equal(DialogFixtures.LoadOnce, pressed.Text);
        Assert.Empty(_prompts.Questions);
        Assert.Equal(RevitOpenResult.Opened, report.Outcome.Result);
    }

    [Fact]
    public async Task WithTheSwitchOff_NothingIsPressed_AndTheUsersAnswerIsNotOfferedAnEntryTwice()
    {
        NormalStart();
        AllowTool(switchOn: false);
        ShowPrompt(out _);
        _script[1] = RemovePrompt;

        await Open(NewCoordinator());

        Assert.Empty(_detector.Pressed);
        Assert.Empty(_prompts.Questions); // already on the list, same DLL
    }

    [Fact]
    public async Task APromptTheUserAnswers_IsOffered_AndAYesSavesTheEntry()
    {
        NormalStart();
        _signatures = DialogFixtures.Table(DialogFixtures.Signature());
        _manifests = [Manifest()];
        ShowPrompt(out _);
        _script[1] = RemovePrompt;
        _prompts.Answer = true;

        await Open(NewCoordinator());

        Assert.Equal(new[] { "Allow Load Once for Tool next time?" }, _prompts.Questions);
        var saved = Assert.Single(SavedEntries());
        Assert.Equal(("Tool", 2025, Dll, Hash), (saved.Name, saved.Release, saved.DllPath, saved.DllSha256));
        Assert.Single(_settings.LoadOnceEntries!);
        Assert.Empty(_detector.Pressed);
    }

    [Fact]
    public async Task ANo_AddsNothing()
    {
        NormalStart();
        _signatures = DialogFixtures.Table(DialogFixtures.Signature());
        _manifests = [Manifest()];
        ShowPrompt(out _);
        _script[1] = RemovePrompt;
        _prompts.Answer = false;

        await Open(NewCoordinator());

        Assert.Single(_prompts.Questions);
        Assert.Empty(_settings.LoadOnceEntries!);
        Assert.Empty(SavedEntries());
    }

    [Fact]
    public async Task AnAnswerThatCameAsTheHandlerAppeared_IsStillSeen()
    {
        _signatures = DialogFixtures.Table(DialogFixtures.Signature());
        _manifests = [Manifest()];
        ShowPrompt(out _);
        _script[2] = () =>
        {
            RemovePrompt();
            HandlerLoads(Launched);
        };
        _script[3] = () => Claim(Launched);
        _script[4] = () => Finish(Launched, @"C:\L\x.rvt");
        _prompts.Answer = true;

        await Open(NewCoordinator());

        Assert.Single(_prompts.Questions);
        Assert.Single(SavedEntries());
    }

    [Fact]
    public async Task WithoutAVerifiedSignature_NothingIsRecognised_OrOffered()
    {
        NormalStart();
        _signatures = DialogFixtures.Table(DialogFixtures.Signature(verified: false));
        _manifests = [Manifest()];
        ShowPrompt(out _);
        _script[1] = RemovePrompt;

        await Open(NewCoordinator());

        Assert.Empty(_prompts.Questions);
        Assert.Empty(_detector.Pressed);
    }

    [Fact]
    public async Task APromptThatVanishesBecauseRevitEnded_IsNotAnAnswer()
    {
        _signatures = DialogFixtures.Table(DialogFixtures.Signature());
        _manifests = [Manifest()];
        ShowPrompt(out _);
        _script[1] = () =>
        {
            RemovePrompt();
            _probe.Running.Remove(Launched);
            _lister.Processes.Clear();
        };

        var report = await Open(NewCoordinator());

        Assert.Equal(RevitOpenResult.RevitExited, report.Outcome.Result);
        Assert.Empty(_prompts.Questions);
    }

    [Fact]
    public async Task AnAddInNoManifestMatches_IsNotOffered()
    {
        NormalStart();
        _signatures = DialogFixtures.Table(DialogFixtures.Signature());
        _manifests = [];
        ShowPrompt(out _);
        _script[1] = RemovePrompt;

        await Open(NewCoordinator());

        Assert.Empty(_prompts.Questions);
        Assert.Empty(_settings.LoadOnceEntries!);
    }

    // --- Load Once: a changed DLL -------------------------------------------------------

    [Fact]
    public async Task AChangedDll_IsNotPressed_ItsEntryGoes_AndTheUserIsAskedAgain()
    {
        NormalStart();
        AllowTool(hash: "old");
        ShowPrompt(out _);
        _script[1] = RemovePrompt;
        _prompts.Answer = true;

        await Open(NewCoordinator());

        Assert.Empty(_detector.Pressed);
        Assert.Equal(new[] { "Tool changed since you allowed it — allow Load Once again?" }, _prompts.Questions);
        var saved = Assert.Single(SavedEntries());
        Assert.Equal(Hash, saved.DllSha256);
    }

    [Fact]
    public async Task AChangedDll_AnsweredNo_LeavesTheListWithoutIt()
    {
        NormalStart();
        AllowTool(hash: "old");
        ShowPrompt(out _);
        _script[1] = RemovePrompt;
        _prompts.Answer = false;

        await Open(NewCoordinator());

        Assert.Single(_prompts.Questions);
        Assert.Empty(_settings.LoadOnceEntries!);
        Assert.Empty(SavedEntries());
    }

    [Fact]
    public async Task AQuestionThatThrows_DoesNotSpoilTheReport()
    {
        NormalStart();
        _signatures = DialogFixtures.Table(DialogFixtures.Signature());
        _manifests = [Manifest()];
        ShowPrompt(out _);
        _script[1] = RemovePrompt;
        _prompts.Throws = true;

        var report = await Open(NewCoordinator());

        Assert.Equal(RevitOpenResult.Opened, report.Outcome.Result);
        Assert.False(report.IsError);
    }

    // --- Helpers ------------------------------------------------------------------------

    private sealed class ListStatus(List<string> items) : IProgress<string>
    {
        public void Report(string value) => items.Add(value);
    }

    private sealed class FakePrompts : IRevitOpenPrompts
    {
        public List<string> Questions { get; } = [];
        public bool Answer { get; set; }
        public bool Throws { get; set; }

        public Task<bool> AskAsync(string question)
        {
            Questions.Add(question);
            if (Throws)
                throw new InvalidOperationException("no dialog");
            return Task.FromResult(Answer);
        }
    }

    private sealed class CountingDetector(IDialogDetector inner) : IDialogDetector
    {
        public int Lists { get; private set; }

        public IReadOnlyList<DialogWindow> ListWindows(int processId)
        {
            Lists++;
            return inner.ListWindows(processId);
        }

        public bool PressButton(DialogButton button) => inner.PressButton(button);
    }

    private sealed class ThrowingDetector : IDialogDetector
    {
        public IReadOnlyList<DialogWindow> ListWindows(int processId) => throw new InvalidOperationException("no windows");
        public bool PressButton(DialogButton button) => false;
    }
}
