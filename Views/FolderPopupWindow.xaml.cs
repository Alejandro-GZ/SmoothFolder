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
    private const string InternalItemDragFormat =
        "SmoothFolder.InternalAppItemId";

    private const int ItemsPerPage = 12;
    private const int PageColumns = 4;
    private const double PageAnimationDistance = 24;
    private const double DragPageEdgeWidth = 52;

    private readonly FolderConfig _folder;
    private readonly IconService _icons;
    private readonly LauncherService _launcher;
    private readonly ShortcutImportService _importer;
    private readonly SettingsService _settings;
    private readonly Action _save;
    private readonly Action _refreshTile;
    private readonly System.Windows.Threading.DispatcherTimer _dragPageTimer;

    private GpuGlassBackdropService? _glassBackdrop;
    private bool _isClosing;
    private bool _allowClose;
    private bool _internalDragInProgress;

    private Window? _anchorTile;
    private AppItem? _pressedItem;
    private Point _pressedItemPosition;
    private Border? _dropIndicatorCard;
    private Border? _dropIndicatorMarker;
    private int? _dropInsertionIndex;
    private int _currentPage;
    private int _pendingDragPageDelta;

    public FolderPopupWindow(
        FolderConfig folder,
        IconService icons,
        LauncherService launcher,
        ShortcutImportService importer,
        SettingsService settings,
        Action save,
        Action refreshTile)
    {
        InitializeComponent();

        _folder = folder;
        _icons = icons;
        _launcher = launcher;
        _importer = importer;
        _settings = settings;
        _save = save;
        _refreshTile = refreshTile;

        _dragPageTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(560)
        };
        _dragPageTimer.Tick += (_, _) => OnDragPageTimerTick();

        TitleText.Text = folder.Name;
        ApplyGlassAppearance();

        _settings.SettingsChanged +=
            OnSettingsChanged;

        Loaded += (_, _) => RefreshItems();

        Closing += OnClosing;
        Closed += (_, _) =>
        {
            _settings.SettingsChanged -=
                OnSettingsChanged;

            _glassBackdrop?.Dispose();
            _glassBackdrop = null;
        };

        SourceInitialized += (_, _) =>
        {
            WindowEffects.ApplyPopupEffects(
                this,
                30);

            _glassBackdrop =
                GpuGlassBackdropService.TryCreate(
                    this,
                    PopupCard,
                    30);

            ApplyMaterialSettings();
        };

        PreviewKeyDown += OnPreviewKeyDown;
        PreviewMouseWheel += OnPreviewMouseWheel;

        DragOver += OnPopupDragOver;
        DragLeave += OnPopupDragLeave;
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
        _glassBackdrop?.Synchronize();
    }

    public void RequestClose()
    {
        if (_isClosing || _allowClose)
            return;

        _isClosing = true;
        CancelDragPageSwitch();
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

    private int PageCount =>
        Math.Max(
            1,
            (_folder.Items.Count + ItemsPerPage - 1) / ItemsPerPage);

    private int CurrentPageStartIndex =>
        _currentPage * ItemsPerPage;

    private int CurrentPageEndInsertionIndex =>
        Math.Min(
            CurrentPageStartIndex + ItemsPerPage,
            _folder.Items.Count);

    private void RefreshItems()
    {
        ClearDropIndicator();
        _pressedItem = null;

        _currentPage = Math.Clamp(
            _currentPage,
            0,
            PageCount - 1);

        ItemsPanel.Children.Clear();

        foreach (var item in _folder.Items
                     .Skip(CurrentPageStartIndex)
                     .Take(ItemsPerPage))
        {
            ItemsPanel.Children.Add(BuildItem(item));
        }

        EmptyState.Visibility =
            _folder.Items.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;

        UpdatePageIndicators();
    }

    private void UpdatePageIndicators()
    {
        PageIndicators.Children.Clear();

        var pageCount = PageCount;

        PageIndicators.Visibility =
            pageCount > 1
                ? Visibility.Visible
                : Visibility.Collapsed;

        if (pageCount <= 1)
            return;

        for (var page = 0; page < pageCount; page++)
        {
            var targetPage = page;
            var isCurrent = page == _currentPage;

            var dot = new Border
            {
                Width = isCurrent ? 7.5 : 6,
                Height = isCurrent ? 7.5 : 6,
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(
                    isCurrent
                        ? Color.FromArgb(225, 255, 255, 255)
                        : Color.FromArgb(105, 255, 255, 255)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            };

            var hitTarget = new Border
            {
                Width = 18,
                Height = 18,
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                Child = dot,
                ToolTip = $"Page {page + 1}"
            };

            hitTarget.MouseLeftButtonUp += (_, e) =>
            {
                if (e.ChangedButton != MouseButton.Left)
                    return;

                NavigateToPage(
                    targetPage,
                    animate: true);

                e.Handled = true;
            };

            PageIndicators.Children.Add(hitTarget);
        }
    }

    private void NavigateToPage(
        int requestedPage,
        bool animate)
    {
        var targetPage = Math.Clamp(
            requestedPage,
            0,
            PageCount - 1);

        if (targetPage == _currentPage)
            return;

        var direction =
            targetPage > _currentPage
                ? 1
                : -1;

        _currentPage = targetPage;
        RefreshItems();

        if (animate)
            AnimatePageEntry(direction);
    }

    private void AnimatePageEntry(int direction)
    {
        if (ItemsPanel.RenderTransform is not TranslateTransform translate)
            return;

        // Do not animate the page opacity. Rebuilding a page and immediately
        // dropping the whole icon grid to 28% made cached Shell icons look like
        // they disappeared/reloaded for one frame, even though the ImageSource
        // itself was already cached. Keep icons fully opaque and animate only a
        // short horizontal translation.
        ItemsPanel.BeginAnimation(
            OpacityProperty,
            null);

        ItemsPanel.Opacity = 1;

        translate.BeginAnimation(
            TranslateTransform.XProperty,
            null);

        var ease = new QuarticEase
        {
            EasingMode = EasingMode.EaseOut
        };

        translate.X =
            direction * PageAnimationDistance;

        var slide = new DoubleAnimation(
            direction * PageAnimationDistance,
            0,
            TimeSpan.FromMilliseconds(175))
        {
            EasingFunction = ease,
            FillBehavior = FillBehavior.Stop
        };

        slide.Completed += (_, _) =>
        {
            translate.X = 0;
            ItemsPanel.Opacity = 1;
        };

        translate.BeginAnimation(
            TranslateTransform.XProperty,
            slide);
    }

    private void OnPreviewMouseWheel(
        object sender,
        MouseWheelEventArgs e)
    {
        if (_internalDragInProgress ||
            PageCount <= 1)
        {
            return;
        }

        var delta =
            e.Delta < 0
                ? 1
                : -1;

        var target =
            Math.Clamp(
                _currentPage + delta,
                0,
                PageCount - 1);

        if (target == _currentPage)
            return;

        NavigateToPage(
            target,
            animate: true);

        e.Handled = true;
    }

    private void OnPreviewKeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            RequestClose();
            e.Handled = true;
            return;
        }

        if (PageCount <= 1)
            return;

        var delta = e.Key switch
        {
            Key.Left or Key.PageUp => -1,
            Key.Right or Key.PageDown => 1,
            _ => 0
        };

        if (delta == 0)
            return;

        NavigateToPage(
            _currentPage + delta,
            animate: true);

        e.Handled = true;
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

        var card = new Border
        {
            Padding = new Thickness(6),
            CornerRadius = new CornerRadius(18),
            Background = Brushes.Transparent,
            Child = stack,
            Cursor = Cursors.Hand,
            AllowDrop = true
        };

        // The marker lives in a non-hit-testable overlay so it does not change
        // card layout while the pointer crosses insertion positions.
        var marker = new Border
        {
            Width = 3,
            Height = 80,
            CornerRadius = new CornerRadius(1.5),
            Background = new SolidColorBrush(
                Color.FromArgb(220, 255, 255, 255)),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false
        };

        var root = new Grid
        {
            Width = 108,
            Height = 112,
            Margin = new Thickness(4)
        };

        root.Children.Add(card);
        root.Children.Add(marker);

        card.MouseEnter += (_, _) =>
        {
            if (!ReferenceEquals(_dropIndicatorCard, card))
            {
                card.Background = new SolidColorBrush(
                    Color.FromArgb(40, 255, 255, 255));
            }
        };

        card.MouseLeave += (_, _) =>
        {
            if (!ReferenceEquals(_dropIndicatorCard, card))
                card.Background = Brushes.Transparent;
        };

        card.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (e.ChangedButton != MouseButton.Left)
                return;

            _pressedItem = item;
            _pressedItemPosition = e.GetPosition(this);
        };

        card.PreviewMouseMove += (_, e) =>
            TryBeginItemDrag(item, card, e);

        card.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (e.ChangedButton != MouseButton.Left ||
                _internalDragInProgress ||
                !ReferenceEquals(_pressedItem, item))
            {
                return;
            }

            _pressedItem = null;
            LaunchItem(item);
            e.Handled = true;
        };

        card.DragOver += (_, e) =>
            HandleItemDragOver(item, card, marker, e);

        card.DragLeave += (_, e) =>
        {
            if (!IsInternalItemDrag(e.Data))
                return;

            // DragLeave can occur while moving from the card to its overlay.
            // Only clear when the pointer really left the card bounds.
            var point = e.GetPosition(card);

            if (point.X < 0 ||
                point.Y < 0 ||
                point.X > card.ActualWidth ||
                point.Y > card.ActualHeight)
            {
                ClearDropIndicator();
            }
        };

        card.Drop += (_, e) =>
            HandleItemDrop(item, card, e);

        // Standard WPF ContextMenu is attached to the whole item card, so
        // right-click works consistently on the icon, label, or empty padding.
        card.ContextMenu = BuildItemContextMenu(item);

        return root;
    }

    private void LaunchItem(AppItem item)
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
    }

    private void TryBeginItemDrag(
        AppItem item,
        Border card,
        MouseEventArgs e)
    {
        if (_internalDragInProgress ||
            e.LeftButton != MouseButtonState.Pressed ||
            !ReferenceEquals(_pressedItem, item))
        {
            return;
        }

        var current = e.GetPosition(this);

        if (Math.Abs(current.X - _pressedItemPosition.X) <
                SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _pressedItemPosition.Y) <
                SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _internalDragInProgress = true;
        _pressedItem = null;
        CancelDragPageSwitch();

        var previousOpacity = card.Opacity;
        card.Opacity = 0.52;

        try
        {
            var data = new DataObject(
                InternalItemDragFormat,
                item.Id);

            _ = DragDrop.DoDragDrop(
                card,
                data,
                DragDropEffects.Move);
        }
        finally
        {
            card.Opacity = previousOpacity;
            _internalDragInProgress = false;
            CancelDragPageSwitch();
            ClearDropIndicator();
        }

        e.Handled = true;
    }

    private void HandleItemDragOver(
        AppItem targetItem,
        Border targetCard,
        Border marker,
        DragEventArgs e)
    {
        if (!TryGetInternalDraggedItem(
                e.Data,
                out var draggedItem))
        {
            return;
        }

        e.Effects = DragDropEffects.Move;
        e.Handled = true;

        if (ReferenceEquals(draggedItem, targetItem))
        {
            ClearDropIndicator();
            return;
        }

        var targetIndex =
            _folder.Items.IndexOf(targetItem);

        if (targetIndex < 0)
        {
            ClearDropIndicator();
            return;
        }

        var pointer = e.GetPosition(targetCard);
        var insertAfter =
            pointer.X >= targetCard.ActualWidth / 2.0;

        _dropInsertionIndex =
            targetIndex + (insertAfter ? 1 : 0);

        SetDropIndicator(
            targetCard,
            marker,
            insertAfter);
    }

    private void HandleItemDrop(
        AppItem targetItem,
        Border targetCard,
        DragEventArgs e)
    {
        if (!TryGetInternalDraggedItem(
                e.Data,
                out var draggedItem))
        {
            return;
        }

        e.Effects = DragDropEffects.Move;
        e.Handled = true;

        if (ReferenceEquals(draggedItem, targetItem))
        {
            ClearDropIndicator();
            return;
        }

        var insertionIndex =
            _dropInsertionIndex ??
            _folder.Items.IndexOf(targetItem);

        ReorderItem(
            draggedItem,
            insertionIndex);
    }

    private void OnPopupDragOver(
        object sender,
        DragEventArgs e)
    {
        if (IsInternalItemDrag(e.Data))
        {
            e.Effects = DragDropEffects.Move;

            // A drop on unused space appends to the visible page rather than
            // unexpectedly jumping to the global end of a large folder.
            if (_dropIndicatorCard is null)
            {
                _dropInsertionIndex =
                    CurrentPageEndInsertionIndex;
            }

            UpdateDragPageSwitch(
                e.GetPosition(PopupCard));

            e.Handled = true;
            return;
        }

        CancelDragPageSwitch();

        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private void OnPopupDragLeave(
        object sender,
        DragEventArgs e)
    {
        if (!IsInternalItemDrag(e.Data))
            return;

        var point = e.GetPosition(this);

        if (point.X < 0 ||
            point.Y < 0 ||
            point.X > ActualWidth ||
            point.Y > ActualHeight)
        {
            CancelDragPageSwitch();
            ClearDropIndicator();
        }
    }

    private void UpdateDragPageSwitch(Point point)
    {
        if (!_internalDragInProgress ||
            PageCount <= 1)
        {
            CancelDragPageSwitch();
            return;
        }

        var delta = 0;

        if (point.X <= DragPageEdgeWidth &&
            _currentPage > 0)
        {
            delta = -1;
        }
        else if (point.X >= PopupCard.ActualWidth - DragPageEdgeWidth &&
                 _currentPage < PageCount - 1)
        {
            delta = 1;
        }

        if (delta == 0)
        {
            CancelDragPageSwitch();
            return;
        }

        if (_dragPageTimer.IsEnabled &&
            _pendingDragPageDelta == delta)
        {
            return;
        }

        _pendingDragPageDelta = delta;
        _dragPageTimer.Stop();
        _dragPageTimer.Start();
    }

    private void OnDragPageTimerTick()
    {
        _dragPageTimer.Stop();

        if (!_internalDragInProgress ||
            _pendingDragPageDelta == 0)
        {
            return;
        }

        var target =
            Math.Clamp(
                _currentPage + _pendingDragPageDelta,
                0,
                PageCount - 1);

        _pendingDragPageDelta = 0;

        if (target == _currentPage)
            return;

        NavigateToPage(
            target,
            animate: true);

        // A new page has different insertion slots. Keep the drag operation
        // alive, but require a fresh DragOver to choose the next slot.
        ClearDropIndicator();
    }

    private void CancelDragPageSwitch()
    {
        _dragPageTimer.Stop();
        _pendingDragPageDelta = 0;
    }

    private bool TryGetInternalDraggedItem(
        IDataObject data,
        out AppItem item)
    {
        item = null!;

        if (!data.GetDataPresent(InternalItemDragFormat))
            return false;

        if (data.GetData(InternalItemDragFormat) is not string itemId)
            return false;

        item = _folder.Items.FirstOrDefault(
            x => string.Equals(
                x.Id,
                itemId,
                StringComparison.Ordinal))!;

        return item is not null;
    }

    private static bool IsInternalItemDrag(IDataObject data) =>
        data.GetDataPresent(InternalItemDragFormat);

    private void SetDropIndicator(
        Border card,
        Border marker,
        bool insertAfter)
    {
        if (!ReferenceEquals(_dropIndicatorMarker, marker))
            ClearDropIndicator();

        _dropIndicatorCard = card;
        _dropIndicatorMarker = marker;

        card.Background = new SolidColorBrush(
            Color.FromArgb(30, 255, 255, 255));

        marker.HorizontalAlignment =
            insertAfter
                ? HorizontalAlignment.Right
                : HorizontalAlignment.Left;

        marker.Visibility = Visibility.Visible;
    }

    private void ClearDropIndicator()
    {
        if (_dropIndicatorMarker is not null)
            _dropIndicatorMarker.Visibility = Visibility.Collapsed;

        if (_dropIndicatorCard is not null)
            _dropIndicatorCard.Background = Brushes.Transparent;

        _dropIndicatorCard = null;
        _dropIndicatorMarker = null;
        _dropInsertionIndex = null;
    }

    private void ReorderItem(
        AppItem item,
        int insertionIndex)
    {
        var oldIndex = _folder.Items.IndexOf(item);
        if (oldIndex < 0)
            return;

        insertionIndex = Math.Clamp(
            insertionIndex,
            0,
            _folder.Items.Count);

        // insertionIndex describes a slot in the list before the source item
        // is removed. Removing an earlier source shifts later slots left.
        if (insertionIndex > oldIndex)
            insertionIndex--;

        if (insertionIndex == oldIndex)
        {
            ClearDropIndicator();
            return;
        }

        _folder.Items.RemoveAt(oldIndex);
        _folder.Items.Insert(
            Math.Clamp(
                insertionIndex,
                0,
                _folder.Items.Count),
            item);

        _save();

        _currentPage = Math.Clamp(
            _currentPage,
            0,
            PageCount - 1);

        RefreshItems();

        // The compact 3x3 preview reflects the first nine items, so it must be
        // refreshed immediately after every successful reorder.
        _refreshTile();
    }

    private ContextMenu BuildItemContextMenu(AppItem item)
    {
        var menu =
            IosContextMenuService.Create();

        menu.Items.Add(
            IosContextMenuService.Item(
                "Rename",
                () =>
                {
                    var name =
                        PromptDialog.Show(
                            "Display name",
                            item.DisplayName);

                    if (string.IsNullOrWhiteSpace(
                            name))
                    {
                        return;
                    }

                    item.DisplayName =
                        name.Trim();

                    _save();
                    RefreshItems();
                    _refreshTile();
                }));

        menu.Items.Add(
            IosContextMenuService.Separator());

        menu.Items.Add(
            IosContextMenuService.Item(
                "Remove app from folder",
                () =>
                    RemoveItem(
                        item),
                destructive:
                    true));

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

        _currentPage = Math.Clamp(
            _currentPage,
            0,
            PageCount - 1);

        RefreshItems();
        _refreshTile();
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        CancelDragPageSwitch();

        if (TryGetInternalDraggedItem(
                e.Data,
                out var draggedItem))
        {
            ReorderItem(
                draggedItem,
                _dropInsertionIndex ??
                CurrentPageEndInsertionIndex);

            e.Effects = DragDropEffects.Move;
            e.Handled = true;
            return;
        }

        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            return;

        var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
        var itemCountBefore = _folder.Items.Count;

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

        if (_folder.Items.Count > itemCountBefore)
            _currentPage = PageCount - 1;

        RefreshItems();
        _refreshTile();

        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void DragHandle_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
            return;

        try
        {
            // HostBackdropBrush is live. Moving the popup only moves the helper
            // HWND; no capture, blur or bitmap refresh is performed on the UI
            // thread during DragMove.
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // The button can be released between the mouse event and DragMove.
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
                        ApplyMaterialSettings));

            return;
        }

        ApplyMaterialSettings();
    }

    private void ApplyMaterialSettings()
    {
        var appearance =
            _settings.Current.Appearance;

        _glassBackdrop?.UpdateMaterial(
            appearance.BlurAmount,
            appearance.Saturation);

        ApplyGlassAppearance();
    }

    private void ApplyGlassAppearance()
    {
        var tintStrength =
            _settings.Current.Appearance.TintStrength;

        // Preserve the old non-GPU fallback density at the default 28% setting
        // while still making the global tint slider meaningful in both paths.
        var opacityScale =
            _glassBackdrop?.IsActive == true
                ? tintStrength
                : Math.Clamp(
                    tintStrength *
                    (0.78 / 0.28),
                    0.0,
                    1.0);

        TintLayer.Background =
            GlassAppearanceService.CreateTintBrush(
                _folder.GlassTint,
                _folder.GlassOpacity,
                opacityScale);
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

        _glassBackdrop?.BeginAnimationTracking();

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

        var scaleYAnimation =
            new DoubleAnimation(
                0.18,
                1,
                TimeSpan.FromMilliseconds(310))
            {
                EasingFunction = spring
            };

        scaleYAnimation.Completed += (_, _) =>
        {
            if (!_isClosing)
                _glassBackdrop?.EndAnimationTracking();
        };

        scale.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            scaleYAnimation);

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

        _glassBackdrop?.BeginAnimationTracking();

        // Recalculate here instead of reusing the opening vector. The popup or
        // the desktop folder may have moved while the folder was open.
        var travel = GetAnchorTravel();
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };

        var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(185))
        {
            EasingFunction = ease
        };
        fade.Completed += (_, _) =>
        {
            _glassBackdrop?.Hide();
            completed();
        };

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
