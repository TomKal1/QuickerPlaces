using System;
using System.Collections.Generic;
using System.Linq;
using QuickerPlaces.Services.Revit.AddIns;

namespace QuickerPlaces.Services.Revit.Dialogs;

/// <summary>What the click decision needs to know about the launch and the user's choices.</summary>
/// <param name="SwitchOn">The "Allow Load Once" setting (off by default).</param>
/// <param name="LaunchedByQuickerPlaces">This Revit process was started by QuickerPlaces.</param>
/// <param name="HandlerInstanceAppeared">That launch's handler instance file has already appeared, so the launch is over.</param>
/// <param name="Release">The release of the Revit process.</param>
/// <param name="Signatures">The recorded prompt signatures.</param>
/// <param name="Entries">The add-ins the user allowed.</param>
/// <param name="Manifests">The release's manifests, to check a name-only prompt.</param>
public sealed record LoadOnceContext(
    bool SwitchOn,
    bool LaunchedByQuickerPlaces,
    bool HandlerInstanceAppeared,
    int Release,
    SecurityPromptSignatures Signatures,
    IReadOnlyList<LoadOnceEntry> Entries,
    IReadOnlyList<AddInManifest> Manifests);

/// <summary>
/// The answer to "may QuickerPlaces press Load Once on this dialog?". When
/// <see cref="MayClick"/> is true, <see cref="Button"/> is the one button to
/// press. <see cref="EntryToRemove"/> and <see cref="FollowUp"/> are set when
/// the add-in's DLL changed since it was allowed.
/// </summary>
public sealed record LoadOnceDecision(
    bool MayClick,
    string Reason,
    DialogButton? Button = null,
    SecurityPromptRecognition? Prompt = null,
    LoadOnceEntry? Entry = null,
    LoadOnceEntry? EntryToRemove = null,
    string? FollowUp = null);

/// <summary>
/// The one rule for pressing a button in someone else's Revit. It says yes only
/// when every condition holds, and says why when one doesn't:
///
/// 1. the Load Once switch is on;
/// 2. the process was launched by QuickerPlaces and its handler instance
///    hasn't appeared yet;
/// 3. the release has a verified signature and the dialog matches it exactly
///    (title, buttons, text pattern);
/// 4. the add-in in the prompt matches exactly one entry (release, name and,
///    if the prompt shows it, the DLL path; a name-only prompt also needs
///    exactly one manifest with that name, whose DLL is the entry's);
/// 5. the entry's DLL still has the recorded SHA-256 (if not: no click, the
///    entry is marked for removal and a follow-up question is produced);
/// 6. the signature's recorded Load Once button is present exactly once.
///
/// The button is found by the recorded Load Once text and nothing else, so
/// "Always Load" and "Do Not Load" are never pressed. Pure: files are read
/// only through <c>hashOf</c>.
/// </summary>
public static class LoadOnceClickPolicy
{
    public static LoadOnceDecision Decide(LoadOnceContext context, DialogWindow window, Func<string, string?>? hashOf = null)
    {
        hashOf ??= DllFingerprint.Sha256;

        if (!context.SwitchOn)
            return No("Allow Load Once is off.");
        if (!context.LaunchedByQuickerPlaces)
            return No("This Revit wasn't launched by QuickerPlaces.");
        if (context.HandlerInstanceAppeared)
            return No("The handler has already appeared, so this launch is over.");
        if (RevitDialogClassifier.Judge(window) != DialogVerdict.Waiting)
            return No("The window isn't an enabled dialog with buttons.");
        if (!context.Signatures.HasVerified(context.Release))
            return No($"No verified security-prompt signature is recorded for Revit {context.Release}.");

        var prompt = context.Signatures.Recognise(context.Release, window, verifiedOnly: true);
        if (prompt is null)
            return No($"The dialog doesn't match the verified signature for Revit {context.Release}.");

        var candidates = context.Entries
            .Where(e => e.Release == context.Release && LoadOnceOffers.SameName(e.Name, prompt.Name)
                && (prompt.DllPath is null || LoadOnceOffers.SamePath(e.DllPath, prompt.DllPath)))
            .ToList();
        if (candidates.Count == 0)
            return No($"\"{prompt.Name}\" isn't on the Load Once list for Revit {context.Release}.", prompt);
        if (candidates.Count > 1)
            return No($"More than one Load Once entry matches \"{prompt.Name}\".", prompt);
        var entry = candidates[0];

        if (prompt.DllPath is null)
        {
            var manifests = LoadOnceOffers.Matching(context.Release, prompt.Name, null, context.Manifests);
            if (manifests.Count != 1 || !LoadOnceOffers.SamePath(manifests[0].Assembly, entry.DllPath))
                return No($"The prompt shows only a name, and the manifests for Revit {context.Release} don't single out the allowed \"{entry.Name}\".", prompt, entry);
        }

        var hash = hashOf(entry.DllPath);
        if (hash is null)
            return No($"The DLL {entry.DllPath} can't be read, so it can't be checked.", prompt, entry);
        if (!DllFingerprint.SameHash(hash, entry.DllSha256))
        {
            return new LoadOnceDecision(false, $"{entry.Name} has changed since it was allowed.", null, prompt, entry, entry,
                $"{entry.Name} changed since you allowed it — allow Load Once again?");
        }

        var loadOnce = DialogText.Normalise(prompt.Signature.LoadOnceButtonText);
        if (loadOnce.Length == 0)
            return No("The signature records no Load Once button.", prompt, entry);
        var buttons = window.Buttons.Where(b => b.Visible && string.Equals(DialogText.Normalise(b.Text), loadOnce, StringComparison.OrdinalIgnoreCase)).ToList();
        if (buttons.Count != 1)
            return No($"Expected exactly one \"{prompt.Signature.LoadOnceButtonText}\" button; found {buttons.Count}.", prompt, entry);

        return new LoadOnceDecision(true, "All conditions hold.", buttons[0], prompt, entry);
    }

