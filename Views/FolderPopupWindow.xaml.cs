using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using SmoothFolder.Models;
using SmoothFolder.Native;
using SmoothFolder.Services;

namespace SmoothFolder.Views;

public partial class FolderPopupWindow : Window
{
    private readonly FolderConfig _folder;
    private readonly IconService _icons;
    private readonly LauncherService _launcher;
    private readonly ShortcutImportService _importer;
    private readonly Action _save;
    private readonly Action _refreshTile;

    private bool _isClosing;
    private bool _allowClose;
    private Window? _anchorTile;

    public FolderPopupWindow(
        FolderConfig folder,
        IconService icons,
        LauncherService launcher,
        ShortcutImportService importer,
        Action save,
        Action refreshTile)
    {
        InitializeComponent();

        _folder = folder;
        _icons = icons;
        _launcher = launcher;
        _importer = importer;
        _save = save;
        _refreshTile = refreshTile;

        TitleText.Text = folder.Name;
        ApplyGlassAppearance();

        Loaded += (_, _) => RefreshItems();

        Closing += OnClosing;
        SourceInitialized += (_, _) => WindowEffects.ApplyPopupEffects(this, 30);

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                RequestClose();
        };

        DragOver += (_, e) =>
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effects = DragDropEffects.Copy;
        };

        Drop += OnDrop;
    }

    public void ShowNear(Window tile)
    {
        // Do not make the desktop tile the native/WPF owner of the popup.
        // Activating an owned popup can promote its owner in the top-level
        // Z-order group.
        _anchorTile = tile;

        // Keep the first frame hidden while the HWND is created. WPF creates
        // top-level windows in DIP coordinates; the final placement is applied
        // in physical pixels after Show(), which is stable across monitors with
        // different DPI scales.
        Opacity = 0;
        Show();

        PositionNearAnchorPixels();

        _ = Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.ContextIdle,
            new Action(() =>
            {
                // WM_DPICHANGED may have resized the WPF HWND after the first
                // cross-monitor SetWindowPos. Recalculate once using the actual
                // target-monitor size before revealing the popup.
                PositionNearAnchorPixels();

                Opacity = 1;
                AnimateOpen();
                Activate();
            }));
    }

    private void PositionNearAnchorPixels()
    {
        if (_anchorTile is null)
            return;

        var tileBounds =
            DesktopHostService.GetScreenBoundsPixels(_anchorTile);

        var monitor = MonitorService.GetForRect(tileBounds);

        var currentBounds =
            DesktopHostService.GetScreenBoundsPixels(this);

        var width = currentBounds.Width > 0
            ? currentBounds.Width
            : MonitorService.DipToPixels(Width, monitor.DpiX);

        var height = currentBounds.Height > 0
            ? currentBounds.Height
            : MonitorService.DipToPixels(Height, monitor.DpiY);

        // If the popup HWND was initially created on a differently-scaled
        // monitor, use the destination monitor's expected WPF size for the
        // first placement. The second dispatcher pass uses the actual HWND.
        var currentMonitor = MonitorService.GetForRect(currentBounds);

        if (!string.Equals(
                currentMonitor.DeviceName,
                monitor.DeviceName,
                StringComparison.OrdinalIgnoreCase))
        {
            width = MonitorService.DipToPixels(
                Width,
                monitor.DpiX);

            height = MonitorService.DipToPixels(
                Height,
                monitor.DpiY);
        }

        var marginX = MonitorService.DipToPixels(
            12,
            monitor.DpiX);

        var marginY = MonitorService.DipToPixels(
            12,
            monitor.DpiY);

        var gap = MonitorService.DipToPixels(
            8,
            monitor.DpiY);

        var left =
            tileBounds.Left +
            (tileBounds.Width / 2) -
            (width / 2);

        var top = tileBounds.Bottom + gap;

        var minLeft = monitor.WorkArea.Left + marginX;
        var maxLeft = Math.Max(
            minLeft,
            monitor.WorkArea.Right - width - marginX);

        left = Math.Clamp(
            left,
            minLeft,
            maxLeft);

        if (top + height > monitor.WorkArea.Bottom - marginY)
            top = tileBounds.Top - height - gap;

        var minTop = monitor.WorkArea.Top + marginY;
        var maxTop = Math.Max(
            minTop,
            monitor.WorkArea.Bottom - height - marginY);

        top = Math.Clamp(
            top,
            minTop,
            maxTop);

        _ = MonitorService.PositionWindowPixels(
            this,
            new ScreenPixelPoint(left, top));
    }

    public void RepositionForCurrentMonitor()
    {
        if (!IsVisible || _anchorTile is null)
            return;

        PositionNearAnchorPixels();
    }

    public void RequestClose()
    {
        if (_isClosing || _allowClose)
            return;

        _isClosing = true;
        AnimateClose(() =>
        {
            _allowClose = true;
            Close();
        });
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
            return;

        e.Cancel = true;
        RequestClose();
    }

    private void RefreshItems()
    {
        ItemsPanel.Children.Clear();

        foreach (var item in _folder.Items)
            ItemsPanel.Children.Add(BuildItem(item));

        if (_folder.Items.Count == 0)
        {
            ItemsPanel.Children.Add(new TextBlock
            {
                Text = "Empty folder",
                Foreground = new SolidColorBrush(Color.FromArgb(150, 230, 238, 248)),
                FontFamily = (FontFamily)FindResource("UiFontFamily"),
                FontSize = 14,
                Margin = new Thickness(6, 14, 0, 0)
            });
        }
    }

    private FrameworkElement BuildItem(AppItem item)
    {
        var icon = new Image
        {
            Source = _icons.GetIcon(item.Path, 128),
            Width = 62,
            Height = 62,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            SnapsToDevicePixels = true,
            UseLayoutRounding = true
        };
        RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);

        var label = new TextBlock
        {
            Text = item.DisplayName,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            FontFamily = (FontFamily)FindResource("UiFontFamily"),
            FontSize = 12.5,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 96,
            Margin = new Thickness(0, 8, 0, 0)
        };

        var stack = new StackPanel();
        stack.Children.Add(icon);
        stack.Children.Add(label);

        var border = new Border
        {
            Width = 108,
            Height = 112,
            Padding = new Thickness(6),
            Margin = new Thickness(4),
            CornerRadius = new CornerRadius(18),
            Background = Brushes.Transparent,
            Child = stack,
            Cursor = Cursors.Hand
        };

        border.MouseEnter += (_, _) =>
        {
            border.Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
        };

        border.MouseLeave += (_, _) => border.Background = Brushes.Transparent;

        border.MouseLeftButtonUp += (_, _) =>
        {
            try
            {
                _launcher.Launch(item.Path);
                RequestClose();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "Could not open item",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        };

        // Standard WPF ContextMenu is attached to the whole item card, so
        // right-click works consistently on the icon, label, or empty padding.
        border.ContextMenu = BuildItemContextMenu(item);

        return border;
    }

    private ContextMenu BuildItemContextMenu(AppItem item)
    {
        var menu = new ContextMenu();

        var rename = new MenuItem { Header = "Rename" };
        rename.Click += (_, _) =>
        {
            var name = PromptDialog.Show("Display name", item.DisplayName);
            if (!string.IsNullOrWhiteSpace(name))
            {
                item.DisplayName = name.Trim();
                _save();
                RefreshItems();
                _refreshTile();
            }
        };

        var remove = new MenuItem { Header = "Remove app from folder" };
        remove.Click += (_, _) => RemoveItem(item);

        menu.Items.Add(rename);
        menu.Items.Add(new Separator());
        menu.Items.Add(remove);

        return menu;
    }

    private void RemoveItem(AppItem item)
    {
        _folder.Items.Remove(item);

        // If SmoothFolder copied a .lnk/.url into its private AppData storage,
        // remove only that private copy. The original shortcut/game is untouched.
        try
        {
            var appDataRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SmoothFolder");

            if (File.Exists(item.Path) &&
                item.Path.StartsWith(appDataRoot, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(item.Path);
            }
        }
        catch
        {
            // The UI/config can still be updated if a private cached shortcut
            // cannot be removed immediately.
        }

        _save();
        RefreshItems();
        _refreshTile();
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            return;

        var paths = (string[])e.Data.GetData(DataFormats.FileDrop);

        foreach (var path in paths)
        {
            try
            {
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
        RefreshItems();
        _refreshTile();
    }

    private void DragHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
            return;

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // The button can be released between the mouse event and DragMove.
        }
    }

    private void ApplyGlassAppearance()
    {
        PopupCard.Background = GlassAppearanceService.CreateTintBrush(
            _folder.GlassTint,
            _folder.GlassOpacity,
            opacityScale: 0.78);
    }

    private void AnimateOpen()
    {
        if (!TryGetPopupTransforms(out var scale, out var translate))
            return;

        var travel = GetAnchorTravel();

        Opacity = 0.04;
        scale.ScaleX = 0.18;
        scale.ScaleY = 0.18;
        translate.X = travel.X;
        translate.Y = travel.Y;

        var spring = new BackEase
        {
            Amplitude = 0.16,
            EasingMode = EasingMode.EaseOut
        };
        var positionEase = new QuinticEase { EasingMode = EasingMode.EaseOut };

        BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0.04, 1, TimeSpan.FromMilliseconds(190))
            {
                EasingFunction = positionEase
            });

        scale.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.18, 1, TimeSpan.FromMilliseconds(310))
            {
                EasingFunction = spring
            });

        scale.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.18, 1, TimeSpan.FromMilliseconds(310))
            {
                EasingFunction = spring
            });

        translate.BeginAnimation(
            TranslateTransform.XProperty,
            new DoubleAnimation(travel.X, 0, TimeSpan.FromMilliseconds(285))
            {
                EasingFunction = positionEase
            });

        translate.BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(travel.Y, 0, TimeSpan.FromMilliseconds(285))
            {
                EasingFunction = positionEase
            });
    }

    private void AnimateClose(Action completed)
    {
        if (!TryGetPopupTransforms(out var scale, out var translate))
        {
            completed();
            return;
        }

        // Recalculate here instead of reusing the opening vector. The popup or
        // the desktop folder may have moved while the folder was open.
        var travel = GetAnchorTravel();
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };

        var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(185))
        {
            EasingFunction = ease
        };
        fade.Completed += (_, _) => completed();

        BeginAnimation(OpacityProperty, fade);

        scale.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            new DoubleAnimation(scale.ScaleX, 0.16, TimeSpan.FromMilliseconds(245))
            {
                EasingFunction = ease
            });

        scale.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            new DoubleAnimation(scale.ScaleY, 0.16, TimeSpan.FromMilliseconds(245))
            {
                EasingFunction = ease
            });

        translate.BeginAnimation(
            TranslateTransform.XProperty,
            new DoubleAnimation(translate.X, travel.X, TimeSpan.FromMilliseconds(245))
            {
                EasingFunction = ease
            });

        translate.BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(translate.Y, travel.Y, TimeSpan.FromMilliseconds(245))
            {
                EasingFunction = ease
            });
    }

    private Point GetAnchorTravel()
    {
        if (_anchorTile is null)
            return new Point(0, 0);

        var tileBounds =
            DesktopHostService.GetScreenBoundsPixels(_anchorTile);

        var popupBounds =
            DesktopHostService.GetScreenBoundsPixels(this);

        var folderCenter = tileBounds.Center;
        var popupCenter = popupBounds.Center;

        var (dpiX, dpiY) =
            MonitorService.GetWindowDpi(this);

        // RenderTransform uses WPF DIPs, while the shell geometry is physical
        // pixels. Convert only the local travel vector using the popup's DPI;
        // never convert an absolute virtual-desktop coordinate this way.
        return new Point(
            MonitorService.PixelsToDip(
                folderCenter.X - popupCenter.X,
                dpiX),
            MonitorService.PixelsToDip(
                folderCenter.Y - popupCenter.Y,
                dpiY));
    }

    private bool TryGetPopupTransforms(
        out ScaleTransform scale,
        out TranslateTransform translate)
    {
        scale = null!;
        translate = null!;

        if (PopupRoot.RenderTransform is not TransformGroup group)
            return false;

        scale = group.Children.OfType<ScaleTransform>().FirstOrDefault()!;
        translate = group.Children.OfType<TranslateTransform>().FirstOrDefault()!;

        return scale is not null && translate is not null;
    }
}
