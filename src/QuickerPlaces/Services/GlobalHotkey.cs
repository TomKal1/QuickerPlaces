using System;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using QuickerPlaces.Models;

namespace QuickerPlaces.Services;

/// <summary>
/// A system-wide hotkey registered with Win32 RegisterHotKey against a
/// window's HWND. Windows posts WM_HOTKEY to that window whenever the key
/// is pressed, whichever app has focus. The window that receives a hotkey
/// is also allowed to take the foreground, which is what lets
/// <see cref="Pressed"/> handlers bring QuickerPlaces to the front.
/// Dispose (or closing the window) unregisters it.
/// </summary>
public sealed class GlobalHotkey : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const uint ModNoRepeat = 0x4000;
    private const int ErrorHotkeyAlreadyRegistered = 1409;

    // Any value in 0x0000-0xBFFF works for an app; there's only ever one.
    private const int HotkeyId = 0x5150;

    private readonly IntPtr _hwnd;
    private readonly HwndSource _source;
    private bool _disposed;

    private GlobalHotkey(IntPtr hwnd, HwndSource source, HotkeyGesture gesture)
    {
        _hwnd = hwnd;
        _source = source;
        Gesture = gesture;
        _source.AddHook(WndProc);
    }

    /// <summary>The hotkey as registered, for display.</summary>
    public HotkeyGesture Gesture { get; }

    /// <summary>Raised on the UI thread each time the hotkey is pressed.</summary>
    public event Action? Pressed;

    /// <summary>
    /// Registers <paramref name="gesture"/> for <paramref name="window"/>,
    /// creating the window's HWND if it doesn't exist yet. Returns null with
    /// a user-readable <paramref name="error"/> if the key name isn't one
    /// Windows knows, or another app already owns the combination.
    /// </summary>
    public static GlobalHotkey? TryRegister(Window window, HotkeyGesture gesture, out string? error)
    {
        if (!TryGetVirtualKey(gesture.Key, out var virtualKey))
        {
            error = $"\"{gesture.Key}\" isn't a key name Windows recognises. Try a letter, a digit, F1-F12, or Space.";
            return null;
        }

        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        var source = HwndSource.FromHwnd(hwnd);
        if (source is null)
        {
            error = "The main window isn't ready to receive a hotkey.";
            return null;
        }

        if (!RegisterHotKey(hwnd, HotkeyId, (uint)gesture.Modifiers | ModNoRepeat, (uint)virtualKey))
        {
            var code = Marshal.GetLastWin32Error();
            error = code == ErrorHotkeyAlreadyRegistered
                ? $"{gesture} is already in use by another app."
                : $"Windows wouldn't register {gesture}: {new Win32Exception(code).Message}";
            return null;
        }

        error = null;
        return new GlobalHotkey(hwnd, source, gesture);
    }

    /// <summary>
    /// Maps a key name to a Win32 virtual-key code via WPF's KeyConverter
    /// ("Space", "A", "F5", "OemTilde"...). A bare digit means the
    /// top-row number key, since the Key enum spells those "D0".."D9".
    /// </summary>
    private static bool TryGetVirtualKey(string keyName, out int virtualKey)
    {
        virtualKey = 0;
        try
        {
            var name = keyName.Length == 1 && char.IsAsciiDigit(keyName[0]) ? "D" + keyName : keyName;
            if (new KeyConverter().ConvertFromString(null, CultureInfo.InvariantCulture, name) is not Key key || key == Key.None)
                return false;

            virtualKey = KeyInterop.VirtualKeyFromKey(key);
            return virtualKey != 0;
        }
        catch (Exception ex) when (ex is NotSupportedException or ArgumentException or FormatException)
        {
            return false;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            Pressed?.Invoke();
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _source.RemoveHook(WndProc);
        UnregisterHotKey(_hwnd, HotkeyId);
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
