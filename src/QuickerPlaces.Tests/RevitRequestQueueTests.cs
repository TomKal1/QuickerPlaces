using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using QuickerPlaces.Services.Revit.Handlers;
using QuickerPlaces.Tests.Fakes;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>QuickerPlaces' side of the request protocol, with the handler's part played by hand.</summary>
public sealed class RevitRequestQueueTests : IDisposable
{
    private const string Release = "2025";
    private const string Handler = "contoso-tools";

    private readonly TempDirectory _dir = new();
    private readonly RevitProtocolFolder _folder;
    private readonly ManualTimeProvider _time = new(new DateTimeOffset(2026, 10, 8, 21, 14, 0, TimeSpan.Zero));
    private readonly RevitRequestQueue _queue;
    private int _pauses;

    public RevitRequestQueueTests()
    {
        _folder = new RevitProtocolFolder(_dir.Path);
        _queue = NewQueue();
    }

    public void Dispose() => _dir.Dispose();

    private RevitRequestQueue NewQueue(TimeSpan? lifetime = null, Func<TimeSpan, CancellationToken, Task>? delay = null) =>
        new(_folder, _time, lifetime, delay ?? ((interval, token) =>
        {
            // A pause that costs nothing: time just moves on.
            _pauses++;
            token.ThrowIfCancellationRequested();
            _time.Advance(interval);
            return Task.CompletedTask;
        }));

    private HandlerRequest NewRequest()
    {
        var creation = _queue.CreateOpenNewLocal(Release, Handler, @"\\server\p\Central.rvt", @"C:\REVIT_LOCAL2025", HandlerProtocol.Worksets.LastViewed);
        return Assert.IsType<HandlerRequest>(creation.Request);
    }

    /// <summary>What a handler's claim does: renames the request file.</summary>
    private void Claim(HandlerRequest request, int pid) =>
        File.Move(_folder.RequestFile(Release, Handler, request.RequestId), _folder.ClaimedFile(Release, Handler, request.RequestId, pid));

    private void Finish(HandlerRequest request, int pid, bool ok = true, string? errorCode = null)
    {
        var result = new HandlerResult
        {
            Protocol = 1, RequestId = request.RequestId, HandlerId = Handler, RevitRelease = Release, ProcessId = pid,
            FinishedUtc = _time.GetUtcNow().UtcDateTime, Ok = ok, LocalPath = ok ? @"C:\REVIT_LOCAL2025\Central_me.rvt" : null,
            ErrorCode = errorCode, Message = ok ? null : "Failed.",
        };
        RevitProtocolFolder.WriteAtomic(_folder.ResultFile(Release, Handler, request.RequestId), HandlerJson.ToUtf8(result));
    }

    [Fact]
    public void Create_WritesTheRequestIntoItsHandlersFolder()
    {
        var request = NewRequest();

        var expected = Path.Combine(_dir.Path, "requests", Release, Handler, request.RequestId + ".json");
        Assert.True(File.Exists(expected));
        Assert.Equal([expected], Directory.GetFileSystemEntries(Path.GetDirectoryName(expected)!));
        var written = HandlerJson.ReadFile<HandlerRequest>(expected).Value;
        Assert.Equal(request, written);
    }

    [Fact]
    public void Create_FillsTheRequestIn()
    {
        var request = NewRequest();

        Assert.Equal(1, request.Protocol);
        Assert.True(HandlerProtocol.IsValidRequestId(request.RequestId));
        Assert.Equal("open-new-local", request.Action);
        Assert.Equal(Handler, request.HandlerId);
        Assert.Equal(Release, request.RevitRelease);
        Assert.Equal(_time.GetUtcNow().UtcDateTime, request.CreatedUtc);
        Assert.Equal(request.CreatedUtc.AddMinutes(10), request.ExpiresUtc);
        Assert.Equal(DateTimeKind.Utc, request.ExpiresUtc.Kind);
    }

    [Fact]
    public void Create_ExpiryIsConfigurable_AndIdsAreUnique()
    {
        var queue = NewQueue(lifetime: TimeSpan.FromSeconds(30));

        var a = queue.CreateOpenNewLocal(Release, Handler, "c", "l", "all").Request!;
        var b = queue.CreateOpenNewLocal(Release, Handler, "c", "l", "all").Request!;

        Assert.Equal(a.CreatedUtc.AddSeconds(30), a.ExpiresUtc);
        Assert.NotEqual(a.RequestId, b.RequestId);
    }

