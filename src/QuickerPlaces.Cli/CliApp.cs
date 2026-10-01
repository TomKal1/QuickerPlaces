using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using QuickerPlaces.Models;
using QuickerPlaces.Services;
using QuickerPlaces.Services.Activity;
using QuickerPlaces.Services.RecentFiles;
using QuickerPlaces.Services.Sessions;

namespace QuickerPlaces.Cli;

/// <summary>
/// qp's entry point: finds the command, runs it, and prints exactly one JSON
/// document — <c>{"ok":true,"apiVersion":1,"command":…,"data":…}</c> or
/// <c>{"ok":false,"apiVersion":1,"command":…,"error":{"code","message","details"}}</c>
/// — then returns the exit code. Nothing else is ever printed on stdout, so
/// an agent can always parse it.
/// </summary>
public static class CliApp
{
    /// <summary>The output contract's version. Raised only for a change that could break a caller; added fields don't raise it.</summary>
    public const int ApiVersion = 1;

    private static readonly OptionSpec DataRoot = new("data-root",
        $"Use the stores under this folder instead of your own, as the app's --data-root does. Also read from {CliEnvironment.DataRootVariable}.", ValueName: "folder");

    private static readonly OptionSpec PrettyOption = new("pretty", "Indent the JSON for reading.", IsFlag: true);

    /// <summary>Options every command accepts.</summary>
    public static IReadOnlyList<OptionSpec> GlobalOptions { get; } = new[] { DataRoot, PrettyOption };

    public static IReadOnlyList<CommandSpec> Commands { get; } = BuildCatalogue();

    public static int Run(IReadOnlyList<string> args, TextWriter output, CliEnvironment environment)
    {
        var pretty = args.Contains("--pretty");
        string commandName = "";
        try
        {
            var (globals, commandArgs) = SplitGlobals(args);
            var (spec, rest) = Find(commandArgs);
            commandName = spec.Name;

            var parsed = CliArgs.Parse(rest, spec);

            var dataRoot = globals.DataRoot ?? environment.DefaultDataRoot;
            dataRoot = string.IsNullOrWhiteSpace(dataRoot) ? null : Path.GetFullPath(dataRoot);

            var data = spec.Run(new CliContext(environment, dataRoot), parsed);
            Write(output, pretty, new Dictionary<string, object?>
            {
                ["ok"] = true,
                ["apiVersion"] = ApiVersion,
                ["command"] = commandName,
                ["data"] = data
            });
            return ExitCodes.Ok;
        }
        catch (CliError error)
        {
            return Fail(output, pretty, commandName, error.Code, error.Message, error.Details, error.ExitCode);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Error($"qp {commandName} failed unexpectedly.", ex);
            return Fail(output, pretty, commandName, ErrorCodes.Internal, $"Unexpected {ex.GetType().Name}: {ex.Message}", null, ExitCodes.Internal);
        }
    }

    /// <summary>
    /// Takes the global options out of <paramref name="args"/> wherever they
    /// stand — before the command ("qp --data-root X places list") or after it
    /// — and returns the rest. Anything after a bare "--" is left alone.
    /// </summary>
    private static ((string? DataRoot, bool Pretty) Globals, List<string> Remaining) SplitGlobals(IReadOnlyList<string> args)
    {
        string? dataRoot = null;
        var pretty = false;
        var remaining = new List<string>();
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (arg == "--")
            {
                remaining.AddRange(args.Skip(i));
                break;
            }

            if (arg == "--pretty")
                pretty = true;
            else if (arg == "--data-root")
                dataRoot = i + 1 < args.Count ? args[++i] : throw CliError.Usage("--data-root needs a value.");
            else if (arg.StartsWith("--data-root=", StringComparison.Ordinal))
                dataRoot = arg["--data-root=".Length..];
            else
                remaining.Add(arg);
        }

