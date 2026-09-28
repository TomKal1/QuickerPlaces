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

        SwapPalette(dark ? DarkPalette : LightPalette);

#pragma warning disable WPF0001 // ThemeMode is marked experimental; here it only drives the title bar and the Fluent menus.
        _app.ThemeMode = dark ? ThemeMode.Dark : ThemeMode.Light;
#pragma warning restore WPF0001

        var colors = HighlightPalette.For(highlight, dark,
            highlight == HighlightPreset.Windows ? ReadWindowsAccent() : null);
        SetBrush("Highlight", colors.Fill);
        SetBrush("Highlight.Hover", colors.Hover);
        SetBrush("Highlight.Text", colors.Text);
        SetBrush("Highlight.Soft", colors.Soft);
        SetBrush("On.Highlight", colors.OnFill);
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
        // Raised on a system-events thread; the resources belong to the UI thread.
        if (Theme == AppTheme.System || Highlight == HighlightPreset.Windows)
            _app.Dispatcher.BeginInvoke(() => Apply(Theme, Highlight));
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