    [Theory]
    [InlineData("2025", "Bad Id", "c", "l", "all")]
    [InlineData("25", "ok", "c", "l", "all")]
    [InlineData("2025", "ok", "", "l", "all")]
    [InlineData("2025", "ok", "c", " ", "all")]
    [InlineData("2025", "ok", "c", "l", "sometimes")]
    public void Create_WithBadArguments_FailsWithAReasonAndWritesNothing(string release, string handler, string central, string local, string worksets)
    {
        var creation = _queue.CreateOpenNewLocal(release, handler, central, local, worksets);

        Assert.False(creation.Succeeded);
        Assert.NotEmpty(creation.Problem!);
        Assert.False(Directory.Exists(Path.Combine(_dir.Path, "requests")));
    }

    [Fact]
    public void Create_WhenTheFolderCannotBeMade_FailsWithAReason()
    {
        File.WriteAllText(Path.Combine(_dir.Path, "requests"), "a file where the folder should go");

        var creation = _queue.CreateOpenNewLocal(Release, Handler, "c", "l", "all");

        Assert.False(creation.Succeeded);
        Assert.NotEmpty(creation.Problem!);
    }

    [Fact]
    public void Status_GoesWaiting_Claimed_Finished()
    {
        var request = NewRequest();
        Assert.Equal(new RequestStatus(RequestState.Waiting), _queue.GetStatus(request));

        Claim(request, 4242);
        Assert.Equal(new RequestStatus(RequestState.Claimed, 4242), _queue.GetStatus(request));

        Finish(request, 4242);
        var status = _queue.GetStatus(request);
        Assert.Equal(RequestState.Finished, status.State);
        Assert.True(status.Result!.Ok);
        Assert.Equal(@"C:\REVIT_LOCAL2025\Central_me.rvt", status.Result.LocalPath);
    }

    [Fact]
    public void Status_NeverSaysGone_WhenAHandlerMovesTheRequestBetweenProbes()
    {
        // The handler claims right after the request file was probed.
        var claimed = NewRequest();
        _queue.AfterProbe = step =>
        {
            if (step == RevitRequestQueue.ProbeStep.RequestFile)
                Claim(claimed, 11);
        };
        Assert.Equal(new RequestStatus(RequestState.Claimed, 11), _queue.GetStatus(claimed));

        // It claims, finishes and deletes its claim right after the request file was probed.
        var finished = NewRequest();
        _queue.AfterProbe = step =>
        {
            if (step != RevitRequestQueue.ProbeStep.RequestFile)
                return;
            Claim(finished, 12);
            Finish(finished, 12);
            File.Delete(_folder.ClaimedFile(Release, Handler, finished.RequestId, 12));
        };
        Assert.Equal(RequestState.Finished, _queue.GetStatus(finished).State);

        // It finishes and deletes its claim right after the claims were probed.
        var late = NewRequest();
        Claim(late, 13);
        _queue.AfterProbe = step =>
        {
            if (step != RevitRequestQueue.ProbeStep.Claims)
                return;
            Finish(late, 13);
            File.Delete(_folder.ClaimedFile(Release, Handler, late.RequestId, 13));
        };
        Assert.Equal(RequestState.Finished, _queue.GetStatus(late).State);
    }

    [Fact]
    public void Status_IsExpired_OnlyWhileUnclaimedAndPastExpiry()
    {
        var request = NewRequest();

        _time.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(RequestState.Waiting, _queue.GetStatus(request).State);

        _time.Advance(TimeSpan.FromTicks(1));
        Assert.Equal(RequestState.Expired, _queue.GetStatus(request).State);

        Claim(request, 9);
        Assert.Equal(RequestState.Claimed, _queue.GetStatus(request).State);
    }

    [Fact]
    public void Status_WithNothingOnDisk_IsGone()
    {
        Assert.Equal(RequestState.Gone, _queue.GetStatus(NewRequestThenDeleteFiles()).State);
    }

