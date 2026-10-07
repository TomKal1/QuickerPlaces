using System;
using System.Collections.Generic;

namespace QuickerPlaces.Services;

/// <summary>
/// The command line QuickerPlaces understands:
///
/// - <c>--tray</c>: start hidden in the tray (the Windows sign-in entry passes it).
/// - The workspace is shown by default.
/// - <c>--workspace</c>: explicitly show the workspace (also the default).
/// - <c>--places-list</c>: show the older places list instead of the workspace.
/// - <c>--data-root &lt;folder&gt;</c> or <c>--data-root=&lt;folder&gt;</c>: keep every
///   store under that folder (<see cref="AppDataFolders"/>), for trying the
///   workspace on test data.
///
/// Names ignore case; anything else is ignored. UI-free and linked into the
/// test project.
/// </summary>
public sealed record StartupOptions(bool Tray, bool Workspace, string? DataRoot, IReadOnlyList<string> Problems)
{
    public const string TrayArg = "--tray";
    public const string WorkspaceArg = "--workspace";
    public const string PlacesListArg = "--places-list";
    public const string DataRootArg = "--data-root";

    public static StartupOptions Parse(IReadOnlyList<string> args)
    {
        var tray = false;
        var workspace = true;
        string? dataRoot = null;
        var problems = new List<string>();

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (Is(arg, TrayArg))
            {
                tray = true;
            }
            else if (Is(arg, WorkspaceArg))
            {
                workspace = true;
            }
            else if (Is(arg, PlacesListArg))
            {
                workspace = false;
            }
            else if (Is(arg, DataRootArg))
            {
                if (i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(args[i + 1]))
                    dataRoot = args[++i];
                else
                    problems.Add($"{DataRootArg} needs a folder after it.");
            }
            else if (arg.StartsWith(DataRootArg + "=", StringComparison.OrdinalIgnoreCase))
            {
                var value = arg[(DataRootArg.Length + 1)..].Trim('"');
                if (string.IsNullOrWhiteSpace(value))
                    problems.Add($"{DataRootArg} needs a folder after it.");
                else
                    dataRoot = value;
            }
        }

        return new StartupOptions(tray, workspace, dataRoot, problems);
    }

    private static bool Is(string arg, string name) => string.Equals(arg, name, StringComparison.OrdinalIgnoreCase);
}
