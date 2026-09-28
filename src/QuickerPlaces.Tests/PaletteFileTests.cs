using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using QuickerPlaces.Models;
using QuickerPlaces.Services.Theming;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>
/// UI refresh U1: the two palette files define the same keys, and their
/// colours keep the contrast the Design System promises. Reads the XAML as
/// XML (copied next to the test assembly), so no WPF is needed.
/// </summary>
public sealed class PaletteFileTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static Dictionary<string, ThemeColor> Load(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Palettes", name);
        return XDocument.Load(path).Root!
            .Elements()
            .Where(e => e.Name.LocalName == "SolidColorBrush")
            .ToDictionary(e => (string)e.Attribute(Xaml + "Key")!, e => ThemeColor.Parse((string)e.Attribute("Color")!));
    }

    private static readonly Dictionary<string, ThemeColor> Dark = Load("Palette.Dark.xaml");
    private static readonly Dictionary<string, ThemeColor> Light = Load("Palette.Light.xaml");

    public static IEnumerable<object[]> Themes() => new[] { new object[] { true }, new object[] { false } };

    private static Dictionary<string, ThemeColor> For(bool dark) => dark ? Dark : Light;

    [Fact]
    public void BothPalettesDefineTheSameKeys()
        => Assert.Equal(Dark.Keys.OrderBy(k => k), Light.Keys.OrderBy(k => k));

    [Theory]
    [MemberData(nameof(Themes))]
    public void TheGroundMatchesHighlightPalette(bool dark)
        => Assert.Equal(HighlightPalette.GroundFor(dark), For(dark)["Bg.Base"]);

    [Theory]
    [MemberData(nameof(Themes))]
    public void TheHighlightDefaultsAreHullGreen(bool dark)
    {
        var green = HighlightPalette.For(HighlightPreset.Green, dark, null);
        var palette = For(dark);
        Assert.Equal(green.Fill, palette["Highlight"]);
        Assert.Equal(green.Hover, palette["Highlight.Hover"]);
        Assert.Equal(green.Text, palette["Highlight.Text"]);
        Assert.Equal(green.Soft, palette["Highlight.Soft"]);
        Assert.Equal(green.OnFill, palette["On.Highlight"]);
        Assert.Equal(green.Fill, palette["Preset.Green"]);
    }

    [Theory]
    [InlineData(true, "Text.Primary", "Bg.Base")]
    [InlineData(true, "Text.Primary", "Bg.Panel")]
    [InlineData(true, "Text.Primary", "Bg.Raised")]
    [InlineData(true, "Text.Primary", "Bg.Sunken")]
    [InlineData(true, "Text.Secondary", "Bg.Base")]
    [InlineData(true, "Text.Secondary", "Bg.Panel")]
    [InlineData(true, "Text.Secondary", "Bg.Raised")]
    [InlineData(true, "Text.Tertiary", "Bg.Base")]
    [InlineData(true, "Danger", "Bg.Base")]
    [InlineData(true, "Signal", "Bg.Base")]
    [InlineData(true, "On.Leather", "Leather")]
    [InlineData(true, "On.Leather.Badge", "Leather.Deep")]
    [InlineData(false, "Text.Primary", "Bg.Base")]
    [InlineData(false, "Text.Primary", "Bg.Panel")]
    [InlineData(false, "Text.Primary", "Bg.Raised")]
    [InlineData(false, "Text.Primary", "Bg.Sunken")]
    [InlineData(false, "Text.Secondary", "Bg.Base")]
    [InlineData(false, "Text.Secondary", "Bg.Panel")]
    [InlineData(false, "Text.Secondary", "Bg.Raised")]
    [InlineData(false, "Text.Tertiary", "Bg.Base")]
    [InlineData(false, "Text.Tertiary", "Bg.Raised")]
    [InlineData(false, "Danger", "Bg.Raised")]
    [InlineData(false, "On.Leather", "Leather")]
    [InlineData(false, "On.Leather.Badge", "Leather.Deep")]
    public void TextKeepsFourPointFiveToOne(bool dark, string text, string ground)
    {
        var palette = For(dark);
        var ratio = ThemeColor.Contrast(palette[text], palette[ground]);
        Assert.True(ratio >= 4.5, $"{text} on {ground} ({(dark ? "dark" : "light")}) is {ratio:0.00}:1");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SignalIsAtLeastThreeToOneOnRaised(bool dark)
    {
        var palette = For(dark);
        Assert.True(ThemeColor.Contrast(palette["Signal"], palette["Bg.Raised"]) >= 3.0);
    }
}
