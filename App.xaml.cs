using System.Windows;
using SmoothFolder.Services;

namespace SmoothFolder;

public partial class App : Application
{
    private DesktopFolderController? _controller;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _controller = new DesktopFolderController();
        _controller.Start();
    }
}
