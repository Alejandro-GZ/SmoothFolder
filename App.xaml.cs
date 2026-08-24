using System.Threading;
using System.Windows;
using SmoothFolder.Services;

namespace SmoothFolder;

public partial class App : Application
{
    private const string InstanceMutexName = @"Local\SmoothFolder.DesktopFolders";
    private DesktopFolderController? _controller;
    private Mutex? _instanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

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

        _controller = new DesktopFolderController();
        _controller.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
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

        base.OnExit(e);
    }
}
