using System.Collections.Generic;
using QuickerPlaces.Models;
using QuickerPlaces.Services.Theming;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>UI refresh U6: every highlight is readable in both themes.</summary>
public sealed class HighlightPaletteTests
{
    public static IEnumerable<object[]> PresetsAndThemes()
    {
        foreach (var preset in new[] { HighlightPreset.Green, HighlightPreset.Blue, HighlightPreset.Red, HighlightPreset.Cognac })
        {
            yield return new object[] { preset, true };
            yield return new object[] { preset, false };
        }
    }

    [Theory]
    [MemberData(nameof(PresetsAndThemes))]
    public void Presets_KeepTextReadable(HighlightPreset preset, bool dark)
    {
        var colors = HighlightPalette.For(preset, dark, windowsAccent: null);

        Assert.True(ThemeColor.Contrast(colors.OnFill, colors.Fill) >= 4.5, $"On-fill text on {preset} ({(dark ? "dark" : "light")})");
        Assert.True(ThemeColor.Contrast(colors.Text, HighlightPalette.GroundFor(dark)) >= 4.5, $"{preset} text on the ground");
    }

    [Fact]
    public void HullGreen_MatchesTheDesignSystem()
    {
        var dark = HighlightPalette.For(HighlightPreset.Green, dark: true, windowsAccent: null);
        var light = HighlightPalette.For(HighlightPreset.Green, dark: false, windowsAccent: null);

        Assert.Equal("#2A7563", dark.Fill.ToHex());
        Assert.Equal("#33866F", dark.Hover.ToHex());
        Assert.Equal("#7CC7AD", dark.Text.ToHex());
        Assert.Equal("#247CC7AD", dark.Soft.ToHex());
        Assert.Equal("#1F5C4D", light.Fill.ToHex());
        Assert.Equal("#174A3E", light.Hover.ToHex());
        Assert.Equal("#1F6B58", light.Text.ToHex());
        Assert.Equal("#1A1F5C4D", light.Soft.ToHex());
        Assert.Equal(ThemeColor.White, dark.OnFill);
    }

    [Fact]
    public void WindowsPreset_WithoutAnAccent_IsHullGreen()
        => Assert.Equal(HighlightPalette.For(HighlightPreset.Green, true, null),
                        HighlightPalette.For(HighlightPreset.Windows, true, null));

    [Theory]
    [InlineData("#FFD800", true)]   // bright yellow
    [InlineData("#FFD800", false)]
    [InlineData("#0078D4", true)]   // Windows default blue
    [InlineData("#0078D4", false)]
    [InlineData("#1A2B4C", true)]   // navy: too dark as text on the dark ground
    [InlineData("#F4C2C2", false)]  // pastel pink: too light as text on the light ground
    [InlineData("#808080", true)]   // mid grey: black text, and a text colour that needs lightening
    public void FromAccent_IsAlwaysReadable(string accent, bool dark)
    {
        var colors = HighlightPalette.For(HighlightPreset.Windows, dark, ThemeColor.Parse(accent));

        Assert.True(ThemeColor.Contrast(colors.OnFill, colors.Fill) >= 4.5, "text on the fill");
        Assert.True(ThemeColor.Contrast(colors.Text, HighlightPalette.GroundFor(dark)) >= 4.5, "text on the ground");
        Assert.Equal(255, colors.Fill.A);
    }

    [Fact]
    public void FromAccent_UsesBlackTextOnALightAccent()
        => Assert.Equal(ThemeColor.Black, HighlightPalette.For(HighlightPreset.Windows, true, ThemeColor.Parse("#FFD800")).OnFill);
}
