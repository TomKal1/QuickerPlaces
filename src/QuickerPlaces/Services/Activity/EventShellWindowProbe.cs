using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Threading;
using System.Windows.Threading;

namespace QuickerPlaces.Services.Activity;

/// <summary>
/// Explorer paths are cached by an STA receiving shell window and navigation
/// events. A regular sample reads that cache and the foreground HWND only.
/// A minute-spaced reconciliation recovers from missed events or Explorer
/// restarts; no COM call runs on the UI or tracking host thread.
/// </summary>
public sealed class EventShellWindowProbe : IShellWindowProbe, IDisposable
{
    private static readonly TimeSpan ComTimeout = TimeSpan.FromSeconds(2);
    private readonly object _sync = new();
    private Worker? _worker;
    private bool _disposed;

    public IReadOnlyList<ShellWindowSnapshot> Sample()
    {
        Worker worker;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            worker = _worker ??= new Worker();
        }

        try
        {
            var cached = worker.Read(ComTimeout);
            var foreground = GetForegroundWindow();
            var result = new ShellWindowSnapshot[cached.Length];
            for (var i = 0; i < result.Length; i++)
                result[i] = new ShellWindowSnapshot(cached[i].Path, cached[i].Hwnd,
                    cached[i].Hwnd == foreground);
            return result;
        }
        catch (TimeoutException)
        {
            // A hung COM worker is abandoned, not joined by the host.
            lock (_sync)
            {
                if (ReferenceEquals(_worker, worker))
                {
                    _worker = null;
                    worker.Dispose();
                }
            }
            throw;
        }
    }

    public int Reconciliations => _worker?.Reconciliations ?? 0;
    public TimeSpan ReconciliationTime => _worker?.ReconciliationTime ?? TimeSpan.Zero;
    public int WindowEvents => _worker?.WindowEvents ?? 0;
    public int NavigationEvents => _worker?.NavigationEvents ?? 0;

    /// <summary>Stops maintenance while locked, suspended or without roots.</summary>
    public void SetPaused(bool paused)
    {
        lock (_sync)
            _worker?.SetPaused(paused);
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _worker?.Dispose();
            _worker = null;
        }
    }

    private sealed record Entry(string Path, nint Hwnd);

    private sealed class Worker : IDisposable
    {
        private static readonly Guid ShellEventsId = new("FE4106E0-399A-11D0-A48C-00A0C90A8F39");
        private readonly ManualResetEventSlim _ready = new(false);
        private readonly Thread _thread;
        private readonly List<WindowSubscription> _subscriptions = new();
        private Entry[] _snapshot = Array.Empty<Entry>();
        private Dispatcher? _dispatcher;
        private DispatcherTimer? _timer;
        private object? _shell;
        private object? _windows;
        private IConnectionPoint? _shellConnection;
        private ShellWindowEventsSink? _shellSink;
        private int _shellCookie;
        private Exception? _error;
        private long _busySince;
        private long _reconciliationTicks;
        private int _reconciliations;
        private int _windowEvents;
        private int _navigationEvents;
        private int _paused;
        private int _stopping;

        public Worker()
        {
            _thread = new Thread(Run) { IsBackground = true, Name = "Explorer shell events" };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
        }

        public int Reconciliations => Volatile.Read(ref _reconciliations);
        public int WindowEvents => Volatile.Read(ref _windowEvents);
        public int NavigationEvents => Volatile.Read(ref _navigationEvents);
        public TimeSpan ReconciliationTime
            => TimeSpan.FromSeconds(Volatile.Read(ref _reconciliationTicks) / (double)Stopwatch.Frequency);

        public Entry[] Read(TimeSpan timeout)
        {
            if (!_ready.Wait(timeout))
                throw new TimeoutException("Explorer shell event setup timed out.");
            var busySince = Volatile.Read(ref _busySince);
            if (busySince != 0 && Stopwatch.GetElapsedTime(busySince) > timeout)
                throw new TimeoutException("Explorer shell reconciliation timed out.");
            if (Volatile.Read(ref _error) is { } error)
                throw error;
            return Volatile.Read(ref _snapshot);
        }

        private void Run()
        {
            try
            {
                _dispatcher = Dispatcher.CurrentDispatcher;
                SafeReconcile();
                _timer = new DispatcherTimer(DispatcherPriority.Background)
                {
                    Interval = _error is null ? TimeSpan.FromMinutes(1) : TimeSpan.FromSeconds(15)
                };
                _timer.Tick += (_, _) => SafeReconcile();
                if (Volatile.Read(ref _paused) == 0) _timer.Start();
                _ready.Set();
                if (Volatile.Read(ref _stopping) == 0)
                    Dispatcher.Run();
            }
            catch (Exception ex)
            {
                Volatile.Write(ref _error, ex);
                _ready.Set();
            }
            finally
            {
                _timer?.Stop();
                ClearCom();
                _ready.Set();
            }
        }

        private void SafeReconcile()
        {
            if (Volatile.Read(ref _stopping) != 0 || Volatile.Read(ref _paused) != 0) return;
            var started = Stopwatch.GetTimestamp();
            Volatile.Write(ref _busySince, started);
            try
            {
                EnsureShellEvents();
                Reconcile();
                Volatile.Write(ref _error, null);
                if (_timer is not null) _timer.Interval = TimeSpan.FromMinutes(1);
            }
            catch (Exception ex)
            {
                Volatile.Write(ref _error, ex);
                ClearCom(); // Never credit stale paths after a broken sink.
                if (_timer is not null) _timer.Interval = TimeSpan.FromSeconds(15);
            }
            finally
            {
                Interlocked.Increment(ref _reconciliations);
                Interlocked.Add(ref _reconciliationTicks, Stopwatch.GetTimestamp() - started);
                Volatile.Write(ref _busySince, 0);
            }
        }

        private void EnsureShellEvents()
        {
            if (_windows is not null) return;
            var shellType = Type.GetTypeFromProgID("Shell.Application", throwOnError: true)!;
            _shell = Activator.CreateInstance(shellType)!;
            _windows = ((dynamic)_shell).Windows();
            var source = (IConnectionPointContainer)_windows;
            var eventId = ShellEventsId;
            source.FindConnectionPoint(ref eventId, out _shellConnection);
            _shellSink = new ShellWindowEventsSink(ShellChanged);
            (_shellConnection ?? throw new InvalidOperationException("Shell event connection unavailable."))
                .Advise(_shellSink, out _shellCookie);
        }

        private void Reconcile()
        {
            var next = new List<WindowSubscription>();
            try
            {
                int count = (int)((dynamic)_windows!).Count;
                for (var i = 0; i < count; i++)
                {
                    object? window = null;
                    try
                    {
                        window = ((dynamic)_windows!).Item(i);
                        if (window is null) continue;
                        var subscription = WindowSubscription.TryCreate(window, this);
                        if (subscription is not null)
                        {
                            next.Add(subscription);
                            window = null; // Its COM reference belongs to the subscription.
                        }
                    }
                    catch (COMException ex) when (ex.HResult is unchecked((int)0x80010108) or unchecked((int)0x800401FD))
                    {
                        // Explorer revoked this entry during enumeration.
                    }
                    finally { Release(window); }
                }
            }
            catch
            {
                foreach (var item in next) item.Dispose();
                throw;
            }

            foreach (var item in _subscriptions) item.Dispose();
            _subscriptions.Clear();
            _subscriptions.AddRange(next);
            Publish();
        }

        private void Publish()
        {
            var entries = new Entry[_subscriptions.Count];
            for (var i = 0; i < entries.Length; i++)
                entries[i] = new Entry(_subscriptions[i].Path, _subscriptions[i].Hwnd);
            Volatile.Write(ref _snapshot, entries);
        }

        private void ShellChanged()
        {
            Interlocked.Increment(ref _windowEvents);
            // Avoid reentering COM while Explorer is delivering its event.
            if (Volatile.Read(ref _stopping) == 0 && Volatile.Read(ref _paused) == 0)
            {
                try { _dispatcher?.BeginInvoke(new Action(SafeReconcile)); }
                catch (InvalidOperationException) { /* The dispatcher already ended. */ }
            }
        }

        private void Navigated(WindowSubscription item)
        {
            Interlocked.Increment(ref _navigationEvents);
            item.ClearPath();
            Publish(); // Do not credit the old folder while the new one loads.
            var started = Stopwatch.GetTimestamp();
            Volatile.Write(ref _busySince, started);
            try
            {
                item.RefreshPath();
                Publish();
            }
            catch (Exception ex)
            {
                Volatile.Write(ref _error, ex);
                Volatile.Write(ref _snapshot, Array.Empty<Entry>());
                ShellChanged();
            }
            finally { Volatile.Write(ref _busySince, 0); }
        }

        private void ClearCom()
        {
            Volatile.Write(ref _snapshot, Array.Empty<Entry>());
            foreach (var item in _subscriptions) item.Dispose();
            _subscriptions.Clear();
            if (_shellConnection is not null)
            {
                try { if (_shellCookie != 0) _shellConnection.Unadvise(_shellCookie); }
                catch (COMException) { /* Explorer may have exited. */ }
                Release(_shellConnection);
                _shellConnection = null;
                _shellCookie = 0;
            }
            _shellSink = null;
            Release(_windows);
            Release(_shell);
            _windows = null;
            _shell = null;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _stopping, 1) != 0) return;
            try { Volatile.Read(ref _dispatcher)?.BeginInvokeShutdown(DispatcherPriority.Send); }
            catch (InvalidOperationException) { /* The dispatcher already ended. */ }
        }

        public void SetPaused(bool paused)
        {
            Volatile.Write(ref _paused, paused ? 1 : 0);
            var dispatcher = Volatile.Read(ref _dispatcher);
            if (dispatcher is null) return;
            try { dispatcher.BeginInvoke(new Action(() =>
            {
                if (_timer is null) return;
                if (paused) _timer.Stop();
                else
                {
                    SafeReconcile();
                    _timer.Start();
                }
            })); }
            catch (InvalidOperationException) { /* The dispatcher already ended. */ }
        }

        private sealed class WindowSubscription : IDisposable
        {
            private static readonly Guid BrowserEventsId = new("34A715A0-6587-11D0-924A-0020AFC7AC4D");
            private readonly object _window;
            private IConnectionPoint? _connection;
            private BrowserNavigationEventsSink? _sink;
            private int _cookie;

            private WindowSubscription(object window, nint hwnd)
            {
                _window = window;
                Hwnd = hwnd;
            }

            public nint Hwnd { get; }
            public string Path { get; private set; } = "";

            public void ClearPath() => Path = "";

            public static WindowSubscription? TryCreate(object window, Worker owner)
            {
                var executable = (string)((dynamic)window).FullName;
                if (!string.Equals(System.IO.Path.GetFileName(executable), "explorer.exe", StringComparison.OrdinalIgnoreCase))
                    return null;
                var hwnd = new nint(Convert.ToInt64(((dynamic)window).HWND));
                var item = new WindowSubscription(window, hwnd);
                try
                {
                    var source = (IConnectionPointContainer)window;
                    var eventId = BrowserEventsId;
                    source.FindConnectionPoint(ref eventId, out item._connection);
                    item._sink = new BrowserNavigationEventsSink(() => owner.Navigated(item));
                    (item._connection ?? throw new InvalidOperationException("Navigation event connection unavailable."))
                        .Advise(item._sink, out item._cookie);
                    item.RefreshPath();
                    return item;
                }
                catch
                {
                    item.Unsubscribe(); // Caller still owns the window reference.
                    throw;
                }
            }

            public void RefreshPath()
            {
                object? document = null;
                object? folder = null;
                object? self = null;
                try
                {
                    document = ((dynamic)_window).Document;
                    folder = ((dynamic)document).Folder;
                    self = ((dynamic)folder).Self;
                    Path = (string)((dynamic)self).Path;
                }
                catch (COMException)
                {
                    Path = ""; // The new folder may still be loading.
                }
                finally
                {
                    Release(self);
                    Release(folder);
                    Release(document);
                }
            }

            public void Dispose()
            {
                Unsubscribe();
                Release(_window);
            }

            private void Unsubscribe()
            {
                if (_connection is not null)
                {
                    try { if (_cookie != 0) _connection.Unadvise(_cookie); }
                    catch (COMException) { /* The window may be gone. */ }
                    Release(_connection);
                    _connection = null;
                    _cookie = 0;
                }
                _sink = null;
            }

        }
    }

    private static void Release(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
            Marshal.ReleaseComObject(value);
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();
}
