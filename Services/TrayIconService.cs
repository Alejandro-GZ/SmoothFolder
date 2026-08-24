using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace SmoothFolder.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly Icon _icon;

    public TrayIconService(Action exitApplication)
    {
        _icon = LoadApplicationIcon();

        var menu = new ContextMenuStrip();

        var openDataFolder = new ToolStripMenuItem("Open data folder");
        openDataFolder.Click += (_, _) => OpenDataFolder();

        var exit = new ToolStripMenuItem("Exit SmoothFolder");
        exit.Click += (_, _) =>
        {
            System.Windows.Application.Current.Dispatcher.BeginInvoke(
                exitApplication);
        };

        menu.Items.Add(openDataFolder);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exit);

        _notifyIcon = new NotifyIcon
        {
            Text = "SmoothFolder",
            Icon = _icon,
            ContextMenuStrip = menu,
            Visible = true
        };
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
        _icon.Dispose();
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
