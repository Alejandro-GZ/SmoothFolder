using Microsoft.Win32;

namespace SmoothFolder.Services;

/// <summary>
/// Manages per-user Windows startup registration.
///
/// SmoothFolder is an unpackaged desktop application, so the standard HKCU Run
/// key is a good fit: it requires no administrator privileges and applies only
/// to the current Windows user.
/// </summary>
public static class StartupService
{
    private const string RunKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Run";

    private const string ValueName =
        "SmoothFolder";

    public static bool IsEnabled()
    {
        try
        {
            using var key =
                Registry.CurrentUser.OpenSubKey(
                    RunKeyPath,
                    writable: false);

            var registeredCommand =
                key?.GetValue(
                    ValueName) as string;

            if (string.IsNullOrWhiteSpace(
                    registeredCommand))
            {
                return false;
            }

            return string.Equals(
                registeredCommand,
                BuildStartupCommand(),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            CrashLogService.Log(
                ex,
                "Reading Start with Windows setting");

            return false;
        }
    }

    public static void SetEnabled(
        bool enabled)
    {
        using var key =
            Registry.CurrentUser.CreateSubKey(
                RunKeyPath,
                writable: true)
            ?? throw new InvalidOperationException(
                "Could not open the current-user Windows startup registry key.");

        if (enabled)
        {
            var command =
                BuildStartupCommand();

            key.SetValue(
                ValueName,
                command,
                RegistryValueKind.String);

            CrashLogService.LogMessage(
                "Startup setting changed",
                $"Start with Windows enabled. Command={command}");
        }
        else
        {
            key.DeleteValue(
                ValueName,
                throwOnMissingValue: false);

            CrashLogService.LogMessage(
                "Startup setting changed",
                "Start with Windows disabled.");
        }
    }

    private static string BuildStartupCommand()
    {
        var executable =
            Environment.ProcessPath;

        if (string.IsNullOrWhiteSpace(
                executable))
        {
            throw new InvalidOperationException(
                "Could not determine the SmoothFolder executable path.");
        }

        // Always quote the path. Release archives are often extracted below
        // directories containing spaces, and the HKCU Run value is a command
        // line rather than a raw executable-path field.
        return $"\"{executable}\"";
    }
}
