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
        // Button.Primary puts On.Highlight text on Highlight.Hover too, so hover must clear 4.5:1 on its own.
        Assert.True(ThemeColor.Contrast(colors.OnFill, colors.Hover) >= 4.5, $"On-fill text on {preset} hover ({(dark ? "dark" : "light")})");
        Assert.True(ThemeColor.Contrast(colors.Text, HighlightPalette.GroundFor(dark)) >= 4.5, $"{preset} text on the ground");
    }

    [Fact]
    public void HullGreen_MatchesTheDesignSystem()
    {
        var dark = HighlightPalette.For(HighlightPreset.Green, dark: true, windowsAccent: null);
        var light = HighlightPalette.For(HighlightPreset.Green, dark: false, windowsAccent: null);

        Assert.Equal("#2A7563", dark.Fill.ToHex());
        // #33866F (the Design System's stated hover) only reaches 4.39:1 against white
        // on-fill text; #32846E is the lightest point on that same fill-to-hover line
        // that still clears 4.5:1.
        Assert.Equal("#32846E", dark.Hover.ToHex());
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
    [InlineData("#808080", true)]   // mid grey: black text; already 4.65:1 as text on the dark ground, no lightening needed
    public void FromAccent_IsAlwaysReadable(string accent, bool dark)
    {
        var colors = HighlightPalette.For(HighlightPreset.Windows, dark, ThemeColor.Parse(accent));

        Assert.True(ThemeColor.Contrast(colors.OnFill, colors.Fill) >= 4.5, "text on the fill");
        Assert.True(ThemeColor.Contrast(colors.OnFill, colors.Hover) >= 4.5, "text on the hover");
        Assert.True(ThemeColor.Contrast(colors.Text, HighlightPalette.GroundFor(dark)) >= 4.5, "text on the ground");
        Assert.Equal(255, colors.Fill.A);
    }

    [Fact]
    public void FromAccent_UsesBlackTextOnALightAccent()
        => Assert.Equal(ThemeColor.Black, HighlightPalette.For(HighlightPreset.Windows, true, ThemeColor.Parse("#FFD800")).OnFill);

    [Fact]
    public void FromAccent_PrefersWhiteAsSoonAsItReaches45()
        // Windows itself shows white text on its default accent; white already clears
        // 4.5:1 against #0078D4 (4.53:1), even though black would contrast slightly more.
        => Assert.Equal(ThemeColor.White, HighlightPalette.For(HighlightPreset.Windows, true, ThemeColor.Parse("#0078D4")).OnFill);

    /// <summary>
    /// U6 as a sweep, not just the hand-picked cases above: every accent on a
    /// 17-per-channel grid (0, 17, ..., 255 — 4096 colours, including pure
    /// black and white at the ends of the grid) in both themes must keep
    /// on-fill and on-ground text readable. A single Fact with an inner loop
    /// keeps this fast; one xUnit test case per colour would not be.
    /// </summary>
    [Fact]
    public void FromAccent_IsReadableForAnyAccentOnAGrid()
    {
        foreach (var dark in new[] { true, false })
        {
            for (var r = 0; r <= 255; r += 17)
            for (var g = 0; g <= 255; g += 17)
            for (var b = 0; b <= 255; b += 17)
            {
                var accent = ThemeColor.FromRgb((byte)r, (byte)g, (byte)b);
                var colors = HighlightPalette.FromAccent(accent, dark);

                Assert.True(ThemeColor.Contrast(colors.OnFill, colors.Fill) >= 4.5, $"fill for {accent.ToHex()} (dark={dark})");
                Assert.True(ThemeColor.Contrast(colors.OnFill, colors.Hover) >= 4.5, $"hover for {accent.ToHex()} (dark={dark})");
                Assert.True(ThemeColor.Contrast(colors.Text, HighlightPalette.GroundFor(dark)) >= 4.5, $"text for {accent.ToHex()} (dark={dark})");
            }
        }
    }
}
