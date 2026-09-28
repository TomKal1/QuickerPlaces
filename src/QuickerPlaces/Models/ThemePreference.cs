using System;

namespace QuickerPlaces.Models;

/// <summary>Which palette the app uses. System follows the Windows app theme.</summary>
public enum AppTheme
{
    Dark,
    Light,
    System
}

/// <summary>
/// The user's highlight colour (the Design System's presets). Windows
/// follows the Windows accent colour, made readable by HighlightPalette.
/// </summary>
public enum HighlightPreset
{
    Green,
    Blue,
    Red,
    Cognac,
    Windows
}

/// <summary>
/// Reads and writes the theme and highlight strings in settings.json (UI
/// refresh U7). Tolerant like PlaceSort.Parse: anything unrecognised is the
/// default, so a hand-edited value can never cost the other settings.
/// </summary>
public static class ThemePreference
{
    public static AppTheme ParseTheme(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "light" => AppTheme.Light,
        "system" => AppTheme.System,
        _ => AppTheme.Dark
    };

    public static HighlightPreset ParseHighlight(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "blue" => HighlightPreset.Blue,
        "red" => HighlightPreset.Red,
        "cognac" => HighlightPreset.Cognac,
        "windows" => HighlightPreset.Windows,
        _ => HighlightPreset.Green
    };

    public static string Format(AppTheme theme) => theme switch
    {
        AppTheme.Light => "light",
        AppTheme.System => "system",
        _ => "dark"
    };

    public static string Format(HighlightPreset preset) => preset switch
    {
        HighlightPreset.Blue => "blue",
        HighlightPreset.Red => "red",
        HighlightPreset.Cognac => "cognac",
        HighlightPreset.Windows => "windows",
        _ => "green"
    };
}
