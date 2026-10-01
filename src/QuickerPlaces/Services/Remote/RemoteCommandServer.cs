using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace QuickerPlaces.Services.Remote;

/// <summary>
/// The app's end of the pipe (RemoteProtocol): waits for qp, reads one
/// request, hands it to <c>handle</c> — which the app runs on its UI thread
/// against its own services — and writes the reply. One request at a time.
/// UI-free, so the tests drive it with a real pipe.
/// </summary>
public sealed class RemoteCommandServer : IDisposable
{
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(10);

    private readonly string _pipeName;
    private readonly Func<OperationRequest, OperationReply> _handle;
    private readonly CancellationTokenSource _stop = new();
    private Task? _loop;

    public RemoteCommandServer(string pipeName, Func<OperationRequest, OperationReply> handle)
    {
        _pipeName = pipeName;
        _handle = handle;
    }

    public void Start() => _loop ??= Task.Run(() => RunAsync(_stop.Token));

    private async Task RunAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(stop).ConfigureAwait(false);
                await ServeAsync(pipe, stop).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // A client that hung up or sent junk costs that request only.
                // Logged by type, never content (DiagnosticLog's privacy rule).
                DiagnosticLog.Warn($"A qp request failed ({ex.GetType().Name}).");
                try
                {
                    await Task.Delay(100, stop).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private async Task ServeAsync(NamedPipeServerStream pipe, CancellationToken stop)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop);
        timeout.CancelAfter(ReadTimeout);

        var line = await RemoteProtocol.ReadLineAsync(pipe, timeout.Token).ConfigureAwait(false);
        if (line is null)
            return;

        OperationReply reply;
        try
        {
            reply = _handle(OperationRequest.FromJson(line));
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException)
        {
            reply = OperationReply.Fail(OperationErrors.Usage, $"Bad request: {ex.Message}");
        }

        RemoteProtocol.WriteLine(pipe, reply.ToJson());

        // On Windows, let qp read the reply before the pipe is closed under it.
        if (OperatingSystem.IsWindows())
            pipe.WaitForPipeDrain();
    }

    public void Dispose()
    {
        _stop.Cancel();
        try
        {
            _loop?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // Already stopping; nothing to report.
        }
        _stop.Dispose();
    }
}

/// <summary>qp's end of the pipe.</summary>
public static class RemoteCommandClient
{
    private static readonly TimeSpan ReplyTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Sends <paramref name="request"/> to the app and waits for its reply.
    /// Throws TimeoutException when no app answers within <paramref name="timeout"/>
    /// (not running, or a build without the pipe), and IOException if it hangs up.
    /// </summary>
    public static OperationReply Send(string pipeName, OperationRequest request, TimeSpan timeout)
    {
        using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.CurrentUserOnly);
        pipe.Connect((int)timeout.TotalMilliseconds);

        RemoteProtocol.WriteLine(pipe, request.ToJson());

        // The app answers once its UI thread is free; a modal dialog doesn't
        // block that, but a long hang should still end in an error.
        using var wait = new CancellationTokenSource(ReplyTimeout);
        var reply = RemoteProtocol.ReadLineAsync(pipe, wait.Token).GetAwaiter().GetResult()
            ?? throw new IOException("QuickerPlaces closed the connection without answering.");
        return OperationReply.FromJson(reply);
    }
}
