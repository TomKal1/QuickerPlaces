using System;
using System.Windows;
using System.Windows.Input;
using QuickerPlaces.Models;
using QuickerPlaces.Services;

namespace QuickerPlaces.Views;

/// <summary>
/// App settings. Appearance choices are previewed at once through the
/// caller's <c>preview</c> callback; the caller reverts them if the dialog
/// is cancelled.
///
/// The global hotkey is recorded by pressing it. The dialog
/// doesn't register anything itself: on Save it hands the chosen text to
/// the caller's <c>tryApply</c> callback (MainWindow, which owns the
/// registration) and only closes if that succeeds, so a combination
/// another app already owns is reported inline instead of being saved.
/// </summary>
public partial class SettingsDialog : Window
{
    private const string Disabled = "None";

    private readonly Func<SettingsChoice, string?> _tryApply;
    private readonly Action<AppTheme, HighlightPreset> _preview;
    private readonly bool _ready;
    private readonly Action<Window>? _showRecentFiles;
    private readonly Action<Window>? _showFolderActivity;
    private readonly Action<Window>? _showRevit;
    private readonly Action? _customiseLayout;
    private string _hotkeyText;
    private bool _customiseRequested;

    private SettingsDialog(Window owner, string? currentHotkey, bool minimizeToTray, bool startWithWindows,
        AppTheme theme, HighlightPreset highlight, Action<AppTheme, HighlightPreset> preview,
        Func<SettingsChoice, string?> tryApply, Action<Window>? showRecentFiles, Action? customiseLayout, Action<Window>? showFolderActivity,
        Action<Window>? showRevit)
    {
        InitializeComponent();

        Owner = owner;
        // SizeToContent respects MaxHeight: on a short screen the window
        // stops at the work area and the sections scroll instead of pushing
        // Save off the bottom.
        MaxHeight = SystemParameters.WorkArea.Height;
        _tryApply = tryApply;
        _preview = preview;
        _showRecentFiles = showRecentFiles;
        _showFolderActivity = showFolderActivity;
        _showRevit = showRevit;
        _customiseLayout = customiseLayout;
        WorkspaceSection.Visibility = showRecentFiles is null && customiseLayout is null && showFolderActivity is null && showRevit is null ? Visibility.Collapsed : Visibility.Visible;
        RecentFilesButton.Visibility = showRecentFiles is null ? Visibility.Collapsed : Visibility.Visible;
        FolderActivityButton.Visibility = showFolderActivity is null ? Visibility.Collapsed : Visibility.Visible;
        RevitButton.Visibility = showRevit is null ? Visibility.Collapsed : Visibility.Visible;
        CustomiseLayoutButton.Visibility = CustomiseLayoutHint.Visibility = customiseLayout is null ? Visibility.Collapsed : Visibility.Visible;
        _hotkeyText = HotkeyGesture.IsDisabled(currentHotkey) ? Disabled : currentHotkey!.Trim();
        ShowHotkey(_hotkeyText);
        MinimizeToTrayCheck.IsChecked = minimizeToTray;
        StartWithWindowsCheck.IsChecked = startWithWindows;

        ThemeButton(theme).IsChecked = true;
        HighlightButton(highlight).IsChecked = true;
        // Checked fires while the choices above are set; only the user's
        // changes should preview.
        _ready = true;

        Loaded += (_, _) => HotkeyBox.Focus();
    }

    /// <summary>The applied choices, set only when Save succeeded.</summary>
    public SettingsChoice? SavedSettings { get; private set; }

