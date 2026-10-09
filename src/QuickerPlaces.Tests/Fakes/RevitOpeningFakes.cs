using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using QuickerPlaces.Services.Activity;
using QuickerPlaces.Services.Revit.Opening;

namespace QuickerPlaces.Tests.Fakes;

/// <summary>An uninstall list, a Program Files folder and a set of existing files, all set by hand.</summary>
public sealed class FakeInstallSource : IRevitInstallSource
{
    public List<UninstallEntry> Entries { get; } = [];
    public string? ProgramFilesFolder { get; set; } = Path.Combine("pf");
    public List<string> AutodeskFolders { get; } = [];
    public HashSet<string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<UninstallEntry> ReadUninstallEntries() => Entries;
    public IReadOnlyList<string> SubfolderNames(string folder) => AutodeskFolders;
    public bool FileExists(string path) => Files.Contains(path);

    /// <summary>Installs a release in the default place: its folder and its exe.</summary>
    public string AddDefault(int year)
    {
        AutodeskFolders.Add("Revit " + year);
        var exe = Path.Combine(ProgramFilesFolder!, "Autodesk", "Revit " + year, "Revit.exe");
        Files.Add(exe);
        return exe;
    }
}

public sealed class FakeProcessLister : IRevitProcessLister
{
    public List<RevitProcessInfo> Processes { get; } = [];
    public IReadOnlyList<RevitProcessInfo> List() => Processes;
}

/// <summary>Records launches instead of starting anything.</summary>
public sealed class FakeLauncher : IRevitLauncher
{
    public List<RevitLaunch> Launches { get; } = [];
    public int NextProcessId { get; set; } = 7000;
    public bool Fails { get; set; }
    public Action<RevitLaunch>? OnLaunch { get; set; }

    public int Launch(RevitLaunch launch)
    {
        if (Fails)
            throw new IOException("Revit couldn't be started: boom");
        Launches.Add(launch);
        OnLaunch?.Invoke(launch);
        return NextProcessId++;
    }
}

public sealed class FakeNetworkDrives : INetworkDriveResolver
{
    public bool Throws { get; set; }

    public Dictionary<string, string> Map { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Maps by drive letter ("W:") to a UNC share.</summary>
    public string? GetNetworkPath(string driveLetterPath)
    {
        if (Throws)
            throw new InvalidOperationException("lookup failed");
        var drive = driveLetterPath[..2];
        return Map.TryGetValue(drive, out var share) ? share + driveLetterPath[2..] : null;
    }
}
