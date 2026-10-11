using System;
using System.Collections.Generic;
using System.Runtime.Versioning;

namespace QuickerPlaces.Services.Revit.Dialogs;

/// <summary>
/// One button of a dialog: its text (as Windows reports it, "&amp;" accelerator
/// marks included) and an opaque handle that <see cref="IDialogDetector.PressButton"/>
/// can press. Classic message boxes have <c>Button</c> child windows; task
/// dialogs have the same under <c>DirectUIHWND</c> and <c>CtrlNotifySink</c>
/// (command links such as "Load Once" among them). Both come out the same.
/// </summary>
public sealed record DialogButton(string Text, long Handle, bool Visible = true);

/// <summary>
/// One top-level window of a process, as the dialog detector saw it at one
/// instant. Only <c>#32770</c> windows are searched for static text and
/// buttons; any other window comes with none, because Revit's main window
/// holds hundreds of children that nobody needs listed.
/// </summary>
public sealed record DialogWindow(
    long Handle,
    string ClassName,
    bool Enabled,
    bool Visible,
    string Title,
    IReadOnlyList<string> StaticTexts,
    IReadOnlyList<DialogButton> Buttons)
{
    /// <summary>The class Windows gives message boxes, task dialogs and common dialogs.</summary>
    public const string DialogClassName = "#32770";
}

/// <summary>
/// The seam between the dialog logic and Windows: lists a process's top-level
/// windows, and presses one button. Tests use a fake; the app and qp use
/// <see cref="DialogDetectors.ForThisMachine"/>.
/// </summary>
public interface IDialogDetector
{
    /// <summary>Every top-level window of <paramref name="processId"/>. Empty when there are none, the process is gone, or this isn't Windows; never throws.</summary>
    IReadOnlyList<DialogWindow> ListWindows(int processId);

    /// <summary>
    /// Presses a button (BM_CLICK). Nothing but <see cref="LoadOnceClicker"/>
    /// calls this: QuickerPlaces detects and reports, and presses only the
    /// one button the Load Once rules allow. False when the press could not
    /// be delivered.
    /// </summary>
    bool PressButton(DialogButton button);
}

/// <summary>The detector for the machine this runs on.</summary>
public static class DialogDetectors
{
    /// <summary>The Windows detector on Windows; a detector that sees nothing elsewhere.</summary>
    public static IDialogDetector ForThisMachine()
        => OperatingSystem.IsWindows() ? CreateWindows() : new NoDialogDetector();

    [SupportedOSPlatform("windows")]
    private static IDialogDetector CreateWindows() => new WindowsDialogDetector();

    private sealed class NoDialogDetector : IDialogDetector
    {
        public IReadOnlyList<DialogWindow> ListWindows(int processId) => Array.Empty<DialogWindow>();

        public bool PressButton(DialogButton button) => false;
    }
}
