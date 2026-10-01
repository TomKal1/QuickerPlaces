using System;
using System.Threading;

namespace QuickerPlaces.Services;

/// <summary>
/// The name of the running app's single-instance event (SingleInstance), and
/// a way for another process — the CLI — to ask whether that copy is up
/// without signalling it. UI-free, so the CLI and the tests can share it.
/// </summary>
public static class InstanceGate
{
    private const string BaseName = @"Local\" + AppInfo.Publisher + "." + AppInfo.Name + ".ShowWindow";

    /// <summary>The event's name for <paramref name="scope"/> (<see cref="AppDataFolders.InstanceScope"/>): null for the everyday stores.</summary>
    public static string EventName(string? scope) => scope is null ? BaseName : $"{BaseName}.{scope}";

    /// <summary>
    /// True when a QuickerPlaces window is running on these stores. Opens the
    /// event only to look, never sets it, so the running copy is not brought
    /// forward. Off Windows the app cannot run, so this is always false.
    /// </summary>
    public static bool IsAppRunning(string? scope)
    {
        if (!OperatingSystem.IsWindows())
            return false;

        if (!EventWaitHandle.TryOpenExisting(EventName(scope), out var handle))
            return false;

        handle.Dispose();
        return true;
    }
}
