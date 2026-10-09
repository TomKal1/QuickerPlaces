using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace QuickerPlaces.Services.Revit.Opening;

/// <summary>A running <c>Revit.exe</c> as the machine reports it, before its release is worked out.</summary>
public sealed record RevitProcessInfo(int ProcessId, DateTime StartUtc, string? ExePath, int? FileMajorVersion);

/// <summary>A running <c>Revit.exe</c> with its release, or null when neither its path nor its version says.</summary>
public sealed record RunningRevit(int ProcessId, DateTime StartUtc, int? Release, string? ExePath)
{
    public string? ReleaseText => Release?.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>Lists the running <c>Revit.exe</c> processes, so planning can be tested without any.</summary>
public interface IRevitProcessLister
{
    IReadOnlyList<RevitProcessInfo> List();
}

public static class RevitProcesses
{
    /// <summary>
    /// The release of each process. First choice: the process's executable
    /// path equals an installed release's <c>Revit.exe</c> (compared without
    /// regard to case or a trailing separator). Fallback, when the path is
    /// unknown or matches no install: the file version's major number plus
    /// 2000 (Revit 2025's Revit.exe is version 25.x), accepted only from 2022.
    /// </summary>
    public static IReadOnlyList<RunningRevit> Resolve(IEnumerable<RevitProcessInfo> processes, IReadOnlyList<RevitInstall> installs) =>
        processes.Select(p => new RunningRevit(p.ProcessId, p.StartUtc, ReleaseOf(p, installs), p.ExePath))
            .OrderBy(p => p.ProcessId)
            .ToList();

    private static int? ReleaseOf(RevitProcessInfo process, IReadOnlyList<RevitInstall> installs)
    {
        if (!string.IsNullOrEmpty(process.ExePath))
        {
            var path = Normalise(process.ExePath);
            var match = installs.FirstOrDefault(i => string.Equals(Normalise(i.ExePath), path, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
                return match.Release;
        }

        if (process.FileMajorVersion is { } major && major + 2000 >= RevitFileInfoReader.OldestSupportedRelease && major < 100)
            return major + 2000;
        return null;
    }

    private static string Normalise(string path) =>
        path.Replace('/', '\\').TrimEnd('\\');
}

/// <summary>The real lister, over <see cref="Process"/>; a process that can't be inspected is left out or listed without a path.</summary>
public sealed class SystemRevitProcessLister : IRevitProcessLister
{
    public IReadOnlyList<RevitProcessInfo> List()
    {
        var result = new List<RevitProcessInfo>();
        Process[] processes;
        try
        {
            processes = Process.GetProcessesByName("Revit");
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or PlatformNotSupportedException)
        {
            return result;
        }

        foreach (var process in processes)
        {
            using (process)
            {
                try
                {
                    var started = process.StartTime.ToUniversalTime();
                    string? exe = null;
                    int? major = null;
                    try
                    {
                        exe = process.MainModule?.FileName;
                        if (exe is not null)
                            major = FileVersionInfo.GetVersionInfo(exe).FileMajorPart;
                    }
                    catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException or IOException)
                    {
                        // Not ours to inspect (another session or elevation): listed without a path.
                    }
                    result.Add(new RevitProcessInfo(process.Id, started, exe, major));
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    // Gone, or not ours to inspect.
                }
            }
        }
        return result;
    }
}
