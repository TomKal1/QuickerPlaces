using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using QuickerPlaces.Services;
using QuickerPlaces.Services.Activity;

namespace QuickerPlaces.ActivityProbe;

internal static class Program
{
    private static readonly object LogSync = new();
    private static string _logPath = "";

    private static int Main(string[] args)
    {
        var session = Path.Combine(Path.GetTempPath(), "QuickerPlaces.ActivityProbe-" + Guid.NewGuid().ToString("N"));
        var logDirectory = Path.Combine(session, "logs");
        var storeDirectory = Path.Combine(session, "store");
        Directory.CreateDirectory(logDirectory);
        Directory.CreateDirectory(storeDirectory);
        _logPath = Path.Combine(logDirectory, "activity-probe.log");
        // App diagnostics may include the temporary storage path on error.
        // Keep them in the disposable store, separate from the one log to send.
        DiagnosticLog.UseDirectoryForTests(Path.Combine(storeDirectory, "diagnostics"));
        try
        {
            Console.WriteLine("QuickerPlaces activity developer probe");
            Console.WriteLine("Debugger attached: " + Debugger.IsAttached);
            Console.WriteLine("Log to send: " + _logPath);
            Console.WriteLine("No observed folder paths are written to the log.");
            Log($"Activity probe started; debugger attached={Debugger.IsAttached}.");
            VerifyComSinks();

            string? pendingChoice = null;
            while (true)
            {
                Console.WriteLine("\n1 Live view   2 Stress (20,000 passes)   3 Host/lock run   Q Quit");
                var choice = pendingChoice ?? (args.Length > 0 ? args[0] : Console.ReadLine());
                pendingChoice = null;
                args = Array.Empty<string>();
                switch (choice?.Trim().ToLowerInvariant())
                {
                    case "1": case "live": pendingChoice = Live(); break;
                    case "2": case "stress": Stress(); break;
                    case "3": case "host": Host(storeDirectory); break;
                    case "q": case "quit": return 0;
                    case "": break; // An Enter typed after a live-view shortcut.
                    default: Console.WriteLine("Choose 1, 2, 3 or Q."); break;
                }
            }
        }
        finally
        {
            try { if (Directory.Exists(storeDirectory)) Directory.Delete(storeDirectory, recursive: true); }
            catch (IOException) { Console.WriteLine("Temporary activity store could not be deleted."); }
            catch (UnauthorizedAccessException) { Console.WriteLine("Temporary activity store could not be deleted."); }
            Log("Activity probe closed.");
            DiagnosticLog.ResetDirectoryForTests();
        }
    }

    private static void Log(string message)
    {
        lock (LogSync)
            File.AppendAllText(_logPath, $"{DateTimeOffset.UtcNow:O}  {message}{Environment.NewLine}");
    }

    private static void VerifyComSinks()
    {
        VerifyComSink(new ShellWindowEventsSink(() => { }),
            new Guid("FE4106E0-399A-11D0-A48C-00A0C90A8F39"));
        VerifyComSink(new BrowserNavigationEventsSink(() => { }),
            new Guid("34A715A0-6587-11D0-924A-0020AFC7AC4D"));
        Console.WriteLine("COM event sink interfaces: ready");
        Log("COM event sink interfaces ready.");
    }

    private static void VerifyComSink(object sink, Guid interfaceId)
    {
        var unknown = Marshal.GetIUnknownForObject(sink);
        try
        {
            var result = Marshal.QueryInterface(unknown, in interfaceId, out var found);
            if (result < 0) Marshal.ThrowExceptionForHR(result);
            Marshal.Release(found);
        }
        finally { Marshal.Release(unknown); }
    }