    private HandlerRequest NewRequestThenDeleteFiles()
    {
        var request = NewRequest();
        File.Delete(_folder.RequestFile(Release, Handler, request.RequestId));
        return request;
    }

    [Fact]
    public void Status_IgnoresAResultThatDoesNotParse_AndOtherRequestsFiles()
    {
        var request = NewRequest();
        File.WriteAllText(_folder.ResultFile(Release, Handler, request.RequestId), "{ half");
        File.WriteAllText(Path.Combine(_folder.RequestFolder(Release, Handler), Guid.NewGuid().ToString("N") + ".claimed-1"), "");

        Assert.Equal(RequestState.Waiting, _queue.GetStatus(request).State);
    }

    [Fact]
    public void Status_ReadsFailedResults_WithTheirCodes()
    {
        var request = NewRequest();
        Claim(request, 5);
        Finish(request, 5, ok: false, errorCode: "releaseMismatch");

        var result = _queue.GetStatus(request).Result!;

        Assert.False(result.Ok);
        Assert.Equal("releaseMismatch", result.EffectiveErrorCode);
        Assert.Equal("Failed.", result.Message);
    }

    [Fact]
    public void Cancel_BeforeAClaim_DeletesTheRequest()
    {
        var request = NewRequest();

        Assert.Equal(RequestCancellation.Cancelled, _queue.Cancel(request));

        Assert.False(File.Exists(_folder.RequestFile(Release, Handler, request.RequestId)));
        Assert.Equal(RequestState.Gone, _queue.GetStatus(request).State);
    }

    [Fact]
    public void Cancel_AfterAClaim_SaysSo_AndLeavesTheClaim()
    {
        var request = NewRequest();
        Claim(request, 77);

        Assert.Equal(RequestCancellation.AlreadyClaimed, _queue.Cancel(request));

        Assert.True(File.Exists(_folder.ClaimedFile(Release, Handler, request.RequestId, 77)));
    }

    [Fact]
    public void Cancel_AfterAResult_SaysClaimed()
    {
        var request = NewRequest();
        Claim(request, 77);
        Finish(request, 77);

        Assert.Equal(RequestCancellation.AlreadyClaimed, _queue.Cancel(request));
    }

    [Fact]
    public void Cancel_WhenNothingIsLeft_SaysGone_AndTwiceDoesNotClaimSuccessTwice()
    {
        var request = NewRequest();

        Assert.Equal(RequestCancellation.Cancelled, _queue.Cancel(request));
        Assert.Equal(RequestCancellation.Gone, _queue.Cancel(request));
    }

    [Fact]
    public async Task Wait_SeesWaitingThenClaimedThenFinished_ReportsEachChange_AndCleansUp()
    {
        var request = NewRequest();
        var reported = new List<RequestStatus>();
        var step = 0;
        var queue = NewQueue(delay: (interval, token) =>
        {
            switch (++step)
            {
                case 1: break;                   // nothing changes: no report
                case 2: Claim(request, 31); break;
                case 3: break;
                case 4: Finish(request, 31); break;
            }
            _time.Advance(interval);
            return Task.CompletedTask;
        });

        var final = await queue.WaitAsync(request, new SyncProgress(reported.Add), TimeSpan.FromSeconds(1));

        Assert.Equal(RequestState.Finished, final.State);
        Assert.Equal(
            [RequestState.Waiting, RequestState.Claimed, RequestState.Finished],
            reported.Select(s => s.State));
        Assert.Equal(31, reported[1].ClaimedByProcessId);
        Assert.Empty(Directory.GetFileSystemEntries(_folder.RequestFolder(Release, Handler)));
    }

    [Fact]
    public async Task Wait_OnExpiryWhileUnclaimed_DeletesTheRequest_AndReturnsExpired()
    {
        var request = NewRequest();

        var final = await _queue.WaitAsync(request, null, TimeSpan.FromMinutes(1));

        Assert.Equal(RequestState.Expired, final.State);
        Assert.Empty(Directory.GetFileSystemEntries(_folder.RequestFolder(Release, Handler)));
        Assert.Equal(11, _pauses); // polls at 0..10 minutes see Waiting; the poll after the 11th minute sees Expired
    }