    private static LoadOnceDecision No(string reason, SecurityPromptRecognition? prompt = null, LoadOnceEntry? entry = null)
        => new(false, reason, null, prompt, entry);
}

/// <summary>What one look at a Revit process came to.</summary>
public sealed record LoadOncePollResult(
    bool Clicked,
    IReadOnlyList<LoadOnceDecision> Decisions,
    IReadOnlyList<LoadOnceEntry> EntriesToRemove,
    IReadOnlyList<string> FollowUps);

/// <summary>
/// Looks at a Revit process's dialogs and, if <see cref="LoadOnceClickPolicy"/>
/// allows, presses the one Load Once button. At most one press per look, and
/// every press is written to the log with the add-in, release, process,
/// DLL and hash. The launcher (next task) calls <see cref="Poll"/> while it
/// waits for the handler; the caller owns the timing.
/// </summary>
public sealed class LoadOnceClicker
{
    private readonly IDialogDetector _detector;
    private readonly Func<string, string?> _hashOf;
    private readonly Action<string> _log;

    public LoadOnceClicker(IDialogDetector detector, Func<string, string?>? hashOf = null, Action<string>? log = null)
    {
        _detector = detector;
        _hashOf = hashOf ?? DllFingerprint.Sha256;
        _log = log ?? DiagnosticLog.Info;
    }

    public LoadOncePollResult Poll(int processId, LoadOnceContext context)
    {
        var decisions = new List<LoadOnceDecision>();
        var remove = new List<LoadOnceEntry>();
        var followUps = new List<string>();
        var clicked = false;

        foreach (var window in _detector.ListWindows(processId))
        {
            if (RevitDialogClassifier.Judge(window) != DialogVerdict.Waiting)
                continue;

            var decision = LoadOnceClickPolicy.Decide(context, window, _hashOf);
            decisions.Add(decision);
            if (decision.EntryToRemove is { } stale)
            {
                remove.Add(stale);
                if (decision.FollowUp is { } followUp)
                    followUps.Add(followUp);
            }

            if (!decision.MayClick || clicked)
                continue;

            var delivered = _detector.PressButton(decision.Button!);
            clicked = true;
            _log($"Load Once: pressed \"{decision.Button!.Text}\" for \"{decision.Entry!.Name}\" in Revit {context.Release} (process {processId}); "
                + $"DLL {decision.Entry.DllPath}, SHA-256 {decision.Entry.DllSha256}; delivered: {delivered}.");
        }

        return new LoadOncePollResult(clicked, decisions, remove, followUps);
    }
}