        return ((dataRoot, pretty), remaining);
    }

    /// <summary>The longest command name the arguments start with ("places list" before "places"); no arguments, "help" or "--help" anywhere mean describe.</summary>
    private static (CommandSpec Spec, List<string> Remaining) Find(IReadOnlyList<string> args)
    {
        var words = args.TakeWhile(a => !a.StartsWith("--", StringComparison.Ordinal)).ToList();
        if (words.Count == 0 || words[0] is "help" || args.Contains("--help"))
            return (Commands.Single(c => c.Name == "describe"), new List<string>());

        foreach (var length in new[] { 2, 1 })
        {
            if (words.Count < length)
                continue;
            var name = string.Join(' ', words.Take(length)).ToLowerInvariant();
            if (Commands.FirstOrDefault(c => c.Name == name) is { } spec)
                return (spec, args.Skip(length).ToList());
        }

        var group = words[0].ToLowerInvariant();
        var inGroup = Commands.Where(c => c.Name.StartsWith(group + " ", StringComparison.Ordinal)).Select(c => c.Name).ToList();
        throw CliError.Usage(inGroup.Count > 0
            ? $"'{string.Join(' ', words.Take(2))}' isn't a command. Try: {string.Join(", ", inGroup)}."
            : $"'{words[0]}' isn't a command. Run 'qp describe' to list them.");
    }

    private static int Fail(TextWriter output, bool pretty, string command, string code, string message, System.Text.Json.Nodes.JsonObject? details, int exitCode)
    {
        Write(output, pretty, new Dictionary<string, object?>
        {
            ["ok"] = false,
            ["apiVersion"] = ApiVersion,
            ["command"] = command,
            ["error"] = new Dictionary<string, object?> { ["code"] = code, ["message"] = message, ["details"] = details }
        });
        return exitCode;
    }

    private static void Write(TextWriter output, bool pretty, object document)
    {
        output.WriteLine(JsonSerializer.Serialize(document, pretty ? Json.Pretty : Json.Options));
        output.Flush();
    }

    private static List<CommandSpec> BuildCatalogue()
    {
        var commands = new List<CommandSpec>
        {
            new("describe", "Describe every command, option, output envelope, error code and exit code, as JSON. Start here.", "no arguments", 0, 0,
                Array.Empty<OptionSpec>(), false, (_, _) => Describe(), new[] { "qp describe --pretty" }),
            new("status", "Where the stores are, whether each loads, and whether the app is running (which makes the CLI read-only).", "no arguments", 0, 0,
                Array.Empty<OptionSpec>(), false, (context, _) => Status(context), new[] { "qp status" })
        };
        commands.AddRange(PlaceCommands.All());
        commands.AddRange(ActivityCommands.All());
        return commands;
    }

    private static object Describe() => new Dictionary<string, object?>
    {
        ["name"] = "qp",
        ["summary"] = "QuickerPlaces from the command line: saved places (folders and links), their tags, notes and open history; project sessions; recently opened files and folders.",
        ["envelope"] = new Dictionary<string, object?>
        {
            ["success"] = "{\"ok\":true,\"apiVersion\":1,\"command\":\"…\",\"data\":{…}}",
            ["failure"] = "{\"ok\":false,\"apiVersion\":1,\"command\":\"…\",\"error\":{\"code\":\"…\",\"message\":\"…\",\"details\":{…}|null}}",
            ["notes"] = new[]
            {
                "stdout carries exactly one JSON document per run.",
                "Times are ISO 8601 UTC; period dates are yyyy-MM-dd in the machine's time zone.",
                "Fields are only ever added within an apiVersion; branch on error.code, not the message.",
                "Commands that write refuse with app_running while QuickerPlaces is open; reads always work."
            }
        },
        ["globalOptions"] = GlobalOptions.Select(OptionJson).ToList(),
        ["commands"] = Commands.Select(c => new Dictionary<string, object?>
        {
            ["name"] = c.Name,
            ["description"] = c.Description,
            ["arguments"] = c.Usage,
            ["writes"] = c.Writes,
            ["options"] = c.Options.Select(OptionJson).ToList(),
            ["examples"] = c.Examples
        }).ToList(),
        ["errorCodes"] = new Dictionary<string, object?>
        {
            [ErrorCodes.Usage] = new { exitCode = ExitCodes.Usage, meaning = "Bad command, option or value. Fix the call." },
            [ErrorCodes.NotFound] = new { exitCode = ExitCodes.NotFound, meaning = "No such place or session. details.suggestions may hold close matches." },
            [ErrorCodes.Ambiguous] = new { exitCode = ExitCodes.Invalid, meaning = "The reference matches several items. details.ids lists them; retry with an id." },
            [ErrorCodes.Invalid] = new { exitCode = ExitCodes.Invalid, meaning = "The change breaks a rule (duplicate alias or folder, bad path or link). The message says which." },
            [ErrorCodes.AppRunning] = new { exitCode = ExitCodes.AppRunning, meaning = "QuickerPlaces is open, so changes are refused. Ask the user to close it, or only read." },
            [ErrorCodes.StoreUnavailable] = new { exitCode = ExitCodes.StoreUnavailable, meaning = "A store file is damaged, unreadable or from a newer version. details.outcome says which." },
            [ErrorCodes.SaveFailed] = new { exitCode = ExitCodes.StoreUnavailable, meaning = "The change was refused by the disk. Nothing was saved." },
            [ErrorCodes.OpenFailed] = new { exitCode = ExitCodes.OpenFailed, meaning = "The place couldn't be opened; details.status is missing or failed." },
            [ErrorCodes.Internal] = new { exitCode = ExitCodes.Internal, meaning = "A bug. The message names the exception." }
        }
    };

    private static Dictionary<string, object?> OptionJson(OptionSpec option) => new()
    {
        ["name"] = "--" + option.Name,
        ["value"] = option.IsFlag ? null : option.ValueName ?? "value",
        ["repeatable"] = option.Repeatable,
        ["description"] = option.Description
    };

    private static object Status(CliContext context)
    {
        var appRunning = context.AppRunning;
        var places = new PlacesService(new ReadOnlyStorage(context.PlacesFile), context.Time);
        var sessions = context.ReadSessions();
        var recentFiles = context.ReadRecentFiles();
        var activity = context.ReadActivity();

        return new Dictionary<string, object?>
        {
            ["dataRoot"] = context.DataRoot,
            ["appRunning"] = appRunning,
            ["writable"] = !appRunning,
            ["stores"] = new[]
            {
                StoreJson("places", context.PlacesFile.StoreFilePath, places.LoadOutcome, PlacesService.CurrentSchemaVersion,
                    new() { ["places"] = places.Places.Count, ["recentlyDeleted"] = places.RecentlyDeleted.Count }),
                StoreJson("sessions", context.SessionsFile.StoreFilePath, sessions.LoadOutcome, SessionStore.CurrentSchemaVersion,
                    new() { ["sessions"] = sessions.Sessions.Count }),
                StoreJson("recentFiles", context.RecentFilesFile.StoreFilePath, recentFiles.LoadOutcome, RecentFilesStore.CurrentSchemaVersion,
                    new() { ["enabled"] = recentFiles.Settings.Enabled, ["files"] = recentFiles.QueryHistory().Count }),
                StoreJson("activity", context.ActivityFile.StoreFilePath, activity.LoadOutcome, ActivityStore.CurrentSchemaVersion,
                    new() { ["roots"] = activity.Roots.Count })
            }
        };
    }

    private static Dictionary<string, object?> StoreJson(string name, string path, StoreLoadOutcome outcome, int schemaVersion, Dictionary<string, object?> counts)
    {
        var usable = outcome is StoreLoadOutcome.Ok or StoreLoadOutcome.NotPresent;
        return new Dictionary<string, object?>
        {
            ["name"] = name,
            ["path"] = path,
            ["outcome"] = Json.Enum(outcome),
            ["readable"] = usable,
            ["schemaVersion"] = schemaVersion,
            ["counts"] = usable ? counts : null
        };
    }
}
