using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using QuickerPlaces.Services.Revit.Handlers;

namespace QuickerPlaces.Services.Revit.Opening;

/// <summary>How the open is getting on; <see cref="RevitOpenStatus.Text"/> is what the user reads.</summary>
public enum RevitOpenPhase
{
    /// <summary>"Waiting for Revit 2025 to start": the handler has no ready instance yet.</summary>
    WaitingForRevit,

    /// <summary>"Revit 2025 is busy": a ready instance hasn't claimed the request after 10 seconds.</summary>
    RevitBusy,

    /// <summary>"Opening in Revit 2025": a handler claimed the request.</summary>
    Opening,
}

/// <summary>
/// A progress report. <see cref="LaunchedProcessId"/> is the Revit that
/// QuickerPlaces started for this open (null if it didn't), for the caller to
/// watch for dialogs; <see cref="ClaimedByProcessId"/> is the Revit working
/// on the request, once known.
/// </summary>
public sealed record RevitOpenStatus(RevitOpenPhase Phase, string Text, int? LaunchedProcessId, int? ClaimedByProcessId);

public enum RevitOpenResult
{
    /// <summary>The handler made the local and opened it.</summary>
    Opened,

    /// <summary>Revit was launched with the file (a direct open), or, with no wait, the request was written; nothing more is watched.</summary>
    Launched,

    /// <summary>The plan said no; nothing was written or started.</summary>
    Refused,

    /// <summary>A Revit of the release is running without the handler; the request was withdrawn.</summary>
    HandlerNotLoaded,

    /// <summary>The handler reported a failure (see <see cref="RevitOpenOutcome.ErrorCode"/>).</summary>
    Failed,

    /// <summary>Nobody claimed the request before it expired.</summary>
    Expired,

    /// <summary>The Revit QuickerPlaces launched ended before claiming the request.</summary>
    RevitExited,

    /// <summary>The Revit that claimed the request ended without a result.</summary>
    ClaimerDied,

    /// <summary>The caller cancelled.</summary>
    Cancelled,

    /// <summary>Revit couldn't be started, or the request couldn't be written.</summary>
    LaunchFailed,
}

/// <summary>How an open ended. <see cref="Dialogs"/> are what Revit raised during the open: evidence for a handler's allowlist.</summary>
public sealed record RevitOpenOutcome(
    RevitOpenResult Result,
    string Message,
    string? LocalPath = null,
    string? ErrorCode = null,
    string? RequestId = null,
    int? LaunchedProcessId = null,
    IReadOnlyList<HandlerDialog>? Dialogs = null)
{
    public bool Ok => Result is RevitOpenResult.Opened or RevitOpenResult.Launched;

    public IReadOnlyList<HandlerDialog> DialogList => Dialogs ?? [];
}

/// <summary>
/// Carries out a <see cref="RevitOpenPlan"/> (roadmap §4.21). A direct open
/// launches <c>Revit.exe</c> with the quoted file. A handler request is
/// written first, so a starting handler finds it; then, only if the handler
/// is not loaded in a running Revit of the release (re-checked after the
/// write), Revit is launched with <c>QUICKERPLACES_REVIT_REQUEST</c> (and
/// <c>QUICKERPLACES_REVIT_ROOT</c> when the protocol root isn't the
/// default), and the wait reports status. Never ends a Revit process.
///
/// A launched Revit that exits before claiming is recognised by its process
/// id alone (a reused id would be missed; the request still expires). UI-free.
/// </summary>
public sealed class RevitOpener
{
    public const string RequestVariable = "QUICKERPLACES_REVIT_REQUEST";
    public const string RootVariable = "QUICKERPLACES_REVIT_ROOT";

    /// <summary>A ready handler that hasn't claimed a request after this long is called busy.</summary>
    public static readonly TimeSpan BusyAfter = TimeSpan.FromSeconds(10);

    private readonly RevitProtocolFolder _folder;
    private readonly RevitRequestQueue _queue;
    private readonly RevitHandlerRegistry _registry;
    private readonly RevitMachine _machine;
    private readonly TimeProvider _time;

    /// <param name="queue">Shares the folder, clock and pause with the caller, so tests control all three.</param>
    public RevitOpener(RevitProtocolFolder folder, RevitRequestQueue queue, RevitHandlerRegistry registry, RevitMachine machine, TimeProvider? time = null)
    {
        _folder = folder;
        _queue = queue;
        _registry = registry;
        _machine = machine;
        _time = time ?? TimeProvider.System;
    }

