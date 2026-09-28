using QuickerPlaces.Models;

namespace QuickerPlaces.Services.Theming;

/// <summary>The five colours a highlight needs, as the Design System names them.</summary>
public sealed record HighlightColors(ThemeColor Fill, ThemeColor Hover, ThemeColor Text, ThemeColor Soft, ThemeColor OnFill);

/// <summary>
/// The highlight presets from the QuickerPlaces Design System (version 2),
/// and the rule that makes the Windows accent colour safe to use (UI
/// refresh U6). The ground colours must match Bg.Base in the palette files;
/// PaletteFileTests checks that.
/// </summary>
public static class HighlightPalette
{
    public static readonly ThemeColor DarkGround = ThemeColor.Parse("#121514");
    public static readonly ThemeColor LightGround = ThemeColor.Parse("#ECEEED");

    private const byte DarkSoftAlpha = 0x24;  // 14%
    private const byte LightSoftAlpha = 0x1A; // 10%
    private const double MinimumContrast = 4.5;

    public static ThemeColor GroundFor(bool dark) => dark ? DarkGround : LightGround;

    public static HighlightColors For(HighlightPreset preset, bool dark, ThemeColor? windowsAccent) => preset switch
    {
        HighlightPreset.Blue => dark
            ? Preset("#36679A", "#4077AE", "#9CC0E6", dark)
            : Preset("#2F5D8A", "#264D73", "#2C5A86", dark),
        HighlightPreset.Red => dark
            ? Preset("#B1372F", "#C4453B", "#F08F84", dark)
            : Preset("#A8322B", "#8F2923", "#A8322B", dark),
        HighlightPreset.Cognac => dark
            ? Preset("#8F5A33", "#A0683E", "#DCAA7D", dark)
            : Preset("#8A5530", "#734526", "#86522E", dark),
        HighlightPreset.Windows when windowsAccent is { } accent => FromAccent(accent, dark),
        _ => dark
            // Hover is #32846E, not the Design System's #33866F: that value
            // only reaches 4.39:1 against the white on-fill text Button.Primary
            // puts on it, so it's nudged one step darker on the fill-to-hover
            // line to clear 4.5:1 (see HullGreen_MatchesTheDesignSystem).
            ? Preset("#2A7563", "#32846E", "#7CC7AD", dark)
            : Preset("#1F5C4D", "#174A3E", "#1F6B58", dark)
    };

    /// <summary>
    /// Turns any accent into a readable highlight: white text if the accent
    /// itself gives white text at least 4.5:1 (so Windows' own blue,
    /// #0078D4, keeps the white text Windows shows for it), otherwise
    /// whichever of black/white contrasts more; the fill is then pushed
    /// away from that text until it reaches 4.5:1 too (a guard that in
    /// practice never takes a step, since the better of black/white always
    /// clears 4.5:1 against any colour); and a text colour is pushed
    /// toward the theme's own text until it reaches 4.5:1 on the ground.
    /// </summary>
    public static HighlightColors FromAccent(ThemeColor accent, bool dark)
    {
        var fill = accent.WithAlpha(255);
        var whiteContrast = ThemeColor.Contrast(fill, ThemeColor.White);
        var onFill = whiteContrast >= MinimumContrast
            ? ThemeColor.White
            : whiteContrast >= ThemeColor.Contrast(fill, ThemeColor.Black) ? ThemeColor.White : ThemeColor.Black;
        var awayFromText = onFill == ThemeColor.White ? ThemeColor.Black : ThemeColor.White;
        // Guard only (see the doc comment): this normally takes no steps.
        fill = PushUntil(fill, awayFromText, onFill);

        var ground = GroundFor(dark);
        var text = PushUntil(accent.WithAlpha(255), dark ? ThemeColor.White : ThemeColor.Black, ground);

        var hover = fill.Mix(awayFromText, 0.12);
        var soft = dark ? text.WithAlpha(DarkSoftAlpha) : fill.WithAlpha(LightSoftAlpha);
        return new HighlightColors(fill, hover, text, soft, onFill);
    }

    private static HighlightColors Preset(string fill, string hover, string text, bool dark)
    {
        var fillColor = ThemeColor.Parse(fill);
        var textColor = ThemeColor.Parse(text);
        var soft = dark ? textColor.WithAlpha(DarkSoftAlpha) : fillColor.WithAlpha(LightSoftAlpha);
        return new HighlightColors(fillColor, ThemeColor.Parse(hover), textColor, soft, ThemeColor.White);
    }

    /// <summary>Moves <paramref name="color"/> 5% at a time toward <paramref name="toward"/> until it contrasts 4.5:1 with <paramref name="against"/>.</summary>
    private static ThemeColor PushUntil(ThemeColor color, ThemeColor toward, ThemeColor against)
    {
        for (var step = 0; step < 60 && ThemeColor.Contrast(color, against) < MinimumContrast; step++)
            color = color.Mix(toward, 0.05);
        return color;
    }
}
