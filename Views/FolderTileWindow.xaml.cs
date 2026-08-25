using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using SmoothFolder.Models;
using SmoothFolder.Native;
using SmoothFolder.Services;

namespace SmoothFolder.Views;

public partial class FolderTileWindow : Window
{
    private readonly FolderConfig _folder;
    private readonly IconService _icons;
    private readonly LauncherService _launcher;
    private readonly ShortcutImportService _importer;
    private readonly DesktopHostService _desktopHost;
    private readonly SettingsService _settings;
    private readonly Action _save;
    private readonly Action<Point> _newFolder;
    private readonly Action<FolderConfig, FolderTileWindow> _deleteFolder;
    private readonly Action _exitApp;

    private ScreenPixelPoint _dragStartCursor;
    private ScreenPixelPoint _dragStartFolder;
    private bool _isDragging;
    private bool _desktopRecoveryMode;
    private FolderPopupWindow? _popup;
    private GpuGlassBackdropService? _tileGlassBackdrop;

    public FolderTileWindow(
        FolderConfig folder,
        IconService icons,
        LauncherService launcher,
        ShortcutImportService importer,
        DesktopHostService desktopHost,
        SettingsService settings,
        Action save,
        Action<Point> newFolder,
        Action<FolderConfig, FolderTileWindow> deleteFolder,
        Action exitApp)
    {
        InitializeComponent();

        _folder = folder;
        _icons = icons;
        _launcher = launcher;
        _importer = importer;
        _desktopHost = desktopHost;
        _settings = settings;
        _save = save;
        _newFolder = newFolder;
        _deleteFolder = deleteFolder;
        _exitApp = exitApp;

        _settings.SettingsChanged +=
            OnSettingsChanged;

        Left = folder.X;
        Top = folder.Y;

        Loaded += (_, _) => Refresh();

        SourceInitialized += (_, _) =>
        {
            WindowEffects.ConfigureDesktopTile(this);
            WindowEffects.InstallDesktopTileActivationGuard(this);

            _tileGlassBackdrop =
                GpuGlassBackdropService.TryCreate(
                    this,
                    FolderCard,
                    25);

            ApplyTileMaterial();
        };

        Closed += (_, _) =>
        {
            _settings.SettingsChanged -=
                OnSettingsChanged;

            _tileGlassBackdrop?.Dispose();
            _tileGlassBackdrop = null;
        };

        ContentRendered += OnInitialContentRendered;

        PreviewMouseLeftButtonDown += OnMouseDown;
        PreviewMouseMove += OnMouseMove;
        PreviewMouseLeftButtonUp += OnMouseUp;

        DragEnter += OnDragEnter;
        DragLeave += OnDragLeave;
        Drop += OnDrop;

        MouseRightButtonUp += (_, _) => OpenContextMenu();
    }

    public string FolderId => _folder.Id;

    public bool EnsureDesktopAttachment()
    {
        var target = DesktopPositionService.Resolve(_folder);
        var attached = _desktopHost.EnsureAttachedPixels(this, target);

        if (!attached)
            return false;

        // DesktopHostService can move the tile within the desktop Z-order.
        // Immediately put the transparent composition helper directly below
        // the tile again so wallpaper/icons are sampled behind both windows.
        _tileGlassBackdrop?.SynchronizeImmediately();

        var bounds = DesktopHostService.GetScreenBoundsPixels(this);

        if (DesktopPositionService.Capture(_folder, bounds))
            _save();

        return true;
    }

    public bool ReconcileDisplayConfiguration()
    {
        var attached = EnsureDesktopAttachment();

        if (attached && _popup is { IsVisible: true })
            _popup.RepositionForCurrentMonitor();

        return attached;
    }

    public void SetDesktopRecoveryMode(bool recovering)
    {
        _desktopRecoveryMode = recovering;
        IsHitTestVisible = !recovering;

        if (!IsLoaded)
            return;

        Opacity = recovering ? 0 : 1;

        if (recovering)
        {
            _tileGlassBackdrop?.Hide();
        }
        else
        {
            _tileGlassBackdrop?.SynchronizeImmediately();
            _tileGlassBackdrop?.Show();
        }
    }