    public async Task<RevitOpenOutcome> RunAsync(RevitOpenPlan plan, IProgress<RevitOpenStatus>? progress = null,
        bool wait = true, CancellationToken cancellationToken = default)
    {
        switch (plan.Kind)
        {
            case RevitOpenKind.DirectOpen:
                return DirectOpen(plan);
            case RevitOpenKind.HandlerRequest:
                return await HandlerOpenAsync(plan, progress, wait, cancellationToken).ConfigureAwait(false);
            default:
                return new RevitOpenOutcome(RevitOpenResult.Refused, plan.RefusalReason ?? "The file isn't opened.");
        }
    }

    private RevitOpenOutcome DirectOpen(RevitOpenPlan plan)
    {
        try
        {
            var pid = _machine.Launcher.Launch(new RevitLaunch(plan.ExePath!, "\"" + plan.FilePath + "\"", new Dictionary<string, string>()));
            return new RevitOpenOutcome(RevitOpenResult.Launched, $"Revit {plan.Release} is opening {plan.FilePath[(plan.FilePath.LastIndexOfAny(['\\', '/']) + 1)..]}.", LaunchedProcessId: pid);
        }
        catch (IOException ex)
        {
            return new RevitOpenOutcome(RevitOpenResult.LaunchFailed, ex.Message);
        }
    }

    private async Task<RevitOpenOutcome> HandlerOpenAsync(RevitOpenPlan plan, IProgress<RevitOpenStatus>? progress, bool wait, CancellationToken token)
    {
        var release = plan.Release!;
        var handlerId = plan.HandlerId!;

        // The request goes first: a handler that is just starting finds it.
        var creation = _queue.CreateOpenNewLocal(release, handlerId, plan.FilePath, plan.LocalFolder!, plan.Worksets!);
        if (creation.Request is not { } request)
            return new RevitOpenOutcome(RevitOpenResult.LaunchFailed, creation.Problem ?? "The request couldn't be written.");

        // What is running may have changed since planning, so look again now that the request is there.
        var availability = CurrentAvailability(release, handlerId);
        if (availability == HandlerAvailability.NotLoaded)
        {
            _queue.Cancel(request);
            return new RevitOpenOutcome(RevitOpenResult.HandlerNotLoaded,
                $"The handler isn't loaded in Revit {release}. Check that it is installed for Revit {release} and restart that Revit.", RequestId: request.RequestId);
        }

        int? launched = null;
        if (availability == HandlerAvailability.NotRunning)
        {
            try
            {
                launched = _machine.Launcher.Launch(new RevitLaunch(plan.ExePath!, null, LaunchEnvironment(_folder, request.RequestId)));
            }
            catch (IOException ex)
            {
                _queue.Cancel(request);
                return new RevitOpenOutcome(RevitOpenResult.LaunchFailed, ex.Message, RequestId: request.RequestId);
            }
        }

        if (!wait)
        {
            return new RevitOpenOutcome(RevitOpenResult.Launched,
                $"The request is waiting for Revit {release}.", RequestId: request.RequestId, LaunchedProcessId: launched);
        }

        var reporter = new Reporter(progress);
        var stop = RevitOpenResult.Opened; // Opened means "not stopped by us".
        int? claimer = null;

        bool StopWhen(RequestStatus status)
        {
            if (status.State == RequestState.Claimed)
            {
                claimer = status.ClaimedByProcessId;
                reporter.Report(new RevitOpenStatus(RevitOpenPhase.Opening, $"Opening in Revit {release}", launched, claimer));
                if (claimer is { } pid && _machine.Probe.GetStartTimeUtc(pid) is null)
                {
                    stop = RevitOpenResult.ClaimerDied;
                    return true;
                }
                return false;
            }

            if (launched is { } launchedPid && _machine.Probe.GetStartTimeUtc(launchedPid) is null)
            {
                stop = RevitOpenResult.RevitExited;
                return true;
            }

            var phase = RevitOpenPhase.WaitingForRevit;
            var text = $"Waiting for Revit {release} to start";
            var now = _time.GetUtcNow().UtcDateTime;
            var ready = ReadyInstances(release, handlerId);
            if (ready.Count > 0)
            {
                var since = new[] { request.CreatedUtc }.Concat(ready.Select(i => i.ReadyUtc!.Value)).Max();
                if (now - since >= BusyAfter)
                {
                    phase = RevitOpenPhase.RevitBusy;
                    text = $"Revit {release} is busy";
                }
            }
            reporter.Report(new RevitOpenStatus(phase, text, launched, null));
            return false;
        }

        RequestStatus final;
        try
        {
            final = await _queue.WaitAsync(request, null, null, token, StopWhen).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            var cancelled = _queue.Cancel(request);
            return new RevitOpenOutcome(RevitOpenResult.Cancelled,
                cancelled == RequestCancellation.AlreadyClaimed
                    ? $"Cancelled. Revit {release} had already started opening the file, so that carries on."
                    : "Cancelled.",
                RequestId: request.RequestId, LaunchedProcessId: launched);
        }

        if (stop == RevitOpenResult.ClaimerDied)
        {
            // It may have written its result just before it ended.
            var again = _queue.GetStatus(request);
            if (again.State == RequestState.Finished && again.Result is { } late)
            {
                _queue.Discard(request);
                return FromResult(late, release, request.RequestId, launched);
            }
            _queue.Discard(request);
            return new RevitOpenOutcome(RevitOpenResult.ClaimerDied,
                $"Revit {release} ended while it was opening the file, without saying how it went. Check whether the local was created.",
                RequestId: request.RequestId, LaunchedProcessId: launched);
        }

        if (stop == RevitOpenResult.RevitExited)
        {
            var cancelled = _queue.Cancel(request);
            return new RevitOpenOutcome(RevitOpenResult.RevitExited,
                cancelled == RequestCancellation.AlreadyClaimed
                    ? $"The Revit {release} that was started ended; another Revit {release} took the request."
                    : $"The Revit {release} that was started ended before it picked up the request.",
                RequestId: request.RequestId, LaunchedProcessId: launched);
        }

        switch (final.State)
        {
            case RequestState.Finished when final.Result is { } result:
                return FromResult(result, release, request.RequestId, launched);
            case RequestState.Expired:
                return new RevitOpenOutcome(RevitOpenResult.Expired, "Revit didn't pick up the request in time.",
                    RequestId: request.RequestId, LaunchedProcessId: launched);
            default:
                return new RevitOpenOutcome(RevitOpenResult.Failed, "The request disappeared before Revit handled it.",
                    RequestId: request.RequestId, LaunchedProcessId: launched);
        }
    }

