using System;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace QuickerPlaces.Services;

/// <summary>
/// The app's <see cref="IBackgroundWork"/>: the work runs on the thread pool
/// and its result is applied through the UI thread's dispatcher. A failure
/// is logged and dropped, leaving the view as it was.
/// </summary>
public sealed class DispatcherBackgroundWork : IBackgroundWork
{
    private readonly Dispatcher _dispatcher;

    public DispatcherBackgroundWork(Dispatcher dispatcher) => _dispatcher = dispatcher;

    public void Run<T>(Func<T> work, Action<T> apply)
        => Task.Run(work).ContinueWith(task =>
        {
            if (task.IsFaulted)
            {
                DiagnosticLog.Error("A background query failed; the view keeps its last result.", task.Exception?.GetBaseException());
                return;
            }

            _dispatcher.BeginInvoke(() => apply(task.Result));
        }, TaskScheduler.Default);
}
