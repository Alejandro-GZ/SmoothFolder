using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Controls;
using Forms = System.Windows.Forms;

namespace SmoothFolder.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Icon _icon;
    private readonly Action _openSettings;
    private readonly Action _exitApplication;

    private ContextMenu? _trayMenu;

    public TrayIconService(
        Action openSettings,
        Action exitApplication)
    {
        _openSettings =
            openSettings;

        _exitApplication =
            exitApplication;

        _icon =
            LoadApplicationIcon();

        _notifyIcon =
            new Forms.NotifyIcon
            {
                Text =
                    "SmoothFolder",
                Icon =
                    _icon,
                Visible =
                    true
            };

        _notifyIcon.MouseUp +=
            OnTrayMouseUp;

        _notifyIcon.DoubleClick +=
            (_, _) =>
                DispatchToWpf(
                    _openSettings);
    }

    public void Dispose()
    {
        if (_trayMenu is not null)
        {
            _trayMenu.IsOpen =
                false;

            _trayMenu =
                null;
        }

        _notifyIcon.MouseUp -=
            OnTrayMouseUp;

        _notifyIcon.Visible =
            false;

        _notifyIcon.Dispose();
        _icon.Dispose();
    }

    private void OnTrayMouseUp(
        object? sender,
        Forms.MouseEventArgs e)
    {
        if (e.Button !=
            Forms.MouseButtons.Right)
        {
            return;
        }

        DispatchToWpf(
            ShowTrayMenu);
    }

    private void ShowTrayMenu()
    {
        if (_trayMenu is not null)
        {
            _trayMenu.IsOpen =
                false;
        }

        var menu =
            IosContextMenuService.Create();

        menu.Items.Add(
            IosContextMenuService.Item(
                "Settings...",
                _openSettings));

        menu.Items.Add(
            IosContextMenuService.Separator());

        menu.Items.Add(
            IosContextMenuService.Item(
                "Open data folder",
                OpenDataFolder));

        var startupEnabled =
            StartupService.IsEnabled();

        menu.Items.Add(
            IosContextMenuService.Item(
                startupEnabled
                    ? "Start with Windows  ✓"
                    : "Start with Windows",
                ToggleStartWithWindows));

        menu.Items.Add(
            IosContextMenuService.Separator());

        menu.Items.Add(
            IosContextMenuService.Item(
                "Exit SmoothFolder",
                _exitApplication,
                destructive:
                    true));

        menu.Closed +=
            (_, _) =>
            {
                if (ReferenceEquals(
                        _trayMenu,
                        menu))
                {
                    _trayMenu =
                        null;
                }
            };

        _trayMenu =
            menu;

        menu.IsOpen =
            true;
    }

    private static void DispatchToWpf(
        Action action)
    {
        _ =
            System.Windows.Application.Current
                .Dispatcher.BeginInvoke(
                    action);
    }

    private void ToggleStartWithWindows()
    {
        try
        {
            StartupService.SetEnabled(
                !StartupService.IsEnabled());
        }
        catch (Exception ex)
        {
            CrashLogService.Log(
                ex,
                "Changing Start with Windows setting");

            _notifyIcon.ShowBalloonTip(
                4000,
                "SmoothFolder",
                "Could not change the Start with Windows setting.",
                Forms.ToolTipIcon.Warning);
        }
    }

    private static Icon LoadApplicationIcon()
    {
        try
        {
            var executable =
                Environment.ProcessPath;

            if (!string.IsNullOrWhiteSpace(
                    executable))
            {
                var associated =
                    Icon.ExtractAssociatedIcon(
                        executable);

                if (associated is not null)
                    return associated;
            }
        }
        catch
        {
            // Fall back to a built-in icon rather than failing app startup.
        }

        return (Icon)
            SystemIcons.Application.Clone();
    }

    private static void OpenDataFolder()
    {
        try
        {
            var path =
                Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder
                            .LocalApplicationData),
                    "SmoothFolder");

            Directory.CreateDirectory(
                path);

            Process.Start(
                new ProcessStartInfo
                {
                    FileName =
                        "explorer.exe",
                    Arguments =
                        $"\"{path}\"",
                    UseShellExecute =
                        true
                });
        }
        catch (Exception ex)
        {
            CrashLogService.Log(
                ex,
                "Opening SmoothFolder data folder from tray");
        }
    }
}
