using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace QuickerPlaces.Services.Revit.Opening;

/// <summary>One <c>Revit.exe</c> launch: what to run, with which arguments (already quoted) and extra environment variables.</summary>
public sealed record RevitLaunch(string ExePath, string? Arguments, IReadOnlyDictionary<string, string> Environment);

/// <summary>Starts Revit, so the runner is tested without starting anything.</summary>
public interface IRevitLauncher
{
    /// <summary>Starts the process and returns its id. Throws <see cref="IOException"/> when it can't be started.</summary>
    int Launch(RevitLaunch launch);
}

/// <summary>
/// The real launcher: <c>UseShellExecute=false</c> (needed to set environment
/// variables), working folder the exe's own, no window handling. Never waits
/// for or ends the process.
/// </summary>
public sealed class SystemRevitLauncher : IRevitLauncher
{
    public int Launch(RevitLaunch launch)
    {
        var start = new ProcessStartInfo(launch.ExePath)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(launch.ExePath) ?? "",
        };
        if (!string.IsNullOrEmpty(launch.Arguments))
            start.Arguments = launch.Arguments;
        foreach (var (name, value) in launch.Environment)
            start.Environment[name] = value;

        try
        {
            using var process = Process.Start(start) ?? throw new IOException("Windows didn't start " + launch.ExePath + ".");
            return process.Id;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            throw new IOException("Revit couldn't be started: " + ex.Message, ex);
        }
    }
}