    private static string? Live()
    {
        using var probe = new EventShellWindowProbe();
        var loggedFailureKinds = new HashSet<string>(StringComparer.Ordinal);
        var failuresSinceSuccess = 0;
        Console.WriteLine("Explorer paths appear on screen only. Press 2 for Stress, 3 for Host, or Q to return.");
        while (true)
        {
            try
            {
                var windows = probe.Sample();
                if (failuresSinceSuccess > 0)
                {
                    var recovered = $"Probe recovered after {failuresSinceSuccess} failed samples.";
                    Console.WriteLine(recovered);
                    Log(recovered);
                    failuresSinceSuccess = 0;
                }
                Console.WriteLine($"{DateTime.Now:T}  {windows.Count} Explorer entries  windowEvents={probe.WindowEvents} navigationEvents={probe.NavigationEvents} reconciliations={probe.Reconciliations}");
                foreach (var window in windows)
                    Console.WriteLine($"  {(window.IsForeground ? '*' : ' ')} HWND={window.Hwnd}  {window.Path}");
            }
            catch (Exception ex)
            {
                failuresSinceSuccess++;
                if (failuresSinceSuccess == 1)
                    Console.WriteLine($"Probe failure: {ex.GetType().Name} (0x{ex.HResult:X8}); retrying.");
                if (loggedFailureKinds.Add(ex.GetType().FullName ?? ex.GetType().Name))
                    Log($"Live probe failed: {ex.GetType().Name} (0x{ex.HResult:X8}); further failures of this kind suppressed.");
            }
            for (var i = 0; i < 15; i++)
            {
                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(intercept: true).Key;
                    if (key is ConsoleKey.Q or ConsoleKey.Escape) return null;
                    if (key is ConsoleKey.D2 or ConsoleKey.NumPad2) return "2";
                    if (key is ConsoleKey.D3 or ConsoleKey.NumPad3) return "3";
                }
                Thread.Sleep(100);
            }
        }
    }

    private static void Stress()
    {
        const int passes = 20_000;
        using var probe = new EventShellWindowProbe();
        try
        {
            var initial = probe.Sample();
            var distinctHwnds = initial.Select(window => window.Hwnd).Distinct().Count();
            Console.WriteLine($"Event cache ready: {initial.Count} Explorer entries, {distinctHwnds} distinct HWNDs.");
            Log($"Event cache ready entries={initial.Count} distinctHwnds={distinctHwnds} reconciliations={probe.Reconciliations}.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Event cache setup failed: {ex.GetType().Name} (0x{ex.HResult:X8}).");
            Log($"Event cache setup failed: {ex.GetType().Name} (0x{ex.HResult:X8}).");
            return;
        }
        using var current = Process.GetCurrentProcess();
        var durations = new double[passes];
        var failures = 0;
        var beforeCpu = current.TotalProcessorTime;
        var wall = Stopwatch.StartNew();
        Log("Stress run started; 20000 passes, resource sample every 1000.");
        for (var i = 0; i < passes; i++)
        {
            var pass = Stopwatch.StartNew();
            try { probe.Sample(); }
            catch (Exception ex)
            {
                failures++;
                if (failures <= 5)
                    Log($"Stress pass failed: {ex.GetType().Name} (0x{ex.HResult:X8}).");
            }
            durations[i] = pass.Elapsed.TotalMilliseconds;
            if ((i + 1) % 1000 != 0) continue;
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            current.Refresh();
            var line = $"Passes={i + 1} explorerHandles={ExplorerHandles()} privateBytes={current.PrivateMemorySize64} failures={failures}";
            Console.WriteLine(line);
            Log(line);
        }
        wall.Stop();
        var cpu = current.TotalProcessorTime - beforeCpu;
        Array.Sort(durations);
        var projectedCpu = cpu.TotalMilliseconds / passes / 1500 * 100;
        var summary = $"Stress complete passes={passes} failures={failures} meanMs={durations.Average():F3} p95Ms={durations[(int)(passes * .95)]:F3} maxMs={durations[^1]:F3} wallSeconds={wall.Elapsed.TotalSeconds:F1} projectedOneCoreCpuPercentAt1.5s={projectedCpu:F4} windowEvents={probe.WindowEvents} navigationEvents={probe.NavigationEvents} reconciliations={probe.Reconciliations} reconciliationMs={probe.ReconciliationTime.TotalMilliseconds:F3}";
        Console.WriteLine(summary);
        Log(summary);
    }

    private static int ExplorerHandles()
    {
        var total = 0;
        foreach (var process in Process.GetProcessesByName("explorer"))
        {
            using (process)
            {
                try { total += process.HandleCount; }
                catch (InvalidOperationException) { }
            }
        }
        return total;
    }

    private static void Host(string storeDirectory)
    {
        Directory.CreateDirectory(storeDirectory);
        var store = new ActivityStore(new FilePlacesStorage(storeDirectory, "activity.json"), TimeProvider.System);
        var result = store.TryAddRoot(@"C:\", null, out _, out var persistence);
        if (!result.Success || !persistence.Saved)
        {
            Console.WriteLine("Could not create the temporary root.");
            return;
        }

        var latestWakes = 0;
        var latestTicks = 0;
        var latestFailures = 0;
        int? wakesAtLock = null;
        var progress = Stopwatch.StartNew();
        using var host = new ActivityTrackingHost(store);
        host.WakeCompleted += loop =>
        {
            Volatile.Write(ref latestWakes, loop.Wakes);
            Volatile.Write(ref latestTicks, loop.Ticks);
            Volatile.Write(ref latestFailures, loop.ProbeFailures);
            if (progress.Elapsed >= TimeSpan.FromSeconds(15))
            {
                Console.WriteLine($"Host running: wakes={loop.Wakes}, ticks={loop.Ticks}, failures={loop.ProbeFailures}. Press Enter to stop.");
                progress.Restart();
            }
            if (loop.Wakes % 40 == 0)
                Log($"Host wakes={loop.Wakes} ticks={loop.Ticks} failures={loop.ProbeFailures}.");
        };
        host.SignalReceived += signal =>
        {
            Console.WriteLine($"Host signal: {signal}.");
            Log($"Host signal={signal} wakes={Volatile.Read(ref latestWakes)} ticks={Volatile.Read(ref latestTicks)}.");
        };
        host.SignalHandled += (signal, loop) =>
        {
            if (signal == TrackingSignal.Locked)
                wakesAtLock = loop.Wakes;
            else if (signal == TrackingSignal.Unlocked && wakesAtLock is { } lockedWakes)
            {
                // The unlock signal itself wakes the host once. All other
                // wakes during the lock would be unwanted timer wakes.
                var timerWakes = Math.Max(0, loop.Wakes - lockedWakes - 1);
                Console.WriteLine($"Locked interval timer wakes: {timerWakes}.");
                Log($"Locked interval timerWakes={timerWakes}.");
                wakesAtLock = null;
            }
        };
        host.Start();
        Console.WriteLine("Host running with a temporary C:\\ root. Lock the PC for one minute, then restart Explorer. Press Enter to stop.");
        Console.ReadLine();
        var stop = Stopwatch.StartNew();
        host.Stop();
        stop.Stop();
        var line = $"Host stopped wakes={Volatile.Read(ref latestWakes)} ticks={Volatile.Read(ref latestTicks)} failures={Volatile.Read(ref latestFailures)} stopMs={stop.Elapsed.TotalMilliseconds:F1} unsaved={store.HasUnsavedChanges}.";
        Console.WriteLine(line);
        Log(line);
    }
}
