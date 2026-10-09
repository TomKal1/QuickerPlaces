using System.Collections.Generic;
using System.IO;
using QuickerPlaces.Services.Revit;

namespace QuickerPlaces.Cli;

/// <summary>
/// <c>qp revit info</c>: the release a Revit file was saved in and whether it
/// is a central or a local, read as the app reads it (roadmap §4.21). Lets
/// users and agents check, or report, a file the reader gets wrong.
/// </summary>
public static class RevitCommands
{
    public static IEnumerable<CommandSpec> All() => new[]
    {
        new CommandSpec("revit info", "The Revit release a .rvt, .rfa or .rte file was saved in, and whether it is workshared, a central or a local. Read from the file without Revit and without changing it. Revit 2022 and later are supported; older files give problem tooOld. release is null when the file doesn't say; problem says why.", "<file>", 1, 1,
            System.Array.Empty<OptionSpec>(), false, Info, new[] { "qp revit info \"C:\\REVIT_LOCAL2025\\Tower_A_someone.rvt\"" })
    };

    private static object Info(CliContext context, CliArgs args)
    {
        var path = Path.GetFullPath(args.Positionals[0]);
        var info = RevitFileInfoReader.Read(path);
        if (info.Problem == RevitFileProblem.NotFound)
            throw new CliError(ErrorCodes.NotFound, $"No file at {path}.");

        return new Dictionary<string, object?>
        {
            ["path"] = path,
            ["release"] = info.Release,
            ["build"] = info.Build,
            ["worksharing"] = Json.Enum(info.Worksharing),
            ["centralModelPath"] = info.CentralModelPath,
            ["lastSavePath"] = info.LastSavePath,
            ["layoutVersion"] = info.LayoutVersion,
            ["problem"] = info.Problem == RevitFileProblem.None ? null : Json.Enum(info.Problem)
        };
    }
}
