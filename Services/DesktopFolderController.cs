using System.Windows;
using SmoothFolder.Models;
using SmoothFolder.Views;

namespace SmoothFolder.Services;

public sealed class DesktopFolderController
{
    private readonly ConfigService _configService = new();
    private readonly IconService _iconService = new();
    private readonly LauncherService _launcher = new();

    private readonly List<FolderTileWindow> _windows = [];
    private AppConfig _config = new();

    public void Start()
    {
        _config = _configService.Load();

        if (_config.Folders.Count == 0)
        {
            _config.Folders.Add(new FolderConfig { Name = "Games", X = 120, Y = 140 });
            _configService.Save(_config);
        }

        foreach (var folder in _config.Folders)
            ShowFolder(folder);
    }

    private void ShowFolder(FolderConfig folder)
    {
        var importer = new ShortcutImportService(_configService);

        var window = new FolderTileWindow(
            folder,
            _iconService,
            _launcher,
            importer,
            save: Save,
            newFolder: CreateFolder,
            deleteFolder: DeleteFolder,
            exitApp: Exit);

        _windows.Add(window);
        window.Show();
    }

    private void Save() => _configService.Save(_config);

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

    private void Exit()
    {
        Save();
        Application.Current.Shutdown();
    }
}
