using System;
using QuickerPlaces.Services.Theming;
using Xunit;

namespace QuickerPlaces.Tests;

public sealed class ThemeColorTests
{
    [Fact]
    public void Parse_ReadsRgbAndArgb()
    {
        Assert.Equal(new ThemeColor(255, 0x12, 0x15, 0x14), ThemeColor.Parse("#121514"));
        Assert.Equal(new ThemeColor(0x24, 0x7C, 0xC7, 0xAD), ThemeColor.Parse("#247cc7ad"));
    }

    [Theory]
    [InlineData("121514")]
    [InlineData("#12151")]
    [InlineData("#GG1514")]
    [InlineData("")]
    [InlineData("#12151 ")] // trailing whitespace must not be tolerated (AllowHexSpecifier, not HexNumber)
    public void Parse_RejectsOtherShapes(string text)
        => Assert.Throws<FormatException>(() => ThemeColor.Parse(text));

    [Fact]
    public void ToHex_RoundTrips()
    {
        Assert.Equal("#1F5C4D", ThemeColor.Parse("#1f5c4d").ToHex());
        Assert.Equal("#1A1F5C4D", ThemeColor.Parse("#1A1F5C4D").ToHex());
    }

    [Fact]
    public void Contrast_MatchesWcagReferencePoints()
    {
        Assert.Equal(21.0, ThemeColor.Contrast(ThemeColor.White, ThemeColor.Black), 2);
        Assert.Equal(1.0, ThemeColor.Contrast(ThemeColor.White, ThemeColor.White), 2);
        // #767676 on white is the classic 4.54:1.
        Assert.Equal(4.54, ThemeColor.Contrast(ThemeColor.Parse("#767676"), ThemeColor.White), 2);
    }

    [Fact]
    public void Mix_MovesTowardTheOtherColour()
    {
        var half = ThemeColor.Black.Mix(ThemeColor.White, 0.5);
        Assert.Equal(new ThemeColor(255, 128, 128, 128), half);
        Assert.Equal(ThemeColor.Black, ThemeColor.Black.Mix(ThemeColor.White, 0));
        Assert.Equal(ThemeColor.White, ThemeColor.Black.Mix(ThemeColor.White, 1));
    }
}
