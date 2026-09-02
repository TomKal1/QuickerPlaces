using System;
using System.Threading;

namespace QuickerPlaces.Services;

/// <summary>
/// Enforces one running QuickerPlaces instance per Windows user per
/// machine (plan 5.6). Wraps a named <see cref="Mutex"/> that the first
/// launch holds for its whole lifetime, and a named
/// <see cref="EventWaitHandle"/> a second launch signals instead of
/// starting up — the second launch never gets far enough to construct a
/// PlacesService, so the mutex alone is what guarantees only one writer
/// ever touches places.json.
///
/// Both names live under the "Local\" kernel-object namespace, not
/// "Global\": "Global\" objects are visible to every session on the
/// machine, including other Windows users' sessions in a multi-user
/// setup (fast user switching, a shared workstation, RDP). Each Windows
/// user already has their own AppData and therefore their own
/// places.json, so each should independently be allowed to run their own
/// instance — "Local\" scopes the mutex to the current session, exactly
/// matching that.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\QuickerPlaces.SingleInstance";
    private const string ActivateEventName = @"Local\QuickerPlaces.Activate";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activateEvent;
    private RegisteredWaitHandle? _registeredWait;
    private bool _disposed;

    private SingleInstance(Mutex mutex, EventWaitHandle activateEvent)
    {
        _mutex = mutex;
        _activateEvent = activateEvent;
    }

    /// <summary>
    /// Tries to become the one running instance. Returns a SingleInstance
    /// holding the acquired mutex on success (the caller owns disposing
    /// it), or null if another instance already holds it — in which case
    /// this has already signalled the activation event, so the caller's
    /// only remaining job is to shut down without touching the store.
    /// </summary>
    public static SingleInstance? TryAcquire()
    {
        // The activation event is created (or opened, if the first
        // instance already created it) unconditionally — whichever
        // instance ends up owning the mutex is also the one whose wait on
        // this event matters; the other, if any, only ever sets it.
        var activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);

        var mutex = new Mutex(initiallyOwned: false, MutexName, out _);

        bool acquired;
        try
        {
            // TimeSpan.Zero: this is a poll, not a wait — if another
            // instance holds the mutex, this one loses and must signal +
            // exit immediately (plan 5.6), not block the whole process
            // start-up queue behind someone else's session.
            acquired = mutex.WaitOne(TimeSpan.Zero);
        }
        catch (AbandonedMutexException)
        {
            // A previous instance crashed while holding the mutex without
            // releasing it. That is exactly what "abandoned" means here —
            // the process that owned it is gone, not that some other
            // instance is still legitimately running — so this counts as
            // a successful acquisition, not a failure. WaitOne still
            // grants ownership to the caller when it throws this.
            acquired = true;
        }

        if (!acquired)
        {
            DiagnosticLog.Info($"{AppInfo.Name} is already running; signalling it and exiting without touching the store.");
            activateEvent.Set();
            mutex.Dispose();
            activateEvent.Dispose();
            return null;
        }

        return new SingleInstance(mutex, activateEvent);
    }

    /// <summary>
    /// Registers <paramref name="onActivated"/> to run on a thread-pool
    /// thread whenever a second launch signals the activation event.
    /// Implemented with RegisterWaitForSingleObject rather than a
    /// dedicated blocking thread — the callback fires however many times
    /// the event is signalled (AutoReset re-arms it after each) for as
    /// long as this SingleInstance is alive, with no thread to manage by
    /// hand. The callback itself must marshal to the UI thread (it runs
    /// on a pool thread) before touching any WPF object — App.xaml.cs's
    /// handler does that via Dispatcher.
    /// </summary>
    public void RegisterActivationHandler(Action onActivated)
    {
        _registeredWait = ThreadPool.RegisterWaitForSingleObject(
            _activateEvent,
            (_, _) => RunActivationHandler(onActivated),
            state: null,
            timeout: Timeout.InfiniteTimeSpan,
            executeOnlyOnce: false);
    }

    /// <summary>
    /// Runs the activation callback, swallowing anything it throws. This
    /// is not defensive habit: the callback runs on a thread-pool thread,
    /// where an unhandled exception terminates the process outright rather
    /// than surfacing anywhere a user or a catch block could see it. The
    /// realistic case is a narrow shutdown race — a second launch signals
    /// the event just as this instance is closing, and the handler's
    /// Dispatcher call finds a dispatcher that has already shut down.
    /// Losing a window activation is a trivial failure; killing the
    /// running instance, and with it any unsaved change the banner is
    /// still offering to retry, is not.
    /// </summary>
    private static void RunActivationHandler(Action onActivated)
    {
        try
        {
            onActivated();
        }
        catch (Exception ex)
        {
            DiagnosticLog.Warn($"Single-instance activation handler failed: {ex.Message}");
        }
    }

    public void Dispose()
    {
        // Idempotent: App.xaml.cs has more than one shutdown path (the
        // early exit from the recovery prompt, and the normal window
        // close), and a second ReleaseMutex on an already-released mutex
        // throws.
        if (_disposed)
            return;

        _disposed = true;

        // Unregister before releasing the mutex so no queued callback can
        // fire against a SingleInstance that's mid-teardown.
        _registeredWait?.Unregister(null);
        _activateEvent.Dispose();

        try
        {
            // ReleaseMutex throws ApplicationException unless it is called
            // by the same thread that acquired ownership. That holds today
            // — TryAcquire and every Dispose call site run on the WPF UI
            // thread — but this runs from a window's Closing handler, and
            // an exception raised there would crash the application on the
            // way out, turning a tidy-up detail into the last thing the
            // user sees. The mutex is released by process exit regardless,
            // so there is nothing to gain by letting it propagate.
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException ex)
        {
            DiagnosticLog.Warn($"Single-instance mutex could not be released cleanly: {ex.Message}");
        }

        _mutex.Dispose();
    }
}
