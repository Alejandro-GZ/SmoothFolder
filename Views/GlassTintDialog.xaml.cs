using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SmoothFolder.Models;
using SmoothFolder.Native;
using SmoothFolder.Services;

namespace SmoothFolder.Views;

public partial class GlassTintDialog : Window
{
    private readonly SettingsService _settings =
        new();

    private GpuGlassBackdropService? _glassBackdrop;
    private bool _ready;

    public GlassTintDialog(
        FolderConfig folder)
    {
        InitializeComponent();

        HexBox.Text =
            GlassAppearanceService.NormalizeHex(
                folder.GlassTint);

        OpacitySlider.Value =
            Math.Clamp(
                folder.GlassOpacity,
                0.12,
                0.72);

        _ready =
            true;

        UpdatePreview();

        SourceInitialized +=
            (_, _) =>
            {
                WindowEffects.ApplyPopupEffects(
                    this,
                    30);

                _glassBackdrop =
                    GpuGlassBackdropService.TryCreate(
                        this,
                        DialogCard,
                        30);

                ApplyDialogMaterial();

                // Unlike the folder popup, this dialog has no open animation
                // that implicitly enables the composition helper. Make the
                // backdrop visible explicitly so it uses the exact same live
                // Gaussian blur pipeline as tiles and folder windows.
                _glassBackdrop?.Show();
                _glassBackdrop?.SynchronizeImmediately();

                UpdatePreview();
            };

        Closed +=
            (_, _) =>
            {
                _glassBackdrop?.Dispose();
                _glassBackdrop =
                    null;
            };
    }

    public string SelectedTint =>
        GlassAppearanceService.NormalizeHex(
            HexBox.Text);

    public double SelectedOpacity =>
        OpacitySlider.Value;

    private void Preset_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is Button
            {
                Tag: string color
            })
        {
            HexBox.Text =
                color;
        }
    }

    private void HexBox_TextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        if (_ready)
            UpdatePreview();
    }

    private void OpacitySlider_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (_ready)
            UpdatePreview();
    }

    private void ApplyDialogMaterial()
    {
        if (DialogTintLayer is null)
            return;

        var appearance =
            _settings.Current.Appearance;

        _glassBackdrop?.UpdateMaterial(
            appearance.BlurAmount,
            appearance.Saturation);

        var opacityScale =
            _glassBackdrop?.IsActive == true
                ? appearance.TintStrength
                : Math.Clamp(
                    appearance.TintStrength *
                    (0.78 / 0.28),
                    0.0,
                    1.0);

        DialogTintLayer.Background =
            GlassAppearanceService.CreateTintBrush(
                "#242A34",
                0.72,
                opacityScale);
    }

    private void UpdatePreview()
    {
        if (PreviewTintLayer is null ||
            OpacityValueText is null ||
            PreviewValueText is null)
        {
            return;
        }

        var tint =
            GlassAppearanceService.NormalizeHex(
                HexBox.Text);

        var strength =
            OpacitySlider.Value;

        PreviewTintLayer.Background =
            GlassAppearanceService.CreateTintBrush(
                tint,
                strength,
                _settings.Current.Appearance
                    .TintStrength);

        OpacityValueText.Text =
            $"{strength * 100:0}%";

        PreviewValueText.Text =
            $"{tint}  •  {strength * 100:0}%";
    }

    private void DragHandle_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (e.ChangedButton !=
            MouseButton.Left)
        {
            return;
        }

        if (WindowDragService.BeginMove(
                this))
        {
            e.Handled =
                true;
        }
    }

    private void Apply_Click(
        object sender,
        RoutedEventArgs e)
    {
        DialogResult =
            true;
    }

    private void Cancel_Click(
        object sender,
        RoutedEventArgs e)
    {
        DialogResult =
            false;
    }
}
