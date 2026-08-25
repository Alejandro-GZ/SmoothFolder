using System.Threading;
using System.Windows;
using SmoothFolder.Services;
using SmoothFolder.Views;

namespace SmoothFolder;

public partial class App : System.Windows.Application
{
    private const string InstanceMutexName = @"Local\SmoothFolder.DesktopFolders";
    private DesktopFolderController? _controller;
    private TrayIconService? _trayIcon;
    private SettingsService? _settings;
    private SettingsWindow? _settingsWindow;
    private Mutex? _instanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException +=
            OnDispatcherUnhandledException;

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // SmoothFolder is a background desktop companion, not a conventional
        // foreground application. Keep one process only so launching the EXE
        // twice cannot duplicate every desktop folder.
        _instanceMutex = new Mutex(
            initiallyOwned: true,
            name: InstanceMutexName,
            createdNew: out var isFirstInstance);

        if (!isFirstInstance)
        {
            Shutdown();
            return;
        }

        _settings =
            new SettingsService();

        _controller = new DesktopFolderController();
        _controller.Start();

        _trayIcon =
            new TrayIconService(
                OpenSettings,
                () => Shutdown());
    }

    private void OpenSettings()
    {
        Dispatcher.BeginInvoke(
            new Action(() =>
            {
                if (_settings is null)
                    return;

                if (_settingsWindow is not null)
                {
                    if (_settingsWindow.WindowState ==
                        WindowState.Minimized)
                    {
                        _settingsWindow.WindowState =
                            WindowState.Normal;
                    }

                    _settingsWindow.Show();
                    _settingsWindow.Activate();
                    return;
                }

                _settingsWindow =
                    new SettingsWindow(
                        _settings);

                _settingsWindow.Closed +=
                    (_, _) =>
                    {
                        _settingsWindow =
                            null;
                    };

                _settingsWindow.Show();
                _settingsWindow.Activate();
            }));
    }

    private void OnDispatcherUnhandledException(
        object sender,
        System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            CrashLogService.Log(
                e.Exception,
                "Unhandled WPF dispatcher exception");
        }
        catch
        {
            // Preserve the original exception if logging itself fails.
        }

        // Log unexpected UI failures, but do not silently swallow them.
        e.Handled =
            false;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _settingsWindow?.Close();
        _settingsWindow = null;

        _trayIcon?.Dispose();
        _trayIcon = null;

        _controller?.Stop();

        try
        {
            _instanceMutex?.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // The mutex was not owned anymore. Shutdown should still continue.
        }
        finally
        {
            _instanceMutex?.Dispose();
        }

        DispatcherUnhandledException -=
            OnDispatcherUnhandledException;

        base.OnExit(e);
    }
}