    private void OnInitialContentRendered(object? sender, EventArgs e)
    {
        // This is intentionally a one-shot startup path. Window.Show() can
        // perform a final WPF top-level z-order update during first rendering.
        // Anchoring only in Loaded is therefore too early: the tile can be
        // promoted above already-open applications until a later drag calls
        // SetWindowPos again.
        ContentRendered -= OnInitialContentRendered;

        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() =>
            {
                var attached = EnsureDesktopAttachment();

                CrashLogService.LogMessage(
                    "Initial desktop tile stabilization",
                    $"Folder '{_folder.Name}': attached={attached}; " +
                    $"position=({_folder.X:0.##}, {_folder.Y:0.##}).");

                // If an Explorer recovery started while this HWND was being
                // created, keep both halves of the glass tile hidden until
                // recovery completes.
                Opacity = _desktopRecoveryMode ? 0 : 1;
                IsHitTestVisible = !_desktopRecoveryMode;

                if (!_desktopRecoveryMode)
                {
                    _tileGlassBackdrop?.SynchronizeImmediately();
                    _tileGlassBackdrop?.Show();
                }
            }));
    }

    public void Refresh()
    {
        FolderName.Text = _folder.Name;
        ApplyTileMaterial();
        PreviewGrid.Children.Clear();

        foreach (var item in _folder.Items.Take(9))
        {
            // Small icons are requested separately from the Shell. Windows/ICO
            // files often contain a dedicated 24/32 px representation which is
            // noticeably sharper than shrinking a generic large shortcut icon.
            var image = new Image
            {
                Source = _icons.GetIcon(item.Path, 32),
                Width = 20,
                Height = 20,
                Margin = new Thickness(1.5),
                Stretch = Stretch.Uniform,
                SnapsToDevicePixels = true,
                UseLayoutRounding = true
            };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            PreviewGrid.Children.Add(image);
        }

        if (_folder.Items.Count == 0)
        {
            PreviewGrid.Children.Add(new TextBlock
            {
                Text = "＋",
                Foreground = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)),
                FontSize = 28,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });
        }
    }

    private void OnSettingsChanged(
        object? sender,
        EventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ =
                Dispatcher.BeginInvoke(
                    new Action(
                        ApplyTileMaterial));

            return;
        }

        ApplyTileMaterial();
    }

    private void ApplyTileMaterial()
    {
        var appearance =
            _settings.Current.Appearance;

        _tileGlassBackdrop?.UpdateMaterial(
            appearance.BlurAmount,
            appearance.Saturation);

        // With GPU glass active, use exactly the same global tint multiplier as
        // the popup. If composition is unavailable, preserve the old compact
        // tile density at the default 28% setting.
        var opacityScale =
            _tileGlassBackdrop?.IsActive == true
                ? appearance.TintStrength
                : Math.Clamp(
                    appearance.TintStrength *
                    (0.72 / 0.28),
                    0.0,
                    1.0);

        FolderCard.Background =
            GlassAppearanceService.CreateTintBrush(
                _folder.GlassTint,
                _folder.GlassOpacity,
                opacityScale);
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
            return;

        _dragStartCursor =
            DesktopHostService.GetCursorScreenPositionPixels();

        _dragStartFolder =
            DesktopHostService.GetScreenBoundsPixels(this).TopLeft;

        _isDragging = false;

        CaptureMouse();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            if (IsMouseCaptured)
                ReleaseMouseCapture();

            return;
        }

        var cursor =
            DesktopHostService.GetCursorScreenPositionPixels();

        var deltaX = cursor.X - _dragStartCursor.X;
        var deltaY = cursor.Y - _dragStartCursor.Y;

        var dragMonitor = MonitorService.GetForPoint(cursor);
        var thresholdX = MonitorService.DipToPixels(5, dragMonitor.DpiX);
        var thresholdY = MonitorService.DipToPixels(5, dragMonitor.DpiY);

        if (!_isDragging)
        {
            if (Math.Abs(deltaX) < thresholdX &&
                Math.Abs(deltaY) < thresholdY)
            {
                return;
            }

            _isDragging = true;

            // Lift the dragged tile once above all other SmoothFolder desktop
            // tiles. Normal application windows remain above the desktop band.
            if (_desktopHost.BeginTileDrag(this))
            {
                _tileGlassBackdrop?.SynchronizeImmediately();
            }
        }

        var target = new ScreenPixelPoint(
            _dragStartFolder.X + deltaX,
            _dragStartFolder.Y + deltaY);

        // Tile and helper keep their established Z-order for the rest of the
        // drag. Only their coordinates change, avoiding backdrop reinsertions.
        if (_desktopHost.MoveToScreenPixelsPreservingZOrder(
                this,
                target))
        {
            _tileGlassBackdrop?.SynchronizePositionImmediately();
        }
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
            return;

        if (IsMouseCaptured)
            ReleaseMouseCapture();

        if (!_isDragging)
        {
            OpenFolder();
        }
        else
        {
            // Persist the destination monitor and physical offset only after
            // the drag completes. This avoids mixed-DPI delta discontinuities
            // while the HWND crosses monitor boundaries.
            var bounds =
                DesktopHostService.GetScreenBoundsPixels(this);

            _ = DesktopPositionService.Capture(
                _folder,
                bounds);

            _tileGlassBackdrop?.SynchronizePositionImmediately();
        }

        _save();
    }

    private void OpenFolder()
    {
        try
        {
            if (_popup is { IsVisible: true })
            {
                _popup.RequestClose();
                _ = EnsureDesktopAttachment();
                return;
            }

            var popup = new FolderPopupWindow(
                _folder,
                _icons,
                _launcher,
                _importer,
                _settings,
                _save,
                Refresh);

            _popup = popup;
            popup.Closed += (_, _) =>
            {
                if (ReferenceEquals(_popup, popup))
                    _popup = null;
            };

            popup.ShowNear(this);

            // Activating the large popup must never leave the compact desktop
            // tile in an application-level Z-order band. Reassert its desktop
            // position immediately; previously a drag happened to do this.
            _ = EnsureDesktopAttachment();
        }
        catch (Exception ex)
        {
            CrashLogService.Log(ex, $"Opening folder '{_folder.Name}'");
            _popup = null;

            MessageBox.Show(
                $"SmoothFolder could not open this folder.\n\n" +
                $"A diagnostic log was written to:\n{CrashLogService.LogPath}",
                "SmoothFolder",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void OnDragEnter(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            return;

        e.Effects = DragDropEffects.Copy;
        FolderCard.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(100)));
    }

    private void OnDragLeave(object sender, DragEventArgs e)
    {
        FolderCard.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0.86, 1.0, TimeSpan.FromMilliseconds(120)));
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        FolderCard.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(100)));

        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            return;

        var paths = (string[])e.Data.GetData(DataFormats.FileDrop);

        foreach (var path in paths)
        {
            try
            {
                if (_folder.Items.Any(x =>
                        string.Equals(x.Path, path, StringComparison.OrdinalIgnoreCase)))
                    continue;

                _folder.Items.Add(_importer.Import(path, _folder));
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "Could not add item",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        _save();
        Refresh();
    }

    private void OpenContextMenu()
    {
        var menu = new ContextMenu();

        var open = new MenuItem { Header = "Open" };
        open.Click += (_, _) => OpenFolder();

        var rename = new MenuItem { Header = "Rename" };
        rename.Click += (_, _) =>
        {
            var result = PromptDialog.Show("Folder name", _folder.Name);
            if (!string.IsNullOrWhiteSpace(result))
            {
                _folder.Name = result.Trim();
                _save();
                Refresh();
            }
        };

        var tint = new MenuItem { Header = "Glass tint..." };
        tint.Click += (_, _) =>
        {
            var dialog = new GlassTintDialog(_folder)
            {
                Owner = this
            };

            if (dialog.ShowDialog() == true)
            {
                _folder.GlassTint = dialog.SelectedTint;
                _folder.GlassOpacity = dialog.SelectedOpacity;
                _save();
                Refresh();

                if (_popup is { IsVisible: true })
                {
                    _popup.Close();
                    _popup = null;
                }
            }
        };

        var add = new MenuItem { Header = "New folder" };
        add.Click += (_, _) =>
        {
            var bounds = DesktopHostService.GetScreenBoundsPixels(this);
            _newFolder(new Point(bounds.Left, bounds.Top));
        };

        var delete = new MenuItem { Header = "Delete folder" };
        delete.Click += (_, _) =>
        {
            if (MessageBox.Show(
                    $"Delete '{_folder.Name}'?\n\nThis does not uninstall or delete any game.",
                    "Delete folder",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                _deleteFolder(_folder, this);
            }
        };

        var exit = new MenuItem { Header = "Exit SmoothFolder" };
        exit.Click += (_, _) => _exitApp();

        menu.Items.Add(open);
        menu.Items.Add(rename);
        menu.Items.Add(tint);
        menu.Items.Add(add);
        menu.Items.Add(new Separator());
        menu.Items.Add(delete);
        menu.Items.Add(new Separator());
        menu.Items.Add(exit);

        menu.IsOpen = true;
    }
}
