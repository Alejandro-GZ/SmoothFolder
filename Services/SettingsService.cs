using System.IO;
using System.Text.Json;
using SmoothFolder.Models;

namespace SmoothFolder.Services;

/// <summary>
/// Owns SmoothFolder's user-level settings and persists them separately from
/// the desktop folder model. Keeping settings.json separate prevents a Settings
/// window from overwriting live folder/item changes in config.json.
/// </summary>
public sealed class SettingsService
{
    public const double MinBlurAmount = 0.0;
    public const double MaxBlurAmount = 20.0;

    public const double MinTintStrength = 0.0;
    public const double MaxTintStrength = 0.70;

    public const double MinSaturation = 0.50;
    public const double MaxSaturation = 1.40;

    private readonly string _root =
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "SmoothFolder");

    private readonly JsonSerializerOptions _jsonOptions =
        new()
        {
            WriteIndented = true
        };

    public SettingsService()
    {
        Current = Load();
    }

    public event EventHandler? SettingsChanged;

    public AppSettings Current { get; private set; } =
        new();

    private string SettingsPath =>
        Path.Combine(
            _root,
            "settings.json");

    public void UpdateAppearance(
        double blurAmount,
        double tintStrength,
        double saturation)
    {
        Current.Appearance.BlurAmount =
            Math.Clamp(
                blurAmount,
                MinBlurAmount,
                MaxBlurAmount);

        Current.Appearance.TintStrength =
            Math.Clamp(
                tintStrength,
                MinTintStrength,
                MaxTintStrength);

        Current.Appearance.Saturation =
            Math.Clamp(
                saturation,
                MinSaturation,
                MaxSaturation);

        SettingsChanged?.Invoke(
            this,
            EventArgs.Empty);
    }

    public void ResetAppearance()
    {
        UpdateAppearance(
            AppearanceSettings.DefaultBlurAmount,
            AppearanceSettings.DefaultTintStrength,
            AppearanceSettings.DefaultSaturation);
    }

    public void Save()
    {
        Directory.CreateDirectory(
            _root);

        Normalize(
            Current);

        var temporaryPath =
            SettingsPath + ".tmp";

        File.WriteAllText(
            temporaryPath,
            JsonSerializer.Serialize(
                Current,
                _jsonOptions));

        File.Move(
            temporaryPath,
            SettingsPath,
            overwrite: true);
    }

    private AppSettings Load()
    {
        Directory.CreateDirectory(
            _root);

        if (!File.Exists(
                SettingsPath))
        {
            var initial =
                new AppSettings();

            SaveInitial(
                initial);

            return initial;
        }

        try
        {
            var settings =
                JsonSerializer.Deserialize<AppSettings>(
                    File.ReadAllText(
                        SettingsPath),
                    _jsonOptions)
                ?? new AppSettings();

            Normalize(
                settings);

            return settings;
        }
        catch (Exception ex)
        {
            try
            {
                var backup =
                    SettingsPath +
                    ".broken-" +
                    DateTime.Now.ToString(
                        "yyyyMMdd-HHmmss");

                File.Copy(
                    SettingsPath,
                    backup,
                    overwrite: true);
            }
            catch
            {
                // A malformed settings file must never block application
                // startup, even if a backup cannot be created.
            }

            CrashLogService.Log(
                ex,
                "Loading SmoothFolder settings");

            return new AppSettings();
        }
    }

    private void SaveInitial(
        AppSettings settings)
    {
        Current =
            settings;

        Save();
    }

    private static void Normalize(
        AppSettings settings)
    {
        settings.SchemaVersion = 1;
        settings.Appearance ??=
            new AppearanceSettings();

        settings.Appearance.BlurAmount =
            Math.Clamp(
                settings.Appearance.BlurAmount,
                MinBlurAmount,
                MaxBlurAmount);

        settings.Appearance.TintStrength =
            Math.Clamp(
                settings.Appearance.TintStrength,
                MinTintStrength,
                MaxTintStrength);

        settings.Appearance.Saturation =
            Math.Clamp(
                settings.Appearance.Saturation,
                MinSaturation,
                MaxSaturation);
    }
}
