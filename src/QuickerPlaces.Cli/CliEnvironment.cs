using System;
using System.Diagnostics;
using System.IO;
using QuickerPlaces.Services;
using QuickerPlaces.Services.Remote;

namespace QuickerPlaces.Cli;

/// <summary>
/// Everything the CLI takes from the machine, as seams the tests replace:
/// the clock, the shell that opens places, whether the app is running, and
/// the default data root.
/// </summary>
public sealed class CliEnvironment
{
    /// <summary>The environment variable that points the CLI at other stores, like --data-root.</summary>
    public const string DataRootVariable = "QUICKERPLACES_DATA_ROOT";

    public required TimeProvider Time { get; init; }

    public required IShell Shell { get; init; }

    /// <summary>Given <see cref="AppDataFolders.InstanceScope"/>, whether a QuickerPlaces window is running on those stores.</summary>
    public required Func<string?, bool> IsAppRunning { get; init; }

    public string? DefaultDataRoot { get; init; }

    /// <summary>
    /// Sends an operation to the running app on the stores of the given scope
    /// (<see cref="AppDataFolders.InstanceScope"/>) and returns its reply. Throws
    /// TimeoutException or IOException when no app answers.
    /// </summary>
    public required Func<string?, OperationRequest, OperationReply> SendToApp { get; init; }

    public static CliEnvironment ForThisMachine() => new()
    {
        Time = TimeProvider.System,
        Shell = new SystemShell(),
        IsAppRunning = InstanceGate.IsAppRunning,
        DefaultDataRoot = Environment.GetEnvironmentVariable(DataRootVariable),
        SendToApp = (scope, request) => RemoteCommandClient.Send(RemoteProtocol.PipeName(scope), request, TimeSpan.FromSeconds(3))
    };

    private sealed class SystemShell : IShell
    {
        public bool DirectoryExists(string path) => Directory.Exists(path);

        public bool FileExists(string path) => File.Exists(path);

        public void Open(string target) => Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
    }
}
