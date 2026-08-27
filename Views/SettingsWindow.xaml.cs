using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using SmoothFolder.Models;
using SmoothFolder.Native;
using SmoothFolder.Services;

namespace SmoothFolder.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsService _settings;
    private readonly DispatcherTimer _saveTimer;
    private GpuGlassBackdropService? _glassBackdrop;
    private bool _initializing;

    public SettingsWindow(
        SettingsService settings)
    {
        // Slider.ValueChanged can fire during InitializeComponent. Initialize
        // dependencies and enable the guard before constructing the XAML tree.
        _settings =
            settings;

        _initializing =
            true;

        InitializeComponent();

        _saveTimer =
            new DispatcherTimer
            {
                Interval =
                    TimeSpan.FromMilliseconds(
                        220)
            };

        _saveTimer.Tick +=
            (_, _) =>
            {
                _saveTimer.Stop();
                SaveSettings();
            };

        SourceInitialized +=
            (_, _) =>
            {
                WindowEffects.ApplyPopupEffects(
                    this,
                    30);

                _glassBackdrop =
                    GpuGlassBackdropService.TryCreate(
                        this,
                        SettingsCard,
                        30);

                ApplyWindowMaterial();

                _glassBackdrop?.Show();
                _glassBackdrop?.SynchronizeImmediately();
            };

        Closing +=
            OnClosing;

        Closed +=
            (_, _) =>
            {
                _glassBackdrop?.Dispose();
                _glassBackdrop =
                    null;
            };

        LoadCurrentSettings();
    }

    private void LoadCurrentSettings()
    {
        _initializing =
            true;

        try
        {
            var appearance =
                _settings.Current.Appearance;

            BlurSlider.Value =
                appearance.BlurAmount;

            TintSlider.Value =
                appearance.TintStrength;

            SaturationSlider.Value =
                appearance.Saturation;

            StartWithWindowsToggle.IsChecked =
                StartupService.IsEnabled();

            RefreshAppearanceLabels();

            ApplyWindowMaterial();
        }
        finally
        {
            _initializing =
                false;
        }
    }

    private void AppearanceSlider_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        RefreshAppearanceLabels();

        if (_initializing)
            return;

        _settings.UpdateAppearance(
            BlurSlider.Value,
            TintSlider.Value,
            SaturationSlider.Value);

        ApplyWindowMaterial();
        RestartSaveTimer();
    }

    private void ApplyWindowMaterial()
    {
        if (SettingsTintLayer is null)
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

        SettingsTintLayer.Background =
            GlassAppearanceService.CreateTintBrush(
                "#242A34",
                0.72,
                opacityScale);

        _glassBackdrop?.SynchronizeImmediately();
    }

    private void RefreshAppearanceLabels()
    {
        if (BlurValueText is null ||
            TintValueText is null ||
            SaturationValueText is null)
        {
            return;
        }

        BlurValueText.Text =
            $"{BlurSlider.Value:0.0}";

        TintValueText.Text =
            $"{TintSlider.Value * 100:0}%";

        SaturationValueText.Text =
            $"{SaturationSlider.Value * 100:0}%";
    }

    private void RestartSaveTimer()
    {
        _saveTimer.Stop();
        _saveTimer.Start();

    }

    private void SaveSettings()
    {
        try
        {
            _settings.Save();
        }
        catch (Exception ex)
        {
            CrashLogService.Log(
                ex,
                "Saving SmoothFolder settings");

        }
    }

    private void StartWithWindowsToggle_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_initializing)
            return;

        try
        {
            StartupService.SetEnabled(
                StartWithWindowsToggle.IsChecked ==
                true);

            StartWithWindowsToggle.IsChecked =
                StartupService.IsEnabled();

        }
        catch (Exception ex)
        {
            CrashLogService.Log(
                ex,
                "Changing Start with Windows from Settings");

            StartWithWindowsToggle.IsChecked =
                StartupService.IsEnabled();

        }
    }

    private void ResetAppearance_Click(
        object sender,
        RoutedEventArgs e)
    {
        _initializing =
            true;

        try
        {
            BlurSlider.Value =
                AppearanceSettings.DefaultBlurAmount;

            TintSlider.Value =
                AppearanceSettings.DefaultTintStrength;

            SaturationSlider.Value =
                AppearanceSettings.DefaultSaturation;

            RefreshAppearanceLabels();
        }
        finally
        {
            _initializing =
                false;
        }

        _settings.ResetAppearance();
        ApplyWindowMaterial();
        RestartSaveTimer();
    }

    private void Header_MouseLeftButtonDown(
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

    private void Done_Click(
        object sender,
        RoutedEventArgs e)
    {
        SaveSettings();
        Close();
    }

    private void Close_Click(
        object sender,
        RoutedEventArgs e)
    {
        Close();
    }

    private void OnClosing(
        object? sender,
        CancelEventArgs e)
    {
        _saveTimer.Stop();
        SaveSettings();
    }
}
