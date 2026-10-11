using System.Collections.Generic;
using System;
using System.IO;
using System.Linq;
using QuickerPlaces.Services.Revit;
using QuickerPlaces.Services.Revit.Dialogs;
using QuickerPlaces.Services.Revit.Handlers;
using QuickerPlaces.Services.Revit.Opening;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace QuickerPlaces.Cli;

/// <summary>
/// <c>qp revit installs</c>, <c>handlers</c>, <c>open</c>, <c>dialogs</c> and <c>info</c>.
/// <c>info</c>: the release a Revit file was saved in and whether it
/// is a central or a local, read as the app reads it (roadmap §4.21). Lets
/// users and agents check, or report, a file the reader gets wrong.
/// </summary>
public static class RevitCommands
{
    public static IEnumerable<CommandSpec> All() => new[]
    {
        new CommandSpec("revit info", "The Revit release a .rvt, .rfa or .rte file was saved in, and whether it is workshared, a central or a local. Read from the file without Revit and without changing it. Revit 2022 and later are supported; older files give problem tooOld. release is null when the file doesn't say; problem says why.", "<file>", 1, 1,
            System.Array.Empty<OptionSpec>(), false, Info, new[] { "qp revit info \"C:\\REVIT_LOCAL2025\\Tower_A_someone.rvt\"" }),
        new CommandSpec("revit dialogs", "Every top-level window of a running process (a Revit.exe), with class, enabled, visible, title, static texts and buttons, and which of them count as dialogs Revit is waiting on. Read-only: nothing is pressed. Use it while an add-in security prompt is showing to record what that release's prompt looks like. With --release, a prompt that matches a recorded signature names its add-in. Windows only; elsewhere the list is empty.", "<pid>", 1, 1,
            new[] { new OptionSpec("release", "The Revit release of the process (for example 2025), to recognise a security prompt from the recorded signatures.", ValueName: "year") },
            false, Dialogs, new[] { "qp revit dialogs 12345 --release 2025" }),
        new CommandSpec("revit installs", "The installed Revit releases (2022 and later) with their Revit.exe, found from the Windows uninstall list and the default Program Files folders, and the Revit processes running now. Read-only. Empty off Windows.", "no arguments", 0, 0,
            Array.Empty<OptionSpec>(), false, Installs, new[] { "qp revit installs" }),
        new CommandSpec("revit handlers", "Per Revit release: the handler add-ins that have registered (docs/revit-handler-protocol.md), whether each is loaded in a running Revit, when it was last seen, and which one the settings choose. Read-only.", "no arguments", 0, 0,
            Array.Empty<OptionSpec>(), false, Handlers, new[] { "qp revit handlers" }),
        new CommandSpec("revit open", "Opens a Revit file in the Revit release that saved it, never another. A local or non-workshared file opens by launching that release's Revit.exe. A central opens as a new local through the chosen handler add-in (the file is never copied by QuickerPlaces); it is refused when no handler is chosen or registered, or when the file is a copy of a central. --dry-run prints the plan without writing or launching. Prints one JSON document with the plan and the outcome; a refusal or a failed open is an error (invalid, open_failed) whose details hold them.", "<file>", 1, 1,
            new[]
            {
                new OptionSpec("dry-run", "Print what would be done; write nothing and launch nothing.", IsFlag: true),
                new OptionSpec("handler", "The handler id to use for a central. Default: the settings' choice for the release, else the only registered handler that can open a new local.", ValueName: "id"),
                new OptionSpec("local-folder", "Where the new local goes. Default: the settings' folder for the release, else C:\\REVIT_LOCAL<release>.", ValueName: "folder"),
                new OptionSpec("worksets", "Which worksets the local opens: lastViewed (default), all or none.", ValueName: "lastViewed|all|none"),
                new OptionSpec("no-wait", "Return once the request is written (and Revit launched if needed) instead of waiting for the result.", IsFlag: true)
            },
            true, Open, new[] { "qp revit open \"\\\\server\\projects\\Tower_Central.rvt\" --dry-run", "qp revit open \"C:\\Jobs\\Tower.rvt\" --handler contoso.revittools --worksets none" })
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

    private static object Dialogs(CliContext context, CliArgs args)
    {
        if (!int.TryParse(args.Positionals[0], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var pid) || pid <= 0)
            throw CliError.Usage("<pid> must be a process id (a positive whole number).");
        var release = args.Int("release", 2000, 2100);

        var windows = context.Environment.DialogDetector?.ListWindows(pid) ?? Array.Empty<DialogWindow>();
        var signatures = SecurityPromptSignatures.Load(Path.Combine(AppContext.BaseDirectory, SecurityPromptSignatures.FileName));
        return DialogsData(pid, release, windows, RevitDialogClassifier.Classify(windows, release, signatures), signatures.Problem);
    }

    /// <summary>The JSON <c>revit dialogs</c> prints: every window, then the classification.</summary>
    public static object DialogsData(int processId, int? release, System.Collections.Generic.IReadOnlyList<DialogWindow> windows, DialogClassification classification, string? signaturesProblem = null)
        => new Dictionary<string, object?>
        {
            ["processId"] = processId,
            ["release"] = release,
            ["windows"] = windows.Select((w, i) => new Dictionary<string, object?>
            {
                ["handle"] = w.Handle,
                ["class"] = w.ClassName,
                ["enabled"] = w.Enabled,
                ["visible"] = w.Visible,
                ["title"] = w.Title,
                ["staticTexts"] = w.StaticTexts,
                ["buttons"] = w.Buttons.Select(b => new Dictionary<string, object?> { ["text"] = b.Text, ["visible"] = b.Visible }).ToList(),
                ["verdict"] = Json.Enum(classification.Verdicts[i])
            }).ToList(),
            ["waiting"] = classification.Waiting.Select(d => new Dictionary<string, object?>
            {
                ["title"] = d.Title,
                ["staticTexts"] = d.StaticTexts,
                ["buttons"] = d.ButtonTexts,
                ["securityPrompt"] = d.SecurityPrompt is { } p
                    ? new Dictionary<string, object?> { ["name"] = p.Name, ["dllPath"] = p.DllPath, ["signatureVerified"] = p.Signature.Verified }
                    : null
            }).ToList(),
            ["statusText"] = classification.StatusText,
            ["signaturesProblem"] = signaturesProblem
        };

    private static object Installs(CliContext context, CliArgs args)
    {
        var machine = context.Revit;
        var installs = machine.Installs();
        return new Dictionary<string, object?>
        {
            ["installs"] = installs.Select(i => new Dictionary<string, object?> { ["release"] = i.Release, ["exePath"] = i.ExePath }).ToList(),
            ["running"] = machine.RunningRevits(installs).Select(r => new Dictionary<string, object?>
            {
                ["processId"] = r.ProcessId,
                ["startedUtc"] = r.StartUtc,
                ["release"] = r.Release,
                ["exePath"] = r.ExePath
            }).ToList()
        };
    }

    private static object Handlers(CliContext context, CliArgs args)
    {
        var folder = context.RevitFolder;
        var settings = context.ReadSettings();
        var machine = context.Revit;
        var registry = new RevitHandlerRegistry(folder, machine.Probe, context.Time);
        var registered = registry.Read();
        var installed = machine.Installs().Select(i => i.Release.ToString(System.Globalization.CultureInfo.InvariantCulture));

        var releases = registered.Select(r => r.Release).Union(installed).OrderBy(r => r, StringComparer.Ordinal).ToList();
        return new Dictionary<string, object?>
        {
            ["protocolRoot"] = folder.Root,
            ["releases"] = releases.Select(release =>
            {
                var effective = RevitSettingsResolver.For(settings, release);
                return new Dictionary<string, object?>
                {
                    ["release"] = release,
                    ["installed"] = installed.Contains(release),
                    ["chosenHandlerId"] = effective.HandlerId,
                    ["localFolder"] = effective.LocalFolder,
                    ["handlers"] = registered.FirstOrDefault(r => r.Release == release)?.Handlers.Select(h => new Dictionary<string, object?>
                    {
                        ["handlerId"] = h.HandlerId,
                        ["displayName"] = h.DisplayName,
                        ["handlerVersion"] = h.HandlerVersion,
                        ["actions"] = h.Actions,
                        ["chosen"] = h.HandlerId == effective.HandlerId,
                        ["loaded"] = h.IsLoaded,
                        ["ready"] = h.IsReady,
                        ["instances"] = h.Instances.Select(i => new Dictionary<string, object?>
                        {
                            ["processId"] = i.ProcessId,
                            ["loadedUtc"] = i.LoadedUtc,
                            ["readyUtc"] = i.ReadyUtc
                        }).ToList(),
                        ["lastSeenUtc"] = h.LastSeenUtc,
                        ["registeredUtc"] = h.WrittenUtc
                    }).ToList() ?? new List<Dictionary<string, object?>>()
                };
            }).ToList()
        };
    }

    private static object Open(CliContext context, CliArgs args)
    {
        var path = Path.GetFullPath(args.Positionals[0]);
        var info = RevitFileInfoReader.Read(path);
        if (info.Problem == RevitFileProblem.NotFound)
            throw new CliError(ErrorCodes.NotFound, $"No file at {path}.");

        var worksets = args.Value("worksets");
        if (worksets is not null && !HandlerProtocol.Worksets.IsKnown(worksets))
            throw CliError.Usage("--worksets must be lastViewed, all or none.");
        var localFolder = args.Value("local-folder");
        if (localFolder is not null && !LooksAbsolute(localFolder))
            throw CliError.Usage("--local-folder must be an absolute path (a drive letter or UNC path).");

        var machine = context.Revit;
        var settings = context.ReadSettings();
        var folder = context.RevitFolder;
        var registry = new RevitHandlerRegistry(folder, machine.Probe, context.Time);
        var handlers = registry.Read();
        var installs = machine.Installs();
        var running = machine.RunningRevits(installs);

        var handler = args.Value("handler");
        if (handler is null && info is { Problem: RevitFileProblem.None, Release: { } release, Worksharing: RevitWorksharing.Central })
            handler = ChooseHandler(handlers, settings, release.ToString(System.Globalization.CultureInfo.InvariantCulture));

        var plan = RevitOpenPlanner.Plan(path, info, installs, running, handlers, settings, context.Time.GetUtcNow().UtcDateTime,
            machine.Network, new RevitOpenOverrides(handler, localFolder, worksets));
        var planJson = PlanJson(plan);

        if (plan.Kind == RevitOpenKind.Refuse)
            throw new CliError(ErrorCodes.Invalid, plan.RefusalReason ?? "The file isn't opened.", new JsonObject { ["plan"] = ToNode(planJson) });

        if (args.Flag("dry-run"))
            return new Dictionary<string, object?> { ["dryRun"] = true, ["plan"] = planJson };

        var queue = new RevitRequestQueue(folder, context.Time);
        var opener = new RevitOpener(folder, queue, registry, machine, context.Time);
        var outcome = opener.RunAsync(plan, null, !args.Flag("no-wait")).GetAwaiter().GetResult();
        var outcomeJson = OutcomeJson(outcome);

        if (!outcome.Ok)
            throw new CliError(ErrorCodes.OpenFailed, outcome.Message, new JsonObject { ["plan"] = ToNode(planJson), ["outcome"] = ToNode(outcomeJson) });

        return new Dictionary<string, object?> { ["dryRun"] = false, ["plan"] = planJson, ["outcome"] = outcomeJson };
    }

    /// <summary>The settings' handler for the release, else the only registered one that can open a new local; several is an error naming them, none leaves the planner to refuse.</summary>
    private static string? ChooseHandler(IReadOnlyList<ReleaseHandlers> handlers, QuickerPlaces.Models.AppSettings settings, string release)
    {
        if (RevitSettingsResolver.For(settings, release).HandlerId is { } chosen)
            return chosen;

        var capable = handlers.FirstOrDefault(r => r.Release == release)?.Handlers
            .Where(h => h.Supports(HandlerProtocol.ActionOpenNewLocal)).ToList() ?? new List<RegisteredHandler>();
        if (capable.Count <= 1)
            return capable.FirstOrDefault()?.HandlerId;

        throw new CliError(ErrorCodes.Invalid,
            $"Several handlers can open a central in Revit {release}: {string.Join(", ", capable.Select(h => h.HandlerId))}. Choose one with --handler.",
            new JsonObject { ["choices"] = new JsonArray(capable.Select(h => (JsonNode?)JsonValue.Create(h.HandlerId)).ToArray()) });
    }

    private static Dictionary<string, object?> PlanJson(RevitOpenPlan plan) => new()
    {
        ["kind"] = Json.Enum(plan.Kind),
        ["file"] = plan.FilePath,
        ["release"] = plan.Release,
        ["refusalReason"] = plan.RefusalReason,
        ["exePath"] = plan.ExePath,
        ["handlerId"] = plan.HandlerId,
        ["handlerName"] = plan.HandlerName,
        ["localFolder"] = plan.LocalFolder,
        ["worksets"] = plan.Worksets,
        ["handlerState"] = plan.Kind == RevitOpenKind.HandlerRequest ? Json.Enum(plan.Availability) : null,
        ["needsLaunch"] = plan.NeedsLaunch,
        ["handlerNotLoaded"] = plan.HandlerNotLoaded,
        ["runningProcessIds"] = plan.RunningProcessIds
    };

    private static Dictionary<string, object?> OutcomeJson(RevitOpenOutcome outcome) => new()
    {
        ["result"] = Json.Enum(outcome.Result),
        ["ok"] = outcome.Ok,
        ["message"] = outcome.Message,
        ["localPath"] = outcome.LocalPath,
        ["errorCode"] = outcome.ErrorCode,
        ["requestId"] = outcome.RequestId,
        ["launchedProcessId"] = outcome.LaunchedProcessId,
        ["dialogs"] = outcome.DialogList.Select(d => new Dictionary<string, object?>
        {
            ["dialogId"] = d.DialogId,
            ["kind"] = d.Kind,
            ["message"] = d.Message,
            ["answered"] = d.Answered,
            ["answer"] = d.Answer
        }).ToList()
    };

    private static JsonNode? ToNode(object value) => JsonSerializer.SerializeToNode(value, Json.Options);

    private static bool LooksAbsolute(string path) =>
        path.StartsWith(@"\\", StringComparison.Ordinal) || (path.Length >= 3 && path[1] == ':' && char.IsAsciiLetter(path[0]) && path[2] is '\\' or '/');
}
