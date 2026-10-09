using System.Collections.Generic;
using System;
using System.IO;
using System.Linq;
using QuickerPlaces.Services.Revit;
using QuickerPlaces.Services.Revit.Dialogs;

namespace QuickerPlaces.Cli;

/// <summary>
/// <c>qp revit dialogs</c> and <c>qp revit info</c>: the release a Revit file was saved in and whether it
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
            false, Dialogs, new[] { "qp revit dialogs 12345 --release 2025" })
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
}
