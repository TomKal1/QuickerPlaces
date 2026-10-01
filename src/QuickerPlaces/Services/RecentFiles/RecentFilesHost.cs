using System;
using System.Threading;
using QuickerPlaces.Services.Activity;
using QuickerPlaces.Services.Documents;

namespace QuickerPlaces.Services.RecentFiles;

/// <summary>
/// Runs Recent Files tracking while QuickerPlaces is open (documents plan
/// §5): once a minute, while tracking is on and not paused, it reads the
/// Recent Items entries written since tracking was last turned on and hands
/// them to <see cref="RecentFilesStore.Record"/>, scoped to the folders
/// tracked in Recents unless the user chose everywhere. It flushes every
/// five minutes and on exit.
///
/// It reads one documented folder of shortcuts, resolving only those that
/// changed (<see cref="WindowsRecentItems"/> caches the rest), and watches
/// nothing: no file system watcher, no hooks, nothing inside any program.
/// The tray's Pause tracking pauses this too. Logs counts only.
///
/// App-only: it calls Windows.
/// </summary>
public sealed class RecentFilesHost : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan FlushInterval = TimeSpan.FromMinutes(5);

    /// <summary>At most this many Recent Items shortcuts are read per pass, newest first.</summary>
    private const int MaxShortcuts = 400;

    private readonly RecentFilesStore _store;
    private readonly ActivityStore _activityStore;
    private readonly WindowsRecentItems _recentItems;
    private readonly Func<bool> _isPaused;
    private readonly object _sync = new();
    private Timer? _timer;
    private DateTime _lastFlushUtc = DateTime.UtcNow;
    private bool _disposed;

    public RecentFilesHost(RecentFilesStore store, ActivityStore activityStore, WindowsRecentItems recentItems, Func<bool> isPaused)
    {
        _store = store;
        _activityStore = activityStore;
        _recentItems = recentItems;
        _isPaused = isPaused;
    }

    public void Start()
    {
        lock (_sync)
        {
            if (_disposed || _timer is not null || !_store.IsAvailable)
                return;
            _timer = new Timer(_ => Tick(), null, TimeSpan.FromSeconds(20), Interval);
        }
    }

    /// <summary>Raised on the timer's thread after a pass recorded new opens, with how many: views showing Recent Files refresh on it.</summary>
    public event Action<int>? Recorded;

    /// <summary>Reads Recent Items now, as when the Library window opens, so what was just opened shows at once.</summary>
    public void RecordNow() => Tick();

    private void Tick()
    {
        // One pass at a time: a slow pass is skipped over, not queued behind.
        if (!Monitor.TryEnter(_sync))
            return;

        try
        {
            if (_disposed || !_store.IsTracking || _isPaused())
                return;

            var settings = _store.Settings;
            if (settings.ResumedAt is not { } resumedAt)
                return;

            var observations = _recentItems.Read(resumedAt, MaxShortcuts);
            var roots = _activityStore.EnabledRoots();
            var added = _store.Record(observations, path => RecentFilesStore.IsInScope(path, settings.Scope, roots));
            if (added > 0)
            {
                DiagnosticLog.Info($"Recent Files recorded {added} open(s).");
                Recorded?.Invoke(added);
            }

            if (DateTime.UtcNow - _lastFlushUtc >= FlushInterval)
            {
                _store.Flush();
                _lastFlushUtc = DateTime.UtcNow;
            }
        }
        catch (Exception ex)
        {
            // The next pass tries again; nothing is lost that Recent Items still holds.
            DiagnosticLog.Warn($"A Recent Files pass failed ({ex.GetType().Name}).");
        }
        finally
        {
            Monitor.Exit(_sync);
        }
    }

    /// <summary>Stops the timer and writes what is buffered.</summary>
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;
            _disposed = true;
            _timer?.Dispose();
            _timer = null;
        }

        _store.Flush();
    }
}
