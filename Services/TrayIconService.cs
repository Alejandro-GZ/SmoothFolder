using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace SmoothFolder.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly Icon _icon;
    private readonly ToolStripMenuItem _startWithWindows;

    public TrayIconService(
        Action openSettings,
        Action exitApplication)
    {
        _icon = LoadApplicationIcon();

        var menu = new ContextMenuStrip();

        var settings =
            new ToolStripMenuItem("Settings...");

        settings.Click +=
            (_, _) =>
                DispatchToWpf(
                    openSettings);

        var openDataFolder = new ToolStripMenuItem("Open data folder");
        openDataFolder.Click += (_, _) => OpenDataFolder();

        _startWithWindows =
            new ToolStripMenuItem("Start with Windows")
            {
                CheckOnClick = false,
                Checked = StartupService.IsEnabled()
            };

        _startWithWindows.Click +=
            (_, _) => ToggleStartWithWindows();

        menu.Opening +=
            (_, _) => RefreshStartWithWindowsState();

        var exit = new ToolStripMenuItem("Exit SmoothFolder");
        exit.Click += (_, _) =>
        {
            System.Windows.Application.Current.Dispatcher.BeginInvoke(
                exitApplication);
        };

        menu.Items.Add(settings);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(openDataFolder);
        menu.Items.Add(_startWithWindows);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exit);

        _notifyIcon = new NotifyIcon
        {
            Text = "SmoothFolder",
            Icon = _icon,
            ContextMenuStrip = menu,
            Visible = true
        };

        _notifyIcon.DoubleClick +=
            (_, _) =>
                DispatchToWpf(
                    openSettings);
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
        _icon.Dispose();
    }

    private static void DispatchToWpf(
        Action action)
    {
        _ =
            System.Windows.Application.Current.Dispatcher.BeginInvoke(
                action);
    }

    private void ToggleStartWithWindows()
    {
        try
        {
            var enable =
                !StartupService.IsEnabled();

            StartupService.SetEnabled(
                enable);

            _startWithWindows.Checked =
                StartupService.IsEnabled();
        }
        catch (Exception ex)
        {
            CrashLogService.Log(
                ex,
                "Changing Start with Windows setting");

            RefreshStartWithWindowsState();

            _notifyIcon.ShowBalloonTip(
                4000,
                "SmoothFolder",
                "Could not change the Start with Windows setting.",
                ToolTipIcon.Warning);
        }
    }

    private void RefreshStartWithWindowsState()
    {
        _startWithWindows.Checked =
            StartupService.IsEnabled();
    }

    private static Icon LoadApplicationIcon()
    {
        try
        {
            var executable = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(executable))
            {
                var associated = Icon.ExtractAssociatedIcon(executable);
                if (associated is not null)
                    return associated;
            }
        }
        catch
        {
            // Fall back to a built-in icon rather than failing app startup.
        }

        return (Icon)SystemIcons.Application.Clone();
    }

    private static void OpenDataFolder()
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SmoothFolder");

            Directory.CreateDirectory(path);

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{path}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            CrashLogService.Log(ex, "Opening SmoothFolder data folder from tray");
        }
    }
}