    private static RevitOpenOutcome FromResult(HandlerResult result, string release, string requestId, int? launched)
    {
        if (result.Ok)
            return new RevitOpenOutcome(RevitOpenResult.Opened, $"Opened in Revit {release}.", result.LocalPath, null, requestId, launched, result.Dialogs);

        var code = result.EffectiveErrorCode!;
        var detail = string.IsNullOrWhiteSpace(result.Message) ? "" : " " + result.Message.Trim();
        var message = code switch
        {
            HandlerProtocol.ErrorCodes.OpenFailed when !string.IsNullOrEmpty(result.LocalPath) =>
                $"The local was created at {result.LocalPath}, but Revit {release} couldn't open it.{detail} You can open it yourself.",
            HandlerProtocol.ErrorCodes.Expired => "Revit didn't pick up the request in time.",
            HandlerProtocol.ErrorCodes.CentralNotFound => "Revit can't reach the central model." + detail,
            HandlerProtocol.ErrorCodes.NotCentral => "Revit says this isn't a central model." + detail,
            HandlerProtocol.ErrorCodes.ReleaseMismatch => $"The file was saved in a different release from the running Revit.{detail}",
            HandlerProtocol.ErrorCodes.LocalFolderUnavailable => "The local folder can't be created or written." + detail,
            HandlerProtocol.ErrorCodes.CreateLocalFailed => "Revit couldn't create the local." + detail,
            _ => $"The handler couldn't open the file ({code})." + detail,
        };
        return new RevitOpenOutcome(RevitOpenResult.Failed, message, result.LocalPath, code, requestId, launched, result.Dialogs);
    }

    private HandlerAvailability CurrentAvailability(string release, string handlerId)
    {
        var handler = _registry.Read(release).FirstOrDefault(h => h.HandlerId == handlerId);
        var running = _machine.RunningRevits().Where(r => r.ReleaseText == release).ToList();
        if (handler is null)
            return running.Count == 0 ? HandlerAvailability.NotRunning : HandlerAvailability.Starting;
        return RevitOpenPlanner.Assess(handler, running, _time.GetUtcNow().UtcDateTime);
    }

    private List<LiveHandlerInstance> ReadyInstances(string release, string handlerId) =>
        _registry.Read(release).FirstOrDefault(h => h.HandlerId == handlerId)?.Instances.Where(i => i.IsReady).ToList() ?? [];

    internal static Dictionary<string, string> LaunchEnvironment(RevitProtocolFolder folder, string requestId)
    {
        var environment = new Dictionary<string, string> { [RequestVariable] = requestId };
        if (!SameFolder(folder.Root, RevitProtocolFolder.Default().Root))
            environment[RootVariable] = folder.Root;
        return environment;
    }

    private static bool SameFolder(string a, string b) =>
        string.Equals(a.TrimEnd('\\', '/'), b.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

    /// <summary>Passes on a status only when it differs from the last one.</summary>
    private sealed class Reporter(IProgress<RevitOpenStatus>? progress)
    {
        private RevitOpenStatus? _last;

        public void Report(RevitOpenStatus status)
        {
            if (status == _last)
                return;
            _last = status;
            progress?.Report(status);
        }
    }
}
