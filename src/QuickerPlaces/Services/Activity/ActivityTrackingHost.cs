using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace QuickerPlaces.Services.Activity;

/// <summary>Runs the tracking decisions and store flushes off the UI thread.</summary>
public sealed class ActivityTrackingHost : IDisposable
{
    private readonly object _sync = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly Queue<TrackingSignal> _signals = new();
    private readonly ActivityStore _store;
    private readonly ActivityTrackingLoop _loop;
    private readonly UserPresence _presence;
    private readonly EventShellWindowProbe _probe;
    private Thread? _thread;
    private bool _stopping;
    private bool _disposed;
    private bool _suspended;
    private int _manuallyPaused;

    public ActivityTrackingHost(ActivityStore store)
    {
        _store = store;
        _presence = new UserPresence();
        _probe = new EventShellWindowProbe();
        var clock = new StopwatchMonotonicClock();
        var tracker = new FolderActivityTracker(_probe, _presence, clock, TimeProvider.System);
        _loop = new ActivityTrackingLoop(tracker, store, _presence, clock);
        IsAvailable = store.IsAvailable;
        _presence.Signal += Signal;
    }

    /// <summary>Constructs the host's store in machine-local activity.json.</summary>
    public static ActivityStore CreateStore()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "QuickerPlaces", "QuickerPlaces");
        return new ActivityStore(new FilePlacesStorage(directory, "activity.json"), TimeProvider.System);
    }

    public bool IsAvailable { get; }
    public bool IsPaused => Volatile.Read(ref _manuallyPaused) != 0;

    /// <summary>Delivered on the worker after a wake; intended for the developer probe.</summary>
    public event Action<ActivityTrackingLoop>? WakeCompleted;

    /// <summary>Delivered when the operating system supplies a presence or power signal.</summary>
    public event Action<TrackingSignal>? SignalReceived;

    /// <summary>Delivered on the worker after a signal has been applied to the loop.</summary>
    public event Action<TrackingSignal, ActivityTrackingLoop>? SignalHandled;

    public void Start()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_thread is not null || !IsAvailable) return;
            _thread = new Thread(Run) { IsBackground = true, Name = "Folder activity host" };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
        }
    }

    public void RootsChanged() => Signal(TrackingSignal.RootsChanged);

    public void SetTrackingPaused(bool paused)
    {
        if (Interlocked.Exchange(ref _manuallyPaused, paused ? 1 : 0) == (paused ? 1 : 0)) return;
        Signal(paused ? TrackingSignal.Paused : TrackingSignal.TrackingResumed);
    }

    private void Signal(TrackingSignal signal)
    {
        lock (_sync)
        {
            if (_stopping || _disposed) return;
            if (signal == TrackingSignal.Suspending) _suspended = true;
            if (signal == TrackingSignal.Resumed) _suspended = false;
            _probe.SetPaused(_presence.SessionLocked || _suspended || IsPaused || _store.EnabledRoots().Count == 0);
            _signals.Enqueue(signal);
            _wake.Set();
        }
        SignalReceived?.Invoke(signal);
    }

    private void Run()
    {
        try
        {
            _loop.Start();
            while (true)
            {
                TrackingSignal[] signals;
                lock (_sync)
                {
                    if (_stopping) break;
                    signals = _signals.ToArray();
                    _signals.Clear();
                }
                _loop.Wake(signals);
                foreach (var signal in signals)
                    SignalHandled?.Invoke(signal, _loop);
                WakeCompleted?.Invoke(_loop);
                var wait = _loop.NextWait;
                _wake.WaitOne(wait ?? Timeout.InfiniteTimeSpan);
            }
        }
        catch (Exception ex)
        {
            // No exception message: a COM exception can contain a folder path.
            DiagnosticLog.Warn($"Folder activity host stopped after {ex.GetType().Name} (0x{ex.HResult:X8}).");
        }
        finally
        {
            try { _loop.Stop(); }
            catch (Exception ex)
            {
                DiagnosticLog.Warn($"Folder activity final flush failed with {ex.GetType().Name} (0x{ex.HResult:X8}).");
            }
            _presence.Signal -= Signal;
            _presence.Dispose();
            _probe.Dispose();
        }
    }

    /// <summary>Requests final flush and waits at most two seconds for shutdown.</summary>
    public void Stop()
    {
        Thread? thread;
        lock (_sync)
        {
            _stopping = true;
            thread = _thread;
            _wake.Set();
        }
        if (thread is not null && !thread.Join(TimeSpan.FromSeconds(2)))
            DiagnosticLog.Warn("Folder activity host did not stop within two seconds; final flush may be delayed.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        _disposed = true;
        if (_thread is null)
        {
            _presence.Signal -= Signal;
            _presence.Dispose();
            _probe.Dispose();
        }
    }
}
