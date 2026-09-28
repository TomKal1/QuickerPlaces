using System;
using System.Globalization;

namespace QuickerPlaces.Services.Theming;

/// <summary>
/// A colour as the theme code reasons about it, free of System.Windows so the
/// contrast rules can be unit-tested (D5). Parses WPF's "#RRGGBB" and
/// "#AARRGGBB" forms; contrast follows WCAG 2 and ignores alpha.
/// </summary>
public readonly record struct ThemeColor(byte A, byte R, byte G, byte B)
{
    public static readonly ThemeColor White = new(255, 255, 255, 255);
    public static readonly ThemeColor Black = new(255, 0, 0, 0);

    public static ThemeColor FromRgb(byte r, byte g, byte b) => new(255, r, g, b);

    public static ThemeColor Parse(string text)
    {
        if (text is null || text.Length is not (7 or 9) || text[0] != '#')
            throw new FormatException($"Expected #RRGGBB or #AARRGGBB, got \"{text}\".");

        if (!uint.TryParse(text.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
            throw new FormatException($"\"{text}\" is not a hex colour.");

        return text.Length == 7
            ? new ThemeColor(255, (byte)(value >> 16), (byte)(value >> 8), (byte)value)
            : new ThemeColor((byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value);
    }

    public string ToHex() => A == 255 ? $"#{R:X2}{G:X2}{B:X2}" : $"#{A:X2}{R:X2}{G:X2}{B:X2}";

    public ThemeColor WithAlpha(byte alpha) => this with { A = alpha };

    /// <summary>WCAG relative luminance, 0 (black) to 1 (white).</summary>
    public double RelativeLuminance => 0.2126 * Channel(R) + 0.7152 * Channel(G) + 0.0722 * Channel(B);

    /// <summary>WCAG contrast ratio, 1 to 21.</summary>
    public static double Contrast(ThemeColor a, ThemeColor b)
    {
        var (light, dark) = a.RelativeLuminance >= b.RelativeLuminance
            ? (a.RelativeLuminance, b.RelativeLuminance)
            : (b.RelativeLuminance, a.RelativeLuminance);
        return (light + 0.05) / (dark + 0.05);
    }

    /// <summary>An opaque colour <paramref name="amount"/> (0 to 1) of the way to <paramref name="other"/>.</summary>
    public ThemeColor Mix(ThemeColor other, double amount) => new(
        255,
        Lerp(R, other.R, amount),
        Lerp(G, other.G, amount),
        Lerp(B, other.B, amount));

    private static byte Lerp(byte from, byte to, double amount)
        => (byte)Math.Round(from + (to - from) * Math.Clamp(amount, 0, 1), MidpointRounding.AwayFromZero);

    private static double Channel(byte value)
    {
        var c = value / 255.0;
        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }
}
