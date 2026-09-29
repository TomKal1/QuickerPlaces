using QuickerPlaces.Models;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>UI refresh U7: theme and highlight are tolerant strings in settings.json.</summary>
public sealed class ThemePreferenceTests
{
    [Theory]
    [InlineData("dark", AppTheme.Dark)]
    [InlineData("Light", AppTheme.Light)]
    [InlineData(" system ", AppTheme.System)]
    [InlineData(null, AppTheme.Dark)]
    [InlineData("", AppTheme.Dark)]
    [InlineData("purple", AppTheme.Dark)]
    [InlineData("1", AppTheme.Dark)]
    public void ParseTheme_FallsBackToDark(string? value, AppTheme expected)
        => Assert.Equal(expected, ThemePreference.ParseTheme(value));

    [Theory]
    [InlineData("green", HighlightPreset.Green)]
    [InlineData("BLUE", HighlightPreset.Blue)]
    [InlineData("red", HighlightPreset.Red)]
    [InlineData("cognac", HighlightPreset.Cognac)]
    [InlineData("windows", HighlightPreset.Windows)]
    [InlineData(null, HighlightPreset.Green)]
    [InlineData("teal", HighlightPreset.Green)]
    public void ParseHighlight_FallsBackToGreen(string? value, HighlightPreset expected)
        => Assert.Equal(expected, ThemePreference.ParseHighlight(value));

    [Theory]
    [InlineData(AppTheme.Dark, "dark")]
    [InlineData(AppTheme.Light, "light")]
    [InlineData(AppTheme.System, "system")]
    public void FormatTheme_RoundTrips(AppTheme theme, string text)
    {
        Assert.Equal(text, ThemePreference.Format(theme));
        Assert.Equal(theme, ThemePreference.ParseTheme(ThemePreference.Format(theme)));
    }

    [Theory]
    [InlineData(HighlightPreset.Green, "green")]
    [InlineData(HighlightPreset.Blue, "blue")]
    [InlineData(HighlightPreset.Red, "red")]
    [InlineData(HighlightPreset.Cognac, "cognac")]
    [InlineData(HighlightPreset.Windows, "windows")]
    public void FormatHighlight_RoundTrips(HighlightPreset preset, string text)
    {
        Assert.Equal(text, ThemePreference.Format(preset));
        Assert.Equal(preset, ThemePreference.ParseHighlight(ThemePreference.Format(preset)));
    }
}
