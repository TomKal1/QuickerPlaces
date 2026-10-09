using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace QuickerPlaces.Services.Revit.Handlers;

/// <summary>Where a request is in its life, as QuickerPlaces sees it.</summary>
public enum RequestState
{
    /// <summary>Written and unclaimed: <c>&lt;requestId&gt;.json</c> exists.</summary>
    Waiting,

    /// <summary>A handler renamed it to <c>&lt;requestId&gt;.claimed-&lt;processId&gt;</c> and is working on it.</summary>
    Claimed,

    /// <summary>A result file exists and parses.</summary>
    Finished,

    /// <summary>Still unclaimed after <c>expiresUtc</c>.</summary>
    Expired,

    /// <summary>No file of the request is left (cancelled, or cleaned up).</summary>
    Gone,
}

/// <summary>
/// A request's state. <see cref="ClaimedByProcessId"/> is the claiming
/// Revit's process id while <see cref="RequestState.Claimed"/> (taken from the
/// claimed file's name, the one place the protocol puts it); <see cref="Result"/>
/// is set when <see cref="RequestState.Finished"/>.
/// </summary>
public sealed record RequestStatus(RequestState State, int? ClaimedByProcessId = null, HandlerResult? Result = null);

/// <summary>What <see cref="RevitRequestQueue.Cancel"/> achieved.</summary>
public enum RequestCancellation
{
    /// <summary>The request file was deleted before any handler claimed it.</summary>
    Cancelled,

    /// <summary>A handler had already claimed it (or finished it); it cannot be cancelled and its result is left for clean-up.</summary>
    AlreadyClaimed,

    /// <summary>There was nothing to cancel: no file of the request exists.</summary>
    Gone,
}

/// <summary>A new request, or why none was written.</summary>
public sealed record RequestCreation(HandlerRequest? Request, string? Problem)
{
    public bool Succeeded => Request is not null;
}

/// <summary>
/// QuickerPlaces' side of the request protocol (docs/revit-handler-protocol.md,
/// "Requests", "Cancelling" and "Results"): writes a request into its
/// handler's folder, reports how it is getting on, cancels it, waits for its
/// result and cleans up after it. Handling a request is the handler's job.
///
/// Time comes from a <see cref="TimeProvider"/> and the pause between polls
/// from an injectable delay, so expiry and waiting are testable without
/// sleeping. UI-free and linked into the test project and the qp CLI.
/// </summary>
public sealed class RevitRequestQueue
{
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(500);

    private readonly RevitProtocolFolder _folder;
    private readonly TimeProvider _time;
    private readonly TimeSpan _lifetime;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public RevitRequestQueue(RevitProtocolFolder folder, TimeProvider? time = null, TimeSpan? lifetime = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _folder = folder;
        _time = time ?? TimeProvider.System;
        _lifetime = lifetime ?? HandlerProtocol.DefaultRequestLifetime;
        _delay = delay ?? ((interval, token) => Task.Delay(interval, token));
    }

    /// <summary>
    /// Writes an <c>open-new-local</c> request atomically to
    /// <c>requests\&lt;release&gt;\&lt;handlerId&gt;\&lt;requestId&gt;.json</c>. Does not
    /// throw: bad arguments or a failed write come back as <see cref="RequestCreation.Problem"/>.
    /// </summary>
    public RequestCreation CreateOpenNewLocal(string release, string handlerId, string centralPath, string localFolder, string worksets)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var request = new HandlerRequest
        {
            Protocol = HandlerProtocol.Version,
            RequestId = Guid.NewGuid().ToString("N"),
            Action = HandlerProtocol.ActionOpenNewLocal,
            HandlerId = handlerId,
            RevitRelease = release,
            CreatedUtc = now,
            ExpiresUtc = now + _lifetime,
            CentralPath = centralPath,
            LocalFolder = localFolder,
            Worksets = worksets,
        };

        if (request.Problem() is { } problem)
            return new RequestCreation(null, problem);

        try
        {
            RevitProtocolFolder.WriteAtomic(_folder.RequestFile(release, handlerId, request.RequestId), HandlerJson.ToUtf8(request));
            return new RequestCreation(request, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new RequestCreation(null, "The request could not be written: " + ex.Message);
        }
    }

    /// <summary>
    /// Where the request is now. A result that parses wins, then a claim,
    /// then the waiting file (Expired once past <c>expiresUtc</c>); with none
    /// of those the request is Gone. A result file that does not parse is
    /// ignored, as the protocol says readers ignore what they cannot read.
    /// </summary>
    public RequestStatus GetStatus(HandlerRequest request)
    {
        var folder = _folder.RequestFolder(request.RevitRelease, request.HandlerId);

        // Probe in the order the files come and go: <id>.json, then
        // <id>.claimed-<pid>, then <id>.result.json. Each is still there until
        // the next exists, so a handler moving the request along while this
        // runs can hide it from at most the probe it has just passed, never
        // from all of them. Probing the other way round could miss every file
        // and report Gone for a request Revit is working on.
        var requestFile = _folder.RequestFile(request.RevitRelease, request.HandlerId, request.RequestId);
        var waitingExisted = File.Exists(requestFile);
        AfterProbe?.Invoke(ProbeStep.RequestFile);

        var claims = FindClaims(folder, request.RequestId);
        AfterProbe?.Invoke(ProbeStep.Claims);

        var resultPath = _folder.ResultFile(request.RevitRelease, request.HandlerId, request.RequestId);
        var result = File.Exists(resultPath) ? HandlerJson.ReadFile<HandlerResult>(resultPath).Value : null;

        if (result is not null)
            return new RequestStatus(RequestState.Finished, result.ProcessId > 0 ? result.ProcessId : null, result);

        if (claims.Count > 0)
            return new RequestStatus(RequestState.Claimed, claims[0] > 0 ? claims[0] : null);

        if (waitingExisted)
        {
            return _time.GetUtcNow().UtcDateTime > request.ExpiresUtc
                ? new RequestStatus(RequestState.Expired)
                : new RequestStatus(RequestState.Waiting);
        }

        return new RequestStatus(RequestState.Gone);
    }

