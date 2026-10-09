using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace QuickerPlaces.Services.Revit.Dialogs;

/// <summary>
/// Lists a process's top-level windows with plain Win32 calls (EnumWindows,
/// GetWindowThreadProcessId, GetClassName, IsWindowEnabled, IsWindowVisible,
/// GetWindowText, EnumChildWindows), the way a person would see them with
/// Spy++. It never touches the process's memory and never throws: a window
/// that closes mid-scan is skipped.
///
/// - For a <c>#32770</c> window, EnumChildWindows (which walks every
///   descendant) finds <c>Button</c> children, whether direct (message boxes)
///   or under <c>DirectUIHWND</c> and <c>CtrlNotifySink</c> (task dialogs), and
///   <c>Static</c> children (message text; a task dialog's text sits under
///   <c>CtrlNotifySink</c> too).
/// - Child text is read with WM_GETTEXT, because GetWindowText doesn't return
///   the text of another process's controls. The message is sent with a
///   timeout, so a hung Revit can't hang QuickerPlaces.
/// - <see cref="PressButton"/> sends BM_CLICK, also with a timeout.
///
/// Not verified against a real Revit prompt; <c>qp revit dialogs</c> is how
/// to see what each release actually shows.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsDialogDetector : IDialogDetector
{
    private const uint WmGetText = 0x000D;
    private const uint WmGetTextLength = 0x000E;
    private const uint BmClick = 0x00F5;
    private const uint SmtoAbortIfHung = 0x0002;
    private const uint MessageTimeoutMilliseconds = 500;
    private const int MaxText = 4096;

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder className, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowEnabled(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeoutText(IntPtr hwnd, uint message, IntPtr wParam, StringBuilder lParam,
        uint flags, uint timeoutMilliseconds, out IntPtr result);

    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW")]
    private static extern IntPtr SendMessageTimeoutPlain(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam,
        uint flags, uint timeoutMilliseconds, out IntPtr result);

    public IReadOnlyList<DialogWindow> ListWindows(int processId)
    {
        var found = new List<DialogWindow>();
        try
        {
            EnumWindows((hwnd, _) =>
            {
                try
                {
                    GetWindowThreadProcessId(hwnd, out var owner);
                    if (owner == (uint)processId)
                        found.Add(Describe(hwnd));
                }
                catch (Exception ex) when (ex is ExternalException or InvalidOperationException)
                {
                    // The window went away mid-scan; leave it out.
                }

                return true;
            }, IntPtr.Zero);
        }
        catch (Exception ex) when (ex is ExternalException or DllNotFoundException or EntryPointNotFoundException)
        {
            return Array.Empty<DialogWindow>();
        }

        return found;
    }

    public bool PressButton(DialogButton button)
    {
        try
        {
            var handle = new IntPtr(button.Handle);
            return SendMessageTimeoutPlain(handle, BmClick, IntPtr.Zero, IntPtr.Zero, SmtoAbortIfHung, MessageTimeoutMilliseconds, out _) != IntPtr.Zero;
        }
        catch (Exception ex) when (ex is ExternalException or DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    private static DialogWindow Describe(IntPtr hwnd)
    {
        var className = ClassOf(hwnd);
        var title = new StringBuilder(MaxText);
        GetWindowText(hwnd, title, title.Capacity);

        var statics = new List<string>();
        var buttons = new List<DialogButton>();
        if (className == DialogWindow.DialogClassName)
        {
            EnumChildWindows(hwnd, (child, _) =>
            {
                var childClass = ClassOf(child);
                if (childClass == "Button")
                    buttons.Add(new DialogButton(TextOf(child), child.ToInt64(), IsWindowVisible(child)));
                else if (childClass == "Static" && TextOf(child) is { Length: > 0 } text)
                    statics.Add(text);
                return true;
            }, IntPtr.Zero);
        }

        return new DialogWindow(hwnd.ToInt64(), className, IsWindowEnabled(hwnd), IsWindowVisible(hwnd), title.ToString(), statics, buttons);
    }

    private static string ClassOf(IntPtr hwnd)
    {
        var name = new StringBuilder(256);
        return GetClassName(hwnd, name, name.Capacity) > 0 ? name.ToString() : string.Empty;
    }

    /// <summary>A control's text, asked for with WM_GETTEXT (and a timeout) because it belongs to another process.</summary>
    private static string TextOf(IntPtr hwnd)
    {
        if (SendMessageTimeoutPlain(hwnd, WmGetTextLength, IntPtr.Zero, IntPtr.Zero, SmtoAbortIfHung, MessageTimeoutMilliseconds, out var length) == IntPtr.Zero
            || length.ToInt64() <= 0)
            return string.Empty;

        var text = new StringBuilder((int)Math.Min(length.ToInt64(), MaxText) + 1);
        return SendMessageTimeoutText(hwnd, WmGetText, new IntPtr(text.Capacity), text, SmtoAbortIfHung, MessageTimeoutMilliseconds, out _) != IntPtr.Zero
            ? text.ToString()
            : string.Empty;
    }
}
