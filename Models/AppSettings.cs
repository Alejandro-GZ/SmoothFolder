namespace SmoothFolder.Models;

/// <summary>
/// User-level SmoothFolder preferences that are independent from the desktop
/// folder model stored in config.json.
/// </summary>
public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 1;

    public AppearanceSettings Appearance { get; set; } =
        new();
}

public sealed class AppearanceSettings
{
    public const double DefaultBlurAmount = 3.0;
    public const double DefaultTintStrength = 0.28;
    public const double DefaultSaturation = 1.0;

    public double BlurAmount { get; set; } =
        DefaultBlurAmount;

    public double TintStrength { get; set; } =
        DefaultTintStrength;

    public double Saturation { get; set; } =
        DefaultSaturation;
}