    [Fact]
    public async Task Wait_IfAHandlerClaimsAtTheLastMoment_KeepsWaitingForTheResult()
    {
        var request = NewRequest();
        _time.Advance(TimeSpan.FromMinutes(11));
        var step = 0;
        var queue = NewQueue(delay: (interval, token) =>
        {
            if (++step == 1)
                Finish(request, 8);
            return Task.CompletedTask;
        });
        Claim(request, 8);

        var final = await queue.WaitAsync(request, null, TimeSpan.FromSeconds(1));

        Assert.Equal(RequestState.Finished, final.State);
    }

    [Fact]
    public async Task Wait_WhenCancelledByTheCaller_ThrowsAndLeavesTheRequest()
    {
        var request = NewRequest();
        using var cts = new CancellationTokenSource();
        var queue = NewQueue(delay: (interval, token) =>
        {
            cts.Cancel();
            token.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queue.WaitAsync(request, null, TimeSpan.FromSeconds(1), cts.Token));

        Assert.Equal(RequestState.Waiting, _queue.GetStatus(request).State);
    }

    [Fact]
    public async Task Wait_WithAnAlreadyCancelledToken_ThrowsAtOnce()
    {
        var request = NewRequest();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _queue.WaitAsync(request, null, null, new CancellationToken(true)));
    }

    [Fact]
    public async Task Wait_ForARequestThatIsGone_ReturnsGone()
    {
        var request = NewRequestThenDeleteFiles();

        Assert.Equal(RequestState.Gone, (await _queue.WaitAsync(request)).State);
    }

    [Fact]
    public async Task Wait_UsesTheRealDelayByDefault()
    {
        var queue = new RevitRequestQueue(_folder);
        var request = queue.CreateOpenNewLocal(Release, Handler, "c", "l", "all").Request!;
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queue.WaitAsync(request, null, TimeSpan.FromMilliseconds(10), cts.Token));
    }

    [Fact]
    public void CleanUp_DeletesResultsAndClaimsOlderThanADay_AndNothingElse()
    {
        var oldRequest = NewRequest();
        Claim(oldRequest, 1);
        Finish(oldRequest, 1);
        var freshRequest = NewRequest();
        Claim(freshRequest, 2);
        Finish(freshRequest, 2);
        var waiting = NewRequest();
        var other = new RevitProtocolFolder(_dir.Path);
        var otherHandlerClaim = other.ClaimedFile("2024", "other", Guid.NewGuid().ToString("N"), 3);
        Directory.CreateDirectory(Path.GetDirectoryName(otherHandlerClaim)!);
        File.WriteAllText(otherHandlerClaim, "");

        var old = _time.GetUtcNow().UtcDateTime.AddHours(-25);
        File.SetLastWriteTimeUtc(_folder.ClaimedFile(Release, Handler, oldRequest.RequestId, 1), old);
        File.SetLastWriteTimeUtc(_folder.ResultFile(Release, Handler, oldRequest.RequestId), old);
        File.SetLastWriteTimeUtc(_folder.RequestFile(Release, Handler, waiting.RequestId), old);
        File.SetLastWriteTimeUtc(otherHandlerClaim, old);

        var deleted = _queue.CleanUp();

        Assert.Equal(3, deleted);
        Assert.False(File.Exists(_folder.ClaimedFile(Release, Handler, oldRequest.RequestId, 1)));
        Assert.False(File.Exists(_folder.ResultFile(Release, Handler, oldRequest.RequestId)));
        Assert.False(File.Exists(otherHandlerClaim));
        Assert.True(File.Exists(_folder.ClaimedFile(Release, Handler, freshRequest.RequestId, 2)));
        Assert.True(File.Exists(_folder.ResultFile(Release, Handler, freshRequest.RequestId)));
        Assert.True(File.Exists(_folder.RequestFile(Release, Handler, waiting.RequestId)));
    }

    [Fact]
    public void CleanUp_WithNoRequestsFolder_DeletesNothing()
    {
        Assert.Equal(0, _queue.CleanUp());
    }

    /// <summary>Progress that reports on the calling thread, so a test sees reports in order and before the call returns.</summary>
    private sealed class SyncProgress(Action<RequestStatus> report) : IProgress<RequestStatus>
    {
        public void Report(RequestStatus value) => report(value);
    }
}
