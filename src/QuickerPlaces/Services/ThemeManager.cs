using System;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using QuickerPlaces.Models;
using QuickerPlaces.Services.Theming;

namespace QuickerPlaces.Services;

/// <summary>
/// Applies the theme and highlight (UI refresh U1–U6): swaps the palette
/// dictionary App.xaml merges, sets ThemeMode so Windows draws a matching
/// title bar and menus, and writes the five Highlight brushes at the top of
/// Application.Resources, where they win over the palette's defaults. Every
/// view refers to colours with DynamicResource, so open windows repaint.
/// Match Windows and Windows accent follow system changes while running.
/// </summary>
public sealed class ThemeManager : IDisposable
{
    private const string DarkPalette = "Palette.Dark.xaml";
    private const string LightPalette = "Palette.Light.xaml";

    private readonly Application _app;

    // What the last successful Apply put on screen, so a re-apply that would
    // change nothing (most UserPreferenceChanged events) repaints no window.
    private (bool Dark, HighlightColors Colors)? _applied;

    public ThemeManager(Application app)
    {
        _app = app;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    public AppTheme Theme { get; private set; } = AppTheme.Dark;

    public HighlightPreset Highlight { get; private set; } = HighlightPreset.Green;

    public void Apply(AppTheme theme, HighlightPreset highlight)
    {
        Theme = theme;
        Highlight = highlight;
        var dark = theme switch
        {
            AppTheme.Light => false,
            AppTheme.System => WindowsAppsUseDarkTheme(),
            _ => true
        };
        var colors = HighlightPalette.For(highlight, dark,
            highlight == HighlightPreset.Windows ? ReadWindowsAccent() : null);
        if (_applied == (dark, colors))
            return;

        SwapPalette(dark ? DarkPalette : LightPalette);

        // ThemeMode does more than draw the title bar and menus: it applies
        // the implicit Window style to every Window subclass (MainWindow and
        // the dialogs) and supplies the Fluent implicit styles this app
        // doesn't override (ListBox, ListBoxItem, RadioButton, ScrollViewer,
        // TextBlock, MenuItem, ContextMenu...). Removing it would strip every
        // window's background, foreground and font.
#pragma warning disable WPF0001 // ThemeMode is marked experimental.
        _app.ThemeMode = dark ? ThemeMode.Dark : ThemeMode.Light;
#pragma warning restore WPF0001

        SetBrush("Highlight", colors.Fill);
        SetBrush("Highlight.Hover", colors.Hover);
        SetBrush("Highlight.Text", colors.Text);
        SetBrush("Highlight.Soft", colors.Soft);
        SetBrush("On.Highlight", colors.OnFill);
        _applied = (dark, colors);
    }

    public void Dispose() => SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;

    /// <summary>
    /// Replaces the merged palette in place. Found by file name, not index:
    /// setting ThemeMode inserts WPF's own Fluent dictionary into the same
    /// list (U4).
    /// </summary>
    private void SwapPalette(string fileName)
    {
        var merged = _app.Resources.MergedDictionaries;
        for (var i = 0; i < merged.Count; i++)
        {
            var source = merged[i].Source?.OriginalString;
            if (source is null)
                continue;
            if (!source.EndsWith(DarkPalette, StringComparison.OrdinalIgnoreCase) &&
                !source.EndsWith(LightPalette, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!source.EndsWith(fileName, StringComparison.OrdinalIgnoreCase))
                merged[i] = new ResourceDictionary { Source = new Uri($"pack://application:,,,/Resources/{fileName}", UriKind.Absolute) };
            return;
        }

        throw new InvalidOperationException($"App.xaml must merge Resources/{DarkPalette} or Resources/{LightPalette}.");
    }

    private void SetBrush(string key, ThemeColor color)
    {
        var brush = new SolidColorBrush(Color.FromArgb(color.A, color.R, color.G, color.B));
        brush.Freeze();
        _app.Resources[key] = brush;
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        // Subscribed on the STA UI thread, so SystemEvents raises this on that
        // thread; BeginInvoke is a safeguard in case it ever arrives elsewhere.
        if (Theme == AppTheme.System || Highlight == HighlightPreset.Windows)
            _app.Dispatcher.BeginInvoke(ReapplyAfterSystemChange);
    }

    private void ReapplyAfterSystemChange()
    {
        try
        {
            Apply(Theme, Highlight);
        }
        catch (Exception ex)
        {
            // A failed re-apply keeps the current look rather than crashing
            // the app over a Windows setting change. No message text: it
            // could carry a path.
            DiagnosticLog.Warn($"Could not re-apply the theme after a Windows setting changed; keeping the current look ({ex.GetType().Name}, 0x{ex.HResult:X8}).");
        }
    }

    /// <summary>Windows' own "app mode" setting; dark when it can't be read.</summary>
    private static bool WindowsAppsUseDarkTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is not int light || light == 0;
    }

    /// <summary>The Windows accent colour (stored as 0xAABBGGRR), or null when unavailable.</summary>
    private static ThemeColor? ReadWindowsAccent()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
        if (key?.GetValue("AccentColor") is not int value)
            return null;
        var abgr = unchecked((uint)value);
        return ThemeColor.FromRgb((byte)abgr, (byte)(abgr >> 8), (byte)(abgr >> 16));
    }
}