    internal enum ProbeStep { RequestFile, Claims }

    /// <summary>Test seam: called after each probe in <see cref="GetStatus"/>, so a test can play the handler moving the request along between probes.</summary>
    internal Action<ProbeStep>? AfterProbe { get; set; }

    /// <summary>
    /// Cancels a request by deleting its file. Honest about the race: if the
    /// file was already gone, a handler claimed it (or it finished) and this
    /// says <see cref="RequestCancellation.AlreadyClaimed"/>; only when no
    /// file of the request exists at all is it <see cref="RequestCancellation.Gone"/>.
    /// </summary>
    public RequestCancellation Cancel(HandlerRequest request)
    {
        var folder = _folder.RequestFolder(request.RevitRelease, request.HandlerId);
        var path = _folder.RequestFile(request.RevitRelease, request.HandlerId, request.RequestId);

        var existed = File.Exists(path);
        if (existed)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Still there, or a handler is renaming it this instant: judged below.
            }
        }

        // A claim that appeared meanwhile means the handler got there first, whatever the delete did.
        if (FindClaims(folder, request.RequestId).Count > 0 || File.Exists(_folder.ResultFile(request.RevitRelease, request.HandlerId, request.RequestId)))
            return RequestCancellation.AlreadyClaimed;

        return existed && !File.Exists(path) ? RequestCancellation.Cancelled : RequestCancellation.Gone;
    }

    /// <summary>
    /// Waits until the request is Finished, Expired or Gone, polling every
    /// <paramref name="pollInterval"/> (default 500 ms), and returns that
    /// final status. <paramref name="progress"/> gets the first status and
    /// each change after it. When the request expires unclaimed its file is
    /// deleted first; when it finishes, the result and claimed files are
    /// deleted after being read. Cancelling the token throws
    /// <see cref="OperationCanceledException"/> and leaves the request as it
    /// is, for the caller to <see cref="Cancel"/> or keep waiting on. A claimed
    /// request whose Revit dies is never finished; that wait ends only by the token.
    /// </summary>
    public async Task<RequestStatus> WaitAsync(HandlerRequest request, IProgress<RequestStatus>? progress = null,
        TimeSpan? pollInterval = null, CancellationToken cancellationToken = default)
    {
        var interval = pollInterval ?? DefaultPollInterval;
        RequestStatus? last = null;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var status = GetStatus(request);
            if (status != last)
            {
                progress?.Report(status);
                last = status;
            }

            switch (status.State)
            {
                case RequestState.Finished:
                    DeleteResultAndClaims(request);
                    return status;

                case RequestState.Gone:
                    return status;

                case RequestState.Expired:
                    // A handler may claim it between the check and the delete; then it is not ours to remove.
                    if (Cancel(request) == RequestCancellation.Cancelled)
                        return status;
                    break;
            }

            await _delay(interval, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Deletes result and claimed files last written more than a day ago, in
    /// every handler's folder; returns how many. For files nobody is waiting
    /// for any more: a request still being waited on is never that old.
    /// </summary>
    public int CleanUp()
    {
        var cutoff = _time.GetUtcNow().UtcDateTime - HandlerProtocol.StaleRequestFileAge;
        var deleted = 0;
        try
        {
            foreach (var path in Directory.EnumerateFiles(_folder.RequestsRoot, "*", SearchOption.AllDirectories))
            {
                var name = Path.GetFileName(path);
                if (RevitProtocolFolder.IsIgnorable(name) || !IsResultOrClaim(name))
                    continue;

                try
                {
                    if (File.GetLastWriteTimeUtc(path) >= cutoff)
                        continue;
                    File.Delete(path);
                    deleted++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // In use: next time.
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // No requests folder yet, or it cannot be listed: nothing to clean.
        }
        return deleted;
    }

    private static bool IsResultOrClaim(string name) =>
        name.EndsWith(".result.json", StringComparison.Ordinal) || name.Contains(".claimed-", StringComparison.Ordinal);

    /// <summary>Process ids of the request's <c>.claimed-&lt;pid&gt;</c> files; a claim whose id cannot be read counts as 0.</summary>
    private static List<int> FindClaims(string folder, string requestId)
    {
        var claims = new List<int>();
        try
        {
            var prefix = requestId + ".claimed-";
            foreach (var path in Directory.EnumerateFiles(folder, prefix + "*"))
            {
                var name = Path.GetFileName(path);
                if (RevitProtocolFolder.IsIgnorable(name))
                    continue;
                claims.Add(int.TryParse(name.AsSpan(prefix.Length), out var pid) ? pid : 0);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // No folder: no claims.
        }
        return claims;
    }

    private void DeleteResultAndClaims(HandlerRequest request)
    {
        var folder = _folder.RequestFolder(request.RevitRelease, request.HandlerId);
        TryDelete(_folder.ResultFile(request.RevitRelease, request.HandlerId, request.RequestId));
        try
        {
            foreach (var path in Directory.EnumerateFiles(folder, request.RequestId + ".claimed-*"))
                TryDelete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nothing to delete.
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left for CleanUp.
        }
    }
}
