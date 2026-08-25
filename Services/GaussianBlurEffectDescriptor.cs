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

    private readonly object _diagnosticsSync =
        new();

    private readonly List<string> _diagnostics =
        new();

    private string _name =
        "SmoothFolderGaussianBlur";

    public string Name
    {
        get => _name;
        set => _name = value;
    }

    public IGraphicsEffectSource? Source { get; set; }

    public float BlurAmount { get; set; } = 3.0f;

    public Guid EffectId
    {
        get
        {
            RecordDiagnostic(
                $"EffectId -> {GaussianBlurEffectId}");

            return GaussianBlurEffectId;
        }
    }

    public uint PropertyCount
    {
        get
        {
            RecordDiagnostic(
                "PropertyCount -> 3");

            return 3;
        }
    }

    public uint SourceCount
    {
        get
        {
            RecordDiagnostic(
                "SourceCount -> 1");

            return 1;
        }
    }

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
            RecordDiagnostic(
                $"GetNamedPropertyMapping('{name}') -> " +
                $"index={StandardDeviationProperty}, mapping={mapping}");

            return StandardDeviationProperty;
        }

        if (string.Equals(
                name,
                "Optimization",
                StringComparison.Ordinal))
        {
            RecordDiagnostic(
                $"GetNamedPropertyMapping('{name}') -> " +
                $"index={OptimizationProperty}, mapping={mapping}");

            return OptimizationProperty;
        }

        if (string.Equals(
                name,
                "BorderMode",
                StringComparison.Ordinal))
        {
            RecordDiagnostic(
                $"GetNamedPropertyMapping('{name}') -> " +
                $"index={BorderModeProperty}, mapping={mapping}");

            return BorderModeProperty;
        }

        mapping =
            GraphicsEffectPropertyMapping.Unknown;

        RecordDiagnostic(
            $"GetNamedPropertyMapping('{name}') -> unknown");

        throw new ArgumentException(
            $"Unknown graphics-effect property '{name}'.",
            nameof(name));
    }

    public object GetProperty(uint index)
    {
        switch (index)
        {
            case StandardDeviationProperty:
                RecordDiagnostic(
                    $"GetProperty({index}) -> " +
                    $"StandardDeviation={BlurAmount:0.###}");

                return PropertyValue.CreateSingle(
                    BlurAmount);

            case OptimizationProperty:
                RecordDiagnostic(
                    $"GetProperty({index}) -> " +
                    $"Optimization={OptimizationQuality}");

                return PropertyValue.CreateUInt32(
                    OptimizationQuality);

            case BorderModeProperty:
                RecordDiagnostic(
                    $"GetProperty({index}) -> " +
                    $"BorderMode={BorderModeHard}");

                return PropertyValue.CreateUInt32(
                    BorderModeHard);

            default:
                RecordDiagnostic(
                    $"GetProperty({index}) -> out of range");

                throw new ArgumentOutOfRangeException(
                    nameof(index));
        }
    }

    public IGraphicsEffectSource GetSource(uint index)
    {
        if (index == 0 &&
            Source is not null)
        {
            RecordDiagnostic(
                "GetSource(0) -> Backdrop source");

            return Source;
        }

        if (index == 0)
        {
            RecordDiagnostic(
                "GetSource(0) -> source is null");

            throw new InvalidOperationException(
                "Gaussian blur source has not been assigned.");
        }

        RecordDiagnostic(
            $"GetSource({index}) -> out of range");

        throw new ArgumentOutOfRangeException(
            nameof(index));
    }

    public string TakeDiagnostics()
    {
        lock (_diagnosticsSync)
        {
            if (_diagnostics.Count == 0)
                return "(no descriptor callbacks recorded)";

            var result =
                string.Join(
                    " | ",
                    _diagnostics);

            _diagnostics.Clear();

            return result;
        }
    }

    private void RecordDiagnostic(
        string message)
    {
        lock (_diagnosticsSync)
        {
            // Effect-factory creation is a one-time operation, but keep this
            // bounded in case a future Windows build queries properties more
            // aggressively.
            if (_diagnostics.Count < 48)
                _diagnostics.Add(message);
        }
    }
}
