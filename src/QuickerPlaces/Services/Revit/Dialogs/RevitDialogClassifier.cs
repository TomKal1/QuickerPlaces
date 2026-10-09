using System;
using System.Collections.Generic;
using System.Linq;

namespace QuickerPlaces.Services.Revit.Dialogs;

/// <summary>Whether one window is a dialog Revit is waiting on, and if not why not.</summary>
public enum DialogVerdict
{
    /// <summary>An enabled, visible dialog with something to press: a person has to answer it.</summary>
    Waiting,

    /// <summary>Not a <c>#32770</c> window: Revit's own windows, tool windows, hidden helpers.</summary>
    NotADialog,

    /// <summary>A dialog that is disabled, because another dialog is on top of it.</summary>
    Disabled,

    /// <summary>A dialog that isn't shown.</summary>
    Hidden,

    /// <summary>A dialog with no buttons: a progress window ("Model Upgrade"), which finishes by itself.</summary>
    NoButtons,

    /// <summary>A dialog whose only buttons are Cancel-like ("Cancel Link"): progress with a way out, not a question.</summary>
    ProgressOnly,
}

/// <summary>A dialog Revit is waiting on, ready to show the user.</summary>
public sealed record WaitingDialog(
    long Handle,
    string Title,
    IReadOnlyList<string> StaticTexts,
    IReadOnlyList<string> ButtonTexts,
    SecurityPromptRecognition? SecurityPrompt);

/// <summary>What a snapshot of a Revit process's windows comes to.</summary>
public sealed record DialogClassification(
    IReadOnlyList<WaitingDialog> Waiting,
    IReadOnlyList<DialogVerdict> Verdicts,
    string? StatusText);

/// <summary>
/// Decides, from a snapshot, which dialogs Revit is waiting on. Pure: no
/// Windows calls, so it is tested with made-up snapshots.
///
/// A window is "waiting" when it is an enabled, visible <c>#32770</c> window
/// with at least one visible button, unless every button is Cancel-like. That
/// last rule is a judgement: RevitBatchProcessor saw "Load Link" with only
/// "Cancel Link" while a link loads, which is progress, and nobody needs
/// telling about it. A Cancel-like button is one whose text starts with
/// "Cancel" (English; another language's wording is reported as waiting,
/// which errs towards telling the user). A dialog that really has only a
/// Cancel button would be missed; none is known. The window's own PID is
/// the detector's business (<see cref="IDialogDetector.ListWindows"/>), so
/// other processes' windows never get here.
/// </summary>
public static class RevitDialogClassifier
{
    public static DialogVerdict Judge(DialogWindow window)
    {
        if (window.ClassName != DialogWindow.DialogClassName)
            return DialogVerdict.NotADialog;
        if (!window.Enabled)
            return DialogVerdict.Disabled;
        if (!window.Visible)
            return DialogVerdict.Hidden;

        var buttons = window.Buttons.Where(b => b.Visible).ToList();
        if (buttons.Count == 0)
            return DialogVerdict.NoButtons;
        if (buttons.All(b => IsCancelLike(b.Text)))
            return DialogVerdict.ProgressOnly;
        return DialogVerdict.Waiting;
    }

    /// <param name="release">The release of the Revit process, when known; security prompts are recognised only with it.</param>
    public static DialogClassification Classify(IReadOnlyList<DialogWindow> windows, int? release, SecurityPromptSignatures signatures)
    {
        var verdicts = windows.Select(Judge).ToList();
        var waiting = new List<WaitingDialog>();
        for (var i = 0; i < windows.Count; i++)
        {
            if (verdicts[i] != DialogVerdict.Waiting)
                continue;

            var window = windows[i];
            var prompt = release is { } r ? signatures.Recognise(r, window) : null;
            waiting.Add(new WaitingDialog(
                window.Handle,
                window.Title,
                window.StaticTexts,
                window.Buttons.Where(b => b.Visible).Select(b => b.Text).ToList(),
                prompt));
        }

        return new DialogClassification(waiting, verdicts, StatusText(waiting));
    }

    /// <summary>
    /// "Revit is waiting on a dialog: Security - Unsigned Add-In (MyAddin)",
    /// naming the add-in only when a signature recognised the prompt. Null
    /// when nothing is waiting. Several dialogs: the first, then "and N more".
    /// </summary>
    public static string? StatusText(IReadOnlyList<WaitingDialog> waiting)
    {
        if (waiting.Count == 0)
            return null;

        var first = waiting[0];
        var title = string.IsNullOrWhiteSpace(first.Title) ? "(no title)" : first.Title;
        var text = "Revit is waiting on a dialog: " + title;
        if (first.SecurityPrompt is { } prompt)
            text += " (" + prompt.Name + ")";
        if (waiting.Count > 1)
            text += $" and {waiting.Count - 1} more";
        return text;
    }

    private static bool IsCancelLike(string text)
        => DialogText.Normalise(text).StartsWith("Cancel", StringComparison.OrdinalIgnoreCase);
}