    /// <summary>
    /// Shows the dialog. <paramref name="preview"/> applies an appearance
    /// choice at once; the caller reverts it if this returns null (Cancel,
    /// Esc or the title bar's close button).
    /// <paramref name="tryApply"/> is called with the choices on Save and
    /// returns an error message, or null once applied. Returns the saved
    /// choices, or null if cancelled.
    /// <paramref name="showRecentFiles"/> (given the dialog as owner) and
    /// <paramref name="customiseLayout"/> are the workspace's own settings;
    /// the layout one runs once this has saved and closed.
    /// <paramref name="showFolderActivity"/> opens the independently saved folder tracking scope.
    /// <paramref name="showRevit"/> opens the independently saved Revit settings.
    /// </summary>
    public static SettingsChoice? Show(Window owner, string? currentHotkey, bool minimizeToTray, bool startWithWindows,
        AppTheme theme, HighlightPreset highlight, Action<AppTheme, HighlightPreset> preview,
        Func<SettingsChoice, string?> tryApply, Action<Window>? showRecentFiles = null, Action? customiseLayout = null,
        Action<Window>? showFolderActivity = null, Action<Window>? showRevit = null)
    {
        var dialog = new SettingsDialog(owner, currentHotkey, minimizeToTray, startWithWindows, theme, highlight, preview, tryApply,
            showRecentFiles, customiseLayout, showFolderActivity, showRevit);
        dialog.ShowDialog();
        if (dialog.SavedSettings is not null && dialog._customiseRequested)
            customiseLayout?.Invoke();
        return dialog.SavedSettings;
    }

    private void Appearance_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready)
            return;

        try
        {
            _preview(SelectedTheme(), SelectedHighlight());
        }
        catch (Exception ex)
        {
            // A preview that fails leaves the current look and the dialog
            // open; Save applies the choice again and reports any failure.
            // No message text: it could carry a path.
            DiagnosticLog.Warn($"Could not preview the appearance choice ({ex.GetType().Name}, 0x{ex.HResult:X8}).");
        }
    }

    private System.Windows.Controls.RadioButton ThemeButton(AppTheme theme) => theme switch
    {
        AppTheme.Light => ThemeLight,
        AppTheme.System => ThemeSystem,
        _ => ThemeDark
    };

    private System.Windows.Controls.RadioButton HighlightButton(HighlightPreset preset) => preset switch
    {
        HighlightPreset.Blue => HighlightBlue,
        HighlightPreset.Red => HighlightRed,
        HighlightPreset.Cognac => HighlightCognac,
        HighlightPreset.Windows => HighlightWindows,
        _ => HighlightGreen
    };

    private AppTheme SelectedTheme()
        => ThemeLight.IsChecked == true ? AppTheme.Light
         : ThemeSystem.IsChecked == true ? AppTheme.System
         : AppTheme.Dark;

    private HighlightPreset SelectedHighlight()
        => HighlightBlue.IsChecked == true ? HighlightPreset.Blue
         : HighlightRed.IsChecked == true ? HighlightPreset.Red
         : HighlightCognac.IsChecked == true ? HighlightPreset.Cognac
         : HighlightWindows.IsChecked == true ? HighlightPreset.Windows
         : HighlightPreset.Green;

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

    private void SaveButton_Click(object sender, RoutedEventArgs e) => Save();

    private void CustomiseLayout_Click(object sender, RoutedEventArgs e)
    {
        _customiseRequested = true;
        Save();
        // Saving failed (the error is shown): stay here.
        if (SavedSettings is null)
            _customiseRequested = false;
    }

    private void RecentFiles_Click(object sender, RoutedEventArgs e) => _showRecentFiles?.Invoke(this);

    private void FolderActivity_Click(object sender, RoutedEventArgs e) => _showFolderActivity?.Invoke(this);

    private void Revit_Click(object sender, RoutedEventArgs e) => _showRevit?.Invoke(this);

    private void Save()
    {
        var choice = new SettingsChoice(_hotkeyText,
            MinimizeToTrayCheck.IsChecked == true, StartWithWindowsCheck.IsChecked == true,
            SelectedTheme(), SelectedHighlight());
        var error = _tryApply(choice);
        if (error is not null)
        {
            ShowError(error);
            return;
        }

        SavedSettings = choice;
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

public sealed record SettingsChoice(string Hotkey, bool MinimizeToTray, bool StartWithWindows,
    AppTheme Theme, HighlightPreset Highlight);
