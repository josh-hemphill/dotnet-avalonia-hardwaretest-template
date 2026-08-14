namespace HardwareTest.Core.Presentation;

/// WCAG 2 relative-luminance contrast for chip / status colors (no UI types).
public static class ContrastMath
{
    public const double WcagAaNormalText = 4.5;

    /// Contrast ratio of two sRGB hex colors (`#RRGGBB` or `RRGGBB`).
    public static double RatioHex(string foregroundHex, string backgroundHex)
    {
        var fg = ParseRgb(foregroundHex);
        var bg = ParseRgb(backgroundHex);
        return Ratio(fg.R, fg.G, fg.B, bg.R, bg.G, bg.B);
    }

    /// Contrast ratio of two sRGB triples (0–255).
    public static double Ratio(int r1, int g1, int b1, int r2, int g2, int b2)
    {
        var lighter = Math.Max(RelativeLuminance(r1, g1, b1), RelativeLuminance(r2, g2, b2));
        var darker = Math.Min(RelativeLuminance(r1, g1, b1), RelativeLuminance(r2, g2, b2));
        return (lighter + 0.05) / (darker + 0.05);
    }

    public static double RelativeLuminance(int r, int g, int b)
        => (0.2126 * Channel(r)) + (0.7152 * Channel(g)) + (0.0722 * Channel(b));

    public static (int R, int G, int B) ParseRgb(string hex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hex);
        var value = hex.Trim();
        if (value.StartsWith('#') || value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            value = value[0] == '#' ? value[1..] : value[2..];
        }

        if (value.Length != 6)
        {
            throw new ArgumentException($"Expected RRGGBB hex, got '{hex}'.", nameof(hex));
        }

        return (
            Convert.ToInt32(value[..2], 16),
            Convert.ToInt32(value[2..4], 16),
            Convert.ToInt32(value[4..6], 16));
    }

    private static double Channel(int c)
    {
        var s = Math.Clamp(c, 0, 255) / 255d;
        return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
    }
}
