using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using QuickerPlaces.Services;
using QuickerPlaces.Services.Documents;
using QuickerPlaces.Services.Remote;
using QuickerPlaces.Services.Revit.Dialogs;
using QuickerPlaces.Services.Revit.Opening;

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

    /// <summary>
    /// Finds the PDF, Office, text, Revit and AutoCAD files open now, the way the app's
    /// Sessions screen does; null where that isn't possible (off Windows).
    /// </summary>
    public Func<OpenDocumentScan>? ScanOpenDocuments { get; init; }

    /// <summary>Lists a process's top-level windows for <c>qp revit dialogs</c>; null is treated as seeing none.</summary>
    public IDialogDetector? DialogDetector { get; init; }

    /// <summary>What <c>qp revit installs</c>, <c>handlers</c> and <c>open</c> take from the machine; null is a machine with no Revit.</summary>
    public RevitMachine? Revit { get; init; }

    public static CliEnvironment ForThisMachine() => new()
    {
        Time = TimeProvider.System,
        Shell = new SystemShell(),
        IsAppRunning = InstanceGate.IsAppRunning,
        DefaultDataRoot = Environment.GetEnvironmentVariable(DataRootVariable),
        SendToApp = (scope, request) => RemoteCommandClient.Send(RemoteProtocol.PipeName(scope), request, TimeSpan.FromSeconds(3)),
        ScanOpenDocuments = OpenDocumentScanner(),
        DialogDetector = DialogDetectors.ForThisMachine(),
        Revit = RevitMachine.ForThisMachine()
    };

    private static Func<OpenDocumentScan>? OpenDocumentScanner()
    {
        if (!OperatingSystem.IsWindows())
            return null;
        return ScanOnWindows;
    }

    [SupportedOSPlatform("windows")]
    private static OpenDocumentScan ScanOnWindows() => new WindowsOpenDocumentProbe(new WindowsRecentItems()).ScanAsync().GetAwaiter().GetResult();

    private sealed class SystemShell : IShell
    {
        public bool DirectoryExists(string path) => Directory.Exists(path);

        public bool FileExists(string path) => File.Exists(path);

        public void Open(string target) => Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
    }
}
