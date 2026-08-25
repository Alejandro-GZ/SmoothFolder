using Windows.Foundation;
using Windows.Graphics.Effects;
using Windows.Graphics.Effects.Interop;

namespace SmoothFolder.Services;

/// <summary>
/// Native Direct2D Saturation effect descriptor used as the color stage after
/// SmoothFolder's Gaussian blur. Direct2D exposes one FLOAT property:
/// D2D1_SATURATION_PROP_SATURATION = 0.
/// </summary>
internal sealed class SaturationEffectDescriptor :
    IGraphicsEffect,
    IGraphicsEffectSource,
    IGraphicsEffectD2D1Interop
{
    // CLSID_D2D1Saturation
    private static readonly Guid SaturationEffectId =
        new("5CB2D9CF-327D-459F-A0CE-40C0B2086BF7");

    private const uint SaturationProperty = 0;

    private string _name =
        "SmoothFolderSaturation";

    public string Name
    {
        get => _name;
        set => _name = value;
    }

    public IGraphicsEffectSource? Source { get; set; }

    public float Saturation { get; set; } = 1.0f;

    public Guid EffectId =>
        SaturationEffectId;

    public uint PropertyCount =>
        1;

    public uint SourceCount =>
        1;

    public uint GetNamedPropertyMapping(
        string name,
        out GraphicsEffectPropertyMapping mapping)
    {
        if (string.Equals(
                name,
                nameof(Saturation),
                StringComparison.Ordinal))
        {
            mapping =
                GraphicsEffectPropertyMapping.Direct;

            return SaturationProperty;
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
            SaturationProperty =>
                PropertyValue.CreateSingle(
                    Saturation),

            _ => throw new ArgumentOutOfRangeException(
                nameof(index))
        };

    public IGraphicsEffectSource GetSource(uint index) =>
        index switch
        {
            0 when Source is not null => Source,

            0 => throw new InvalidOperationException(
                "Saturation source has not been assigned."),

            _ => throw new ArgumentOutOfRangeException(
                nameof(index))
        };
}
