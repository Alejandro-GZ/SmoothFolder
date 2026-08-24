using System.Windows;
using System.Windows.Threading;
using SmoothFolder.Models;
using SmoothFolder.Views;

namespace SmoothFolder.Services;

public sealed class DesktopFolderController
{
    private readonly ConfigService _configService = new();
    private readonly IconService _iconService = new();
    private readonly LauncherService _launcher = new();
    private readonly DesktopHostService _desktopHost = new();
    private readonly DispatcherTimer _desktopHostTimer;

    private readonly List<FolderTileWindow> _windows = [];
    private AppConfig _config = new();
    private bool _stopped;

    public DesktopFolderController()
    {
        _desktopHostTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _desktopHostTimer.Tick += (_, _) => MaintainDesktopHosting();
    }

    public void Start()
    {
        _config = _configService.Load();

        if (_config.Folders.Count == 0)
        {
            _config.Folders.Add(new FolderConfig { Name = "Games", X = 120, Y = 140 });
            _configService.Save(_config);
        }

        CrashLogService.LogMessage(
            "SmoothFolder startup",
            $"Loading {_config.Folders.Count} desktop folder(s).");

        var desktopHostReady = _desktopHost.RefreshHost();
        if (!desktopHostReady)
        {
            CrashLogService.LogMessage(
                "Desktop hosting fallback",
                "Explorer desktop hosting is unavailable. Tiles will remain normal " +
                "top-level tool windows so they stay visible.");
        }

        foreach (var folder in _config.Folders)
            ShowFolder(folder);

        _desktopHostTimer.Start();
    }

    private void ShowFolder(FolderConfig folder)
    {
        var importer = new ShortcutImportService(_configService);

        var window = new FolderTileWindow(
            folder,
            _iconService,
            _launcher,
            importer,
            _desktopHost,
            save: Save,
            newFolder: CreateFolder,
            deleteFolder: DeleteFolder,
            exitApp: Exit);

        _windows.Add(window);
        window.Show();

        // Show() creates the top-level HWND and WPF can still touch its z-order
        // while completing the first render. Queue a post-show correction so a
        // newly launched SmoothFolder never leaves compact tiles above apps that
        // were already open.
        _ = window.Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(() =>
            {
                _ = window.EnsureDesktopAttachment();
            }));
    }

    private void Save() => _configService.Save(_config);

    private void MaintainDesktopHosting()
    {
        if (!_desktopHost.RefreshHost())
            return;

        foreach (var window in _windows.ToArray())
        {
            if (DesktopHostService.IsWindowAlive(window))
            {
                window.EnsureDesktopAttachment();
                continue;
            }

            // Explorer can destroy/rebuild its desktop hierarchy. If that also
            // invalidates a tile HWND, recreate only the compact tile from the
            // persisted FolderConfig. Open folder popups remain top-level and
            // are intentionally not parented to Explorer.
            var folder = _config.Folders.FirstOrDefault(x => x.Id == window.FolderId);
            _windows.Remove(window);

            if (folder is not null)
                ShowFolder(folder);
        }
    }

    private void CreateFolder(Point suggestedPosition)
    {
        var folder = new FolderConfig
        {
            Name = "New Folder",
            X = suggestedPosition.X + 130,
            Y = suggestedPosition.Y
        };

        _config.Folders.Add(folder);
        Save();
        ShowFolder(folder);
    }

    private void DeleteFolder(FolderConfig folder, FolderTileWindow window)
    {
        _config.Folders.Remove(folder);
        _configService.DeleteFolderStorage(folder.Id);
        Save();

        _windows.Remove(window);
        window.Close();

        if (_windows.Count == 0)
            CreateFolder(new Point(120, 140));
    }

    public void Stop()
    {
        if (_stopped)
            return;

        _stopped = true;
        _desktopHostTimer.Stop();
        Save();
    }

    private void Exit()
    {
        Stop();
        System.Windows.Application.Current.Shutdown();
    }
}
