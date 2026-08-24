using System.Windows;
using System.Windows.Controls;
using SmoothFolder.Models;
using SmoothFolder.Native;
using SmoothFolder.Services;

namespace SmoothFolder.Views;

public partial class GlassTintDialog : Window
{
    private bool _ready;

    public GlassTintDialog(FolderConfig folder)
    {
        InitializeComponent();

        HexBox.Text = GlassAppearanceService.NormalizeHex(folder.GlassTint);
        OpacitySlider.Value = Math.Clamp(folder.GlassOpacity, 0.12, 0.72);
        _ready = true;
        UpdatePreview();

        SourceInitialized += (_, _) => WindowEffects.ApplyPopupEffects(this, 26);
    }

    public string SelectedTint => GlassAppearanceService.NormalizeHex(HexBox.Text);
    public double SelectedOpacity => OpacitySlider.Value;

    private void Preset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string color })
            HexBox.Text = color;
    }

    private void HexBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_ready)
            UpdatePreview();
    }

    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_ready)
            UpdatePreview();
    }

    private void UpdatePreview()
    {
        var brush = GlassAppearanceService.CreateTintBrush(HexBox.Text, OpacitySlider.Value);
        DialogCard.Background = brush;
        Preview.Background = GlassAppearanceService.CreateTintBrush(HexBox.Text, OpacitySlider.Value, 1.15);
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
