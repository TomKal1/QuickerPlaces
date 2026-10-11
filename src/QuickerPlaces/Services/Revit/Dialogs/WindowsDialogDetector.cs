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
///   readable child text. DirectUI labels are read through MSAA when there
///   are no Static HWNDs for their text.
/// - Child text is read with WM_GETTEXT, because GetWindowText doesn't return
///   the text of another process's controls. The message is sent with a
///   timeout, so a hung Revit can't hang QuickerPlaces.
/// - <see cref="PressButton"/> sends BM_CLICK, also with a timeout.
///
/// Text and buttons verified against the English Revit 2025 unsigned add-in
/// prompt; use <c>qp revit dialogs</c> to record other releases.
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

    // Task dialogs render their labels inside DirectUI, without Static HWNDs.
    // MSAA exposes those labels without adding a WPF/UIAutomation dependency.
    [ComImport, Guid("618736E0-3C3D-11CF-810C-00AA00389B71"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface IAccessibleText
    {
        [DispId(-500)] object Parent { [return: MarshalAs(UnmanagedType.IDispatch)] get; }
        [DispId(-501)] int ChildCount { get; }
        [DispId(-502)] [return: MarshalAs(UnmanagedType.IDispatch)] object Child([In, MarshalAs(UnmanagedType.Struct)] object child);
        [DispId(-503)] [return: MarshalAs(UnmanagedType.BStr)] string? Name([In, MarshalAs(UnmanagedType.Struct)] object child);
    }

    [DllImport("oleacc.dll")]
    private static extern int AccessibleObjectFromWindow(IntPtr window, uint objectId, ref Guid interfaceId,
        [MarshalAs(UnmanagedType.Interface)] out IAccessibleText accessible);

    [DllImport("oleacc.dll")]
    private static extern int AccessibleChildren([MarshalAs(UnmanagedType.Interface)] IAccessibleText accessible,
        int start, int count, [Out, MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.Struct, SizeParamIndex = 2)] object[] children,
        out int obtained);

    private static void AddAccessibleText(IntPtr hwnd, List<string> texts)
    {
        try
        {
            var id = typeof(IAccessibleText).GUID;
            if (AccessibleObjectFromWindow(hwnd, unchecked((uint)-4), ref id, out var root) != 0)
                return;
            var remaining = 256;
            void Read(IAccessibleText node, int depth)
            {
                if (depth > 12 || remaining-- <= 0) return;
                var name = node.Name(0);
                if (!string.IsNullOrWhiteSpace(name) && !texts.Contains(name)) texts.Add(name);
                var count = Math.Min(node.ChildCount, Math.Max(0, remaining));
                if (count == 0) return;
                var children = new object[count];
                if (AccessibleChildren(node, 0, count, children, out var obtained) < 0) return;
                for (var i = 0; i < obtained && remaining > 0; i++)
                {
                    if (children[i] is IAccessibleText child) Read(child, depth + 1);
                    else if (children[i] is int childId)
                    {
                        remaining--;
                        name = node.Name(childId);
                        if (!string.IsNullOrWhiteSpace(name) && !texts.Contains(name)) texts.Add(name);
                    }
                }
            }
            Read(root, 0);
        }
        catch (Exception ex) when (ex is ExternalException or InvalidCastException or NotSupportedException)
        {
            // A closing or inaccessible dialog still has its Win32 title/buttons.
        }
    }

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
                else if (IsWindowVisible(child) && TextOf(child) is { Length: > 0 } text && !statics.Contains(text))
                    statics.Add(text);
                return true;
            }, IntPtr.Zero);
        }

        if (className == DialogWindow.DialogClassName && IsWindowVisible(hwnd))
            AddAccessibleText(hwnd, statics);
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
