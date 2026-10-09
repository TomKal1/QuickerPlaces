using System;
using System.ComponentModel;
using System.Diagnostics;

namespace QuickerPlaces.Services.Revit.Handlers;

/// <summary>Looks a process up by id, so liveness can be tested without real processes.</summary>
public interface IProcessProbe
{
    /// <summary>When the process with this id started, in UTC; null when there is no such process or it cannot be inspected.</summary>
    DateTime? GetStartTimeUtc(int processId);
}

/// <summary>The real thing, over <see cref="Process"/>.</summary>
public sealed class SystemProcessProbe : IProcessProbe
{
    public DateTime? GetStartTimeUtc(int processId)
    {
        // 0 is the idle process and negative ids are never real.
        if (processId <= 0)
            return null;

        try
        {
            using var process = Process.GetProcessById(processId);
            return process.StartTime.ToUniversalTime();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception or NotSupportedException)
        {
            // Gone (ArgumentException, InvalidOperationException), exited meanwhile, or not ours to inspect (Win32Exception).
            return null;
        }
    }
}
