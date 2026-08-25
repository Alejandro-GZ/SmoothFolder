using Windows.Foundation;
using Windows.Graphics.Effects;
using Windows.Graphics.Effects.Interop;

namespace SmoothFolder.Services;

/// <summary>
/// Native Direct2D Gaussian blur descriptor accepted by
/// Windows.UI.Composition.Compositor.CreateEffectFactory.
///
/// D2D1 Gaussian Blur exposes three properties:
///   0 - StandardDeviation
///   1 - Optimization
///   2 - BorderMode
///
/// Composition queries the full native property surface while creating the
/// effect factory, so all three must be exposed even when SmoothFolder only
/// changes the blur amount itself.
/// </summary>
internal sealed class GaussianBlurEffectDescriptor :
    IGraphicsEffect,
    IGraphicsEffectSource,
    IGraphicsEffectD2D1Interop
{
    // CLSID_D2D1GaussianBlur
    private static readonly Guid GaussianBlurEffectId =
        new("1FEB6D69-2FE6-4AC9-8C58-1D7F93E7A6A5");

    private const uint StandardDeviationProperty = 0;
    private const uint OptimizationProperty = 1;
    private const uint BorderModeProperty = 2;

    // D2D1_GAUSSIANBLUR_OPTIMIZATION_QUALITY
    private const uint OptimizationQuality = 2;

    // D2D1_BORDER_MODE_HARD
    private const uint BorderModeHard = 1;

    private string _name =
        "SmoothFolderGaussianBlur";

    public string Name
    {
        get => _name;
        set => _name = value;
    }

    public IGraphicsEffectSource? Source { get; set; }

    public float BlurAmount { get; set; } = 3.0f;

    public Guid EffectId =>
        GaussianBlurEffectId;

    public uint PropertyCount =>
        3;

    public uint SourceCount =>
        1;

    public uint GetNamedPropertyMapping(
        string name,
        out GraphicsEffectPropertyMapping mapping)
    {
        mapping =
            GraphicsEffectPropertyMapping.Direct;

        if (string.Equals(
                name,
                nameof(BlurAmount),
                StringComparison.Ordinal))
        {
            return StandardDeviationProperty;
        }

        if (string.Equals(
                name,
                "Optimization",
                StringComparison.Ordinal))
        {
            return OptimizationProperty;
        }

        if (string.Equals(
                name,
                "BorderMode",
                StringComparison.Ordinal))
        {
            return BorderModeProperty;
        }

        mapping =
            GraphicsEffectPropertyMapping.Unknown;

        throw new ArgumentException(
            $"Unknown graphics-effect property '{name}'.",
            nameof(name));
    }

    public object GetProperty(uint index) =>
        index switch
        {
            StandardDeviationProperty =>
                PropertyValue.CreateSingle(
                    BlurAmount),

            OptimizationProperty =>
                PropertyValue.CreateUInt32(
                    OptimizationQuality),

            BorderModeProperty =>
                PropertyValue.CreateUInt32(
                    BorderModeHard),

            _ => throw new ArgumentOutOfRangeException(
                nameof(index))
        };

    public IGraphicsEffectSource GetSource(uint index) =>
        index switch
        {
            0 when Source is not null => Source,

            0 => throw new InvalidOperationException(
                "Gaussian blur source has not been assigned."),

            _ => throw new ArgumentOutOfRangeException(
                nameof(index))
        };
}
