using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Threading;

namespace QuickerPlaces.Services;

/// <summary>
/// Keeps QuickerPlaces to one running copy per Windows session. Two
/// copies would each hold their own in-memory place list and overwrite
/// each other's places.json, silently losing changes; and with a global
/// hotkey the app behaves like a launcher, where launching it again should
/// just bring it forward.
///
/// Uses one named, auto-reset event as both the lock and the doorbell:
/// whoever creates it is the primary instance and waits on it; a later
/// launch finds it already exists, signals it, and exits. Creating a named
/// kernel object is atomic, so two simultaneous launches can't both win.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    // "Local\" scopes the name to this logon session, so other users on
    // the same machine (fast user switching, RDP) each get their own copy.
    private const string EventName = @"Local\" + AppInfo.Publisher + "." + AppInfo.Name + ".ShowWindow";

    private const int AsfwAny = -1;

    private readonly EventWaitHandle _showRequested;
    private RegisteredWaitHandle? _registration;

    private SingleInstance(EventWaitHandle showRequested)
    {
        _showRequested = showRequested;
    }

    /// <summary>
    /// Returns the primary-instance guard, or null if another copy is
    /// already running, in which case that copy has been asked to show
    /// itself and the caller should exit.
    /// </summary>
    public static SingleInstance? TryStart()
    {
        var showRequested = new EventWaitHandle(false, EventResetMode.AutoReset, EventName, out var createdNew);
        if (createdNew)
            return new SingleInstance(showRequested);

        // This process was just launched by the user, so it may hand its
        // right to take the foreground over to the running copy. Without
        // that, Windows would only flash the running copy's taskbar button.
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
            (_, _) => dispatcher.BeginInvoke(onShowRequested),
            state: null,
            millisecondsTimeOutInterval: Timeout.Infinite,
            executeOnlyOnce: false);
    }

    public void Dispose()
    {
        _registration?.Unregister(null);
        _registration = null;
        _showRequested.Dispose();
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int dwProcessId);
}
