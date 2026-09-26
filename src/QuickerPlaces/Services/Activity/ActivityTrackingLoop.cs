using System;
using System.Collections.Generic;
using System.Linq;

namespace QuickerPlaces.Services.Activity;

/// <summary>
/// Every decision the tracking host makes (Phase 9 plan D2, D6, D10, D26):
/// when to tick the tracker into the store, when to flush, and how long to
/// sleep. ActivityTrackingHost only runs this on its thread: it calls
/// <see cref="Wake"/> with whatever signals arrived, then waits for
/// <see cref="NextWait"/> or the next signal. Pure, and linked into the
/// tests; used by one thread only.
///
/// - No sampling while locked, suspended, or with nothing to track: the
///   host waits for a signal with no timeout at all.
/// - Idle past every enabled root's timeout: one flush, no sampling, and a
///   presence check every 15 s. (Seeing input return with no timer at all
///   would take a system-wide input hook, which D38 declines.) On return
///   the gap is discarded.
/// - Otherwise a tick every <see cref="FolderActivityTracker.PollInterval"/>.
/// - A flush every 5 minutes, and on lock, suspend, idle and stop (D10).
/// - A probe failure (Explorer restarting, a pass timing out) is logged
///   once per kind and its gap discarded (D12).
/// </summary>
public sealed class ActivityTrackingLoop
{
    /// <summary>How often buffered activity is written while tracking (D10).</summary>
    public static readonly TimeSpan FlushInterval = TimeSpan.FromMinutes(5);

    /// <summary>How often presence is looked at while the user is away.</summary>
    public static readonly TimeSpan IdleCheckInterval = TimeSpan.FromSeconds(15);

    private readonly FolderActivityTracker _tracker;
    private readonly ActivityStore _store;
    private readonly IUserPresence _presence;
    private readonly IMonotonicClock _clock;
    private readonly HashSet<string> _loggedFailures = new(StringComparer.Ordinal);

    private IReadOnlyList<TrackedRootConfig> _roots = Array.Empty<TrackedRootConfig>();
    private TimeSpan _lastFlush;
    private bool _suspended;
    private bool _paused;
    private bool _idle;

    public ActivityTrackingLoop(FolderActivityTracker tracker, ActivityStore store, IUserPresence presence, IMonotonicClock clock)
    {
        _tracker = tracker;
        _store = store;
        _presence = presence;
        _clock = clock;
    }

    /// <summary>
    /// How long to sleep before the next <see cref="Wake"/>, or null to sleep
    /// until a signal arrives. Never past the next flush.
    /// </summary>
    public TimeSpan? NextWait
    {
        get
        {
            if (_roots.Count == 0 || _suspended || _paused || _presence.SessionLocked)
                return null;

            var untilFlush = FlushInterval - (_clock.Elapsed - _lastFlush);
            var wait = _idle ? IdleCheckInterval : _tracker.PollInterval;
            return untilFlush < wait ? (untilFlush > TimeSpan.Zero ? untilFlush : TimeSpan.Zero) : wait;
        }
    }

    /// <summary>How many times the loop has been woken: what the performance check counts while locked.</summary>
    public int Wakes { get; private set; }

    /// <summary>How many times the tracker has sampled the probe.</summary>
    public int Ticks { get; private set; }

    /// <summary>How many ticks failed because the probe threw.</summary>
    public int ProbeFailures { get; private set; }

    /// <summary>Whether the host is waiting for the next idle-presence check.</summary>
    public bool IsIdle => _idle;

    /// <summary>The duration of the most recent tick, probe pass included, for the performance log.</summary>
    public TimeSpan LastTickDuration { get; private set; }

    /// <summary>Takes the store's enabled roots and starts the flush clock.</summary>
    public void Start()
    {
        LoadRoots();
        _lastFlush = _clock.Elapsed;
    }

    /// <summary>Handles <paramref name="signals"/>, then ticks and flushes as due.</summary>
    public void Wake(params TrackingSignal[] signals)
    {
        Wakes++;
        foreach (var signal in signals)
        {
            switch (signal)
            {
                case TrackingSignal.Locked:
                    _tracker.DiscardGap();
                    Flush();
                    break;

                case TrackingSignal.Suspending:
                    _suspended = true;
                    _tracker.DiscardGap();
                    Flush();
                    break;

                case TrackingSignal.Resumed:
                    _suspended = false;
                    _tracker.DiscardGap();
                    break;

                case TrackingSignal.Unlocked:
                    _tracker.DiscardGap();
                    break;

                case TrackingSignal.RootsChanged:
                    LoadRoots();
                    break;

                case TrackingSignal.Paused:
                    _paused = true;
                    _tracker.DiscardGap();
                    Flush();
                    break;

                case TrackingSignal.TrackingResumed:
                    _paused = false;
                    _tracker.DiscardGap();
                    break;
            }
        }

        if (_roots.Count == 0 || _suspended || _paused || _presence.SessionLocked)
            return;

        if (_presence.IdleFor >= _roots.Max(r => r.IdleTimeout))
        {
            if (!_idle)
            {
                _idle = true;
                Flush();
            }
        }
        else
        {
            if (_idle)
            {
                _idle = false;
                _tracker.DiscardGap();
            }

            Tick();
        }

        if (_clock.Elapsed - _lastFlush >= FlushInterval)
            Flush();
    }

    /// <summary>The final flush, when the app exits.</summary>
    public void Stop() => Flush();

    private void Tick()
    {
        var started = _clock.Elapsed;
        try
        {
            Ticks++;
            _store.Record(_tracker.Tick());
        }
        catch (Exception ex)
        {
            ProbeFailures++;
            _tracker.DiscardGap();

            // D12: the kind of failure only, once per kind per session, never a path.
            if (_loggedFailures.Add(ex.GetType().FullName ?? ex.GetType().Name))
                DiagnosticLog.Warn($"Folder activity probe failed ({ex.GetType().Name}: 0x{ex.HResult:X8}); tracking carries on, and further failures of this kind are not logged this session.");
        }
        finally
        {
            LastTickDuration = _clock.Elapsed - started;
        }
    }

    private void Flush()
    {
        _store.Flush();
        _lastFlush = _clock.Elapsed;
    }

    private void LoadRoots()
    {
        _roots = _store.EnabledRoots();
        _tracker.SetRoots(_roots);
    }
}
