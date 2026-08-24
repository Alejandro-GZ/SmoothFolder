using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using SmoothFolder.Models;
using SmoothFolder.Views;

namespace SmoothFolder.Services;

public sealed class DesktopFolderController
{
    private static readonly TimeSpan HealthCheckInterval = TimeSpan.FromSeconds(15);

    private static readonly TimeSpan[] RecoveryDelays =
    [
        TimeSpan.FromMilliseconds(250),
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(3),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(8)
    ];

    private readonly ConfigService _configService = new();
    private readonly IconService _iconService = new();
    private readonly LauncherService _launcher = new();
    private readonly DesktopHostService _desktopHost = new();
    private readonly ShellLifecycleService _shellLifecycle;
    private readonly DispatcherTimer _healthTimer;
    private readonly DispatcherTimer _recoveryTimer;

    private readonly List<FolderTileWindow> _windows = [];
    private AppConfig _config = new();

    private long _attachedHostGeneration = -1;
    private int _recoveryAttempt;
    private string _recoveryReason = string.Empty;
    private bool _stopped;

    public DesktopFolderController()
    {
        _shellLifecycle = new ShellLifecycleService();
        _shellLifecycle.ExplorerRestarted += OnExplorerRestarted;

        _healthTimer = new DispatcherTimer
        {
            Interval = HealthCheckInterval
        };
        _healthTimer.Tick += (_, _) => CheckDesktopHealth();

        _recoveryTimer = new DispatcherTimer();
        _recoveryTimer.Tick += (_, _) => TryRecoverDesktopHost();
    }

    public void Start()
    {
        _config = _configService.Load();

        if (_config.Folders.Count == 0)
        {
            _config.Folders.Add(new FolderConfig { Name = "Games", X = 120, Y = 140 });
            _configService.Save(_config);
        }

        LogRuntimeEnvironment();

        CrashLogService.LogMessage(
            "SmoothFolder startup",
            $"Loading {_config.Folders.Count} desktop folder(s).");

        if (_desktopHost.RefreshHost())
        {
            _attachedHostGeneration = _desktopHost.Generation;
        }
        else
        {
            CrashLogService.LogMessage(
                "Desktop hosting fallback",
                "Explorer desktop hosting is unavailable during startup. Tiles will remain " +
                "normal hidden-from-taskbar WPF windows while recovery continues.");

            BeginDesktopRecovery("Desktop host was unavailable during startup");
        }

        foreach (var folder in _config.Folders)
            ShowFolder(folder);

        _healthTimer.Start();
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

        if (_recoveryTimer.IsEnabled)
            window.SetDesktopRecoveryMode(recovering: true);

        // Show() creates the top-level HWND and WPF can still touch its z-order
        // while completing the first render. Queue a post-show correction.
        _ = window.Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(() =>
            {
                _ = window.EnsureDesktopAttachment();
            }));
    }

    private void Save() => _configService.Save(_config);

    private void OnExplorerRestarted(object? sender, EventArgs e)
    {
        if (_stopped)
            return;

        _desktopHost.InvalidateHost("TaskbarCreated was broadcast by Explorer");
        BeginDesktopRecovery("Explorer TaskbarCreated event");
    }

    private void BeginDesktopRecovery(string reason)
    {
        if (_stopped)
            return;

        _recoveryReason = reason;
        _recoveryAttempt = 0;
        _recoveryTimer.Stop();

        SetTilesRecoveryMode(recovering: true);

        CrashLogService.LogMessage(
            "Desktop recovery started",
            reason);

        ScheduleNextRecoveryAttempt();
    }

    private void ScheduleNextRecoveryAttempt()
    {
        if (_recoveryAttempt >= RecoveryDelays.Length)
        {
            FinishRecoveryAsFallback();
            return;
        }

        _recoveryTimer.Interval = RecoveryDelays[_recoveryAttempt];
        _recoveryTimer.Start();
    }

    private void TryRecoverDesktopHost()
    {
        _recoveryTimer.Stop();

        if (_stopped)
            return;

        _recoveryAttempt++;

        if (_desktopHost.RefreshHost())
        {
            ReconcileTileWindows(forceReanchor: true);
            _attachedHostGeneration = _desktopHost.Generation;
            SetTilesRecoveryMode(recovering: false);

            CrashLogService.LogMessage(
                "Desktop recovery completed",
                $"{_recoveryReason}; attempts={_recoveryAttempt}; " +
                $"host generation={_attachedHostGeneration}.");

            return;
        }

        ScheduleNextRecoveryAttempt();
    }

    private void FinishRecoveryAsFallback()
    {
        SetTilesRecoveryMode(recovering: false);

        CrashLogService.LogMessage(
            "Desktop recovery fallback",
            $"{_recoveryReason}; Explorer desktop host was still unavailable after " +
            $"{_recoveryAttempt} attempt(s). Tiles were restored in fallback mode. " +
            $"The periodic health check will continue looking for Explorer.");
    }

    private void CheckDesktopHealth()
    {
        if (_stopped || _recoveryTimer.IsEnabled)
            return;

        if (!_desktopHost.RefreshHost())
        {
            BeginDesktopRecovery("Periodic desktop-host validation failed");
            return;
        }

        // A new generation means Explorer's hierarchy was rediscovered outside
        // the TaskbarCreated path. Reanchor exactly once instead of touching the
        // Z-order every two seconds as older builds did.
        var generationChanged =
            _desktopHost.Generation != _attachedHostGeneration;

        ReconcileTileWindows(forceReanchor: generationChanged);

        if (generationChanged)
        {
            CrashLogService.LogMessage(
                "Desktop host generation changed",
                $"Reanchored tiles to generation {_desktopHost.Generation} " +
                $"(previous={_attachedHostGeneration}).");

            _attachedHostGeneration = _desktopHost.Generation;
        }
    }

    private void ReconcileTileWindows(bool forceReanchor)
    {
        foreach (var window in _windows.ToArray())
        {
            if (DesktopHostService.IsWindowAlive(window))
            {
                if (forceReanchor)
                    _ = window.EnsureDesktopAttachment();

                continue;
            }

            // Compact WPF tiles normally survive explorer.exe restarts because
            // they are top-level windows. Recreate one only if its HWND really
            // became invalid.
            var folder = _config.Folders.FirstOrDefault(
                x => x.Id == window.FolderId);

            _windows.Remove(window);

            if (folder is not null)
                ShowFolder(folder);
        }
    }

    private void SetTilesRecoveryMode(bool recovering)
    {
        foreach (var window in _windows.ToArray())
        {
            if (DesktopHostService.IsWindowAlive(window))
                window.SetDesktopRecoveryMode(recovering);
        }
    }

    private static void LogRuntimeEnvironment()
    {
        CrashLogService.LogMessage(
            "Runtime environment",
            $"OS={RuntimeInformation.OSDescription}{Environment.NewLine}" +
            $"OS version={Environment.OSVersion.Version}{Environment.NewLine}" +
            $"Process architecture={RuntimeInformation.ProcessArchitecture}{Environment.NewLine}" +
            $"Framework={RuntimeInformation.FrameworkDescription}");
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

        _healthTimer.Stop();
        _recoveryTimer.Stop();

        _shellLifecycle.ExplorerRestarted -= OnExplorerRestarted;
        _shellLifecycle.Dispose();

        Save();
    }

    private void Exit()
    {
        Stop();
        System.Windows.Application.Current.Shutdown();
    }
}
