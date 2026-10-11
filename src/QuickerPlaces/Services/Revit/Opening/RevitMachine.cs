using System;
using System.Collections.Generic;
using QuickerPlaces.Services.Activity;
using QuickerPlaces.Services.Revit.Handlers;

namespace QuickerPlaces.Services.Revit.Opening;

/// <summary>
/// Everything opening a Revit file takes from the machine, as seams: what is
/// installed, what is running, how Revit is started, how a process is
/// probed, how a mapped drive becomes UNC. Tests (and qp's tests) replace them.
/// </summary>
public sealed class RevitMachine
{
    public required IRevitInstallSource InstallSource { get; init; }
    public required IRevitProcessLister Processes { get; init; }
    public required IRevitLauncher Launcher { get; init; }
    public required IProcessProbe Probe { get; init; }
    public INetworkDriveResolver? Network { get; init; }

    public IReadOnlyList<RevitInstall> Installs() => RevitInstallFinder.Find(InstallSource);

    public IReadOnlyList<RunningRevit> RunningRevits(IReadOnlyList<RevitInstall>? installs = null) =>
        RevitProcesses.Resolve(Processes.List(), installs ?? Installs());

    public static RevitMachine ForThisMachine() => new()
    {
        InstallSource = new SystemRevitInstallSource(),
        Processes = new SystemRevitProcessLister(),
        Launcher = new SystemRevitLauncher(),
        Probe = new SystemProcessProbe(),
        Network = OperatingSystem.IsWindows() ? new NetworkDriveResolver() : null,
    };

    /// <summary>A machine with no Revit: nothing installed or running.</summary>
    public static RevitMachine None() => new()
    {
        InstallSource = new NoInstalls(),
        Processes = new NoProcesses(),
        Launcher = new NoLauncher(),
        Probe = new SystemProcessProbe(),
    };

    private sealed class NoInstalls : IRevitInstallSource
    {
        public IReadOnlyList<UninstallEntry> ReadUninstallEntries() => [];
        public string? ProgramFilesFolder => null;
        public IReadOnlyList<string> SubfolderNames(string folder) => [];
        public bool FileExists(string path) => false;
    }

    private sealed class NoProcesses : IRevitProcessLister
    {
        public IReadOnlyList<RevitProcessInfo> List() => [];
    }

    private sealed class NoLauncher : IRevitLauncher
    {
        public int Launch(RevitLaunch launch) => throw new System.IO.IOException("No Revit is available.");
    }
}
