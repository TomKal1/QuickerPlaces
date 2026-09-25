using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Threading;

namespace QuickerPlaces.Services;

/// <summary>
/// Enforces one running QuickerPlaces instance per Windows user session
/// (plan 5.6). Two copies would each hold their own in-memory place list
/// and overwrite each other's places.json; and with a global hotkey the
/// app behaves like a launcher, where launching it again should just bring
/// it forward. A second launch never gets far enough to construct a
/// PlacesService, so it can never touch the store.
///
/// Uses one named, auto-reset event as both the lock and the doorbell:
/// whoever creates it is the primary instance and waits on it; a later
/// launch finds it already exists, signals it, and exits. Creating a named
/// kernel object is atomic, so two simultaneous launches can't both win,
/// and unlike a mutex there's no ownership to release or abandon: the
/// event disappears with the last handle, including after a crash.
///
/// The name lives under "Local\", not "Global\": each Windows user has
/// their own AppData and places.json, so each session (fast user
/// switching, RDP) is allowed its own instance.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const string EventName = @"Local\" + AppInfo.Publisher + "." + AppInfo.Name + ".ShowWindow";

    private const int AsfwAny = -1;

    private readonly EventWaitHandle _showRequested;
    private RegisteredWaitHandle? _registration;
    private bool _disposed;

    private SingleInstance(EventWaitHandle showRequested)
    {
        _showRequested = showRequested;
    }

    /// <summary>
    /// Returns the primary-instance guard, or null if another copy is
    /// already running, in which case that copy has been asked to show
    /// itself and the caller should exit without touching the store.
    /// </summary>
    public static SingleInstance? TryStart()
    {
        var showRequested = new EventWaitHandle(false, EventResetMode.AutoReset, EventName, out var createdNew);
        if (createdNew)
            return new SingleInstance(showRequested);

        DiagnosticLog.Info($"{AppInfo.Name} is already running; signalling it and exiting without touching the store.");

        // This process was just launched by the user, so it may hand its
        // right to take the foreground over to the running copy. Without
        // that, Windows would only flash the running copy's taskbar button
        // (the documented alternative to Topmost toggling, which Windows
        // may still refuse).
        AllowSetForegroundWindow(AsfwAny);
        showRequested.Set();
        showRequested.Dispose();
        return null;
    }

    /// <summary>Calls <paramref name="onShowRequested"/> on <paramref name="dispatcher"/>'s thread each time another launch asks this copy to show itself.</summary>
    public void ListenForShowRequests(Dispatcher dispatcher, Action onShowRequested)
    {
        _registration?.Unregister(null);
        _registration = ThreadPool.RegisterWaitForSingleObject(
            _showRequested,
            (_, _) => RunShowRequest(dispatcher, onShowRequested),
            state: null,
            millisecondsTimeOutInterval: Timeout.Infinite,
            executeOnlyOnce: false);
    }

    /// <summary>
    /// Runs on a thread-pool thread, where an unhandled exception would
    /// terminate the process outright — taking any unsaved change the
    /// banner is still offering to retry with it. Losing one window
    /// activation (say, a signal racing this instance's shutdown) is a
    /// trivial failure by comparison, so anything thrown is logged and
    /// swallowed.
    /// </summary>
    private static void RunShowRequest(Dispatcher dispatcher, Action onShowRequested)
    {
        try
        {
            dispatcher.BeginInvoke(() =>
            {
                DiagnosticLog.Info($"{AppInfo.Name} activated by a second launch attempt.");
                onShowRequested();
            });
        }
        catch (Exception ex)
        {
            DiagnosticLog.Warn($"Single-instance activation handler failed: {ex.Message}");
        }
    }

    public void Dispose()
    {
        // Idempotent: App.xaml.cs has more than one shutdown path (the
        // early exit from the recovery prompt, and the normal exit).
        if (_disposed)
            return;

        _disposed = true;
        _registration?.Unregister(null);
        _registration = null;
        _showRequested.Dispose();
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int dwProcessId);
}
