using System;
using System.Windows;
using System.Windows.Input;
using QuickerPlaces.Models;

namespace QuickerPlaces.Views;

/// <summary>
/// Lets the user record a new global hotkey by pressing it. The dialog
/// doesn't register anything itself: on Save it hands the chosen text to
/// the caller's <c>tryApply</c> callback (MainWindow, which owns the
/// registration) and only closes if that succeeds, so a combination
/// another app already owns is reported inline instead of being saved.
/// </summary>
public partial class SettingsDialog : Window
{
    private const string Disabled = "None";

    private readonly Func<string, string?> _tryApply;
    private string _hotkeyText;

    private SettingsDialog(Window owner, string? currentHotkey, Func<string, string?> tryApply)
    {
        InitializeComponent();

        Owner = owner;
        _tryApply = tryApply;
        _hotkeyText = HotkeyGesture.IsDisabled(currentHotkey) ? Disabled : currentHotkey!.Trim();
        ShowHotkey(_hotkeyText);

        Loaded += (_, _) => HotkeyBox.Focus();
    }

    /// <summary>The saved hotkey text ("Ctrl+Alt+Space", or "None" when turned off). Set only when Save succeeded.</summary>
    public string? SavedHotkey { get; private set; }

    /// <summary>
    /// Shows the dialog. <paramref name="tryApply"/> is called with the
    /// chosen text on Save and returns an error message, or null once the
    /// hotkey is live. Returns the saved text, or null if cancelled.
    /// </summary>
    public static string? Show(Window owner, string? currentHotkey, Func<string, string?> tryApply)
    {
        var dialog = new SettingsDialog(owner, currentHotkey, tryApply);
        dialog.ShowDialog();
        return dialog.SavedHotkey;
    }

    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Alt combinations arrive as Key.System, with the real key in SystemKey.
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;

        // Keep the dialog keyboard-usable: plain Tab moves focus, plain
        // Enter saves and plain Esc cancels (IsDefault/IsCancel).
        if (modifiers == ModifierKeys.None && key is Key.Tab or Key.Enter or Key.Escape)
            return;

        e.Handled = true;

        if (modifiers == ModifierKeys.None && key is Key.Back or Key.Delete)
        {
            SetHotkey(Disabled);
            return;
        }

        // ModifierKeys' Alt/Control/Shift/Windows values are the same flags
        // as HotkeyModifiers (both mirror Win32's MOD_* constants).
        var hotkeyModifiers = (HotkeyModifiers)(int)modifiers;

        if (IsModifierKey(key))
        {
            // Still holding modifiers: show them as a hint, keep the last full choice.
            var held = new HotkeyGesture(hotkeyModifiers, "x").ToString();
            HotkeyBox.Text = held[..^1] + "…";
            return;
        }

        SetHotkey(new HotkeyGesture(hotkeyModifiers, KeyName(key)).ToString());
    }

    private void HotkeyBox_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        // Letting go of the modifiers without pressing a key: show the
        // current choice again instead of the "Ctrl+Alt+…" hint.
        if (Keyboard.Modifiers == ModifierKeys.None)
            ShowHotkey(_hotkeyText);
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e) => SetHotkey(HotkeyGesture.Default);

    private void TurnOffButton_Click(object sender, RoutedEventArgs e) => SetHotkey(Disabled);

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var error = _tryApply(_hotkeyText);
        if (error is not null)
        {
            ShowError(error);
            return;
        }

        SavedHotkey = _hotkeyText;
        DialogResult = true;
    }

    /// <summary>Records <paramref name="text"/> as the choice, showing straight away if it can't work (e.g. Shift+Q).</summary>
    private void SetHotkey(string text)
    {
        _hotkeyText = text;
        ShowHotkey(text);

        if (!HotkeyGesture.IsDisabled(text) && !HotkeyGesture.TryParse(text, out _, out var error))
            ShowError(error!);
        else
            ErrorText.Visibility = Visibility.Collapsed;
    }

    private void ShowHotkey(string text)
        => HotkeyBox.Text = HotkeyGesture.IsDisabled(text) ? "None (turned off)" : text;

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private static bool IsModifierKey(Key key) => key is
        Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or
        Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;

    /// <summary>
    /// A key's name as GlobalHotkey reads it back: top-row digits as "1"
    /// rather than the Key enum's "D1", everything else by its enum name
    /// ("Q", "Space", "F5", "OemTilde"), which WPF's KeyConverter parses.
    /// </summary>
    private static string KeyName(Key key)
        => key is >= Key.D0 and <= Key.D9 ? ((char)('0' + (key - Key.D0))).ToString() : key.ToString();
}
