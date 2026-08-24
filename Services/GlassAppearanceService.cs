using System.Globalization;
using System.Windows.Media;

namespace SmoothFolder.Services;

public static class GlassAppearanceService
{
    public const string DefaultTint = "#3C5064";
    public const double DefaultOpacity = 0.42;

    public static SolidColorBrush CreateTintBrush(string? hex, double opacity, double opacityScale = 1.0)
    {
        var color = ParseColor(hex);
        var alpha = (byte)Math.Round(Math.Clamp(opacity * opacityScale, 0.0, 1.0) * 255.0);
        return new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
    }

    public static Color ParseColor(string? hex)
    {
        var normalized = NormalizeHex(hex);

        if (ColorConverter.ConvertFromString(normalized) is Color color)
            return Color.FromRgb(color.R, color.G, color.B);

        return Color.FromRgb(0x3C, 0x50, 0x64);
    }

    public static string NormalizeHex(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return DefaultTint;

        var value = hex.Trim();
        if (!value.StartsWith('#'))
            value = "#" + value;

        if (value.Length != 7)
            return DefaultTint;

        return int.TryParse(value[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _)
            ? value.ToUpperInvariant()
            : DefaultTint;
    }
}
