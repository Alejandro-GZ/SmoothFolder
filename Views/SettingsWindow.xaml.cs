using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using SmoothFolder.Models;
using SmoothFolder.Services;

namespace SmoothFolder.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsService _settings;
    private readonly DispatcherTimer _saveTimer;
    private bool _initializing;

    public SettingsWindow(
        SettingsService settings)
    {
        // Slider.ValueChanged can fire during InitializeComponent (for example
        // when SaturationSlider.Minimum coerces the default value). Initialize
        // dependencies and enable the guard before XAML construction.
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

        Closing +=
            OnClosing;

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
            StatusText.Text =
                string.Empty;
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

        RestartSaveTimer();
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

        StatusText.Text =
            "Saved automatically";
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

            StatusText.Text =
                "Could not save settings. Check the SmoothFolder log for details.";
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

            StatusText.Text =
                StartWithWindowsToggle.IsChecked ==
                true
                    ? "SmoothFolder will start with Windows."
                    : "SmoothFolder will not start with Windows.";
        }
        catch (Exception ex)
        {
            CrashLogService.Log(
                ex,
                "Changing Start with Windows from Settings");

            StartWithWindowsToggle.IsChecked =
                StartupService.IsEnabled();

            StatusText.Text =
                "Could not change the Start with Windows setting.";
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

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // The button may be released between the event and DragMove().
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
