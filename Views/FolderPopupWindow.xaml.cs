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

        Loaded += (_, _) =>
        {
            RefreshItems();
            AnimateOpen();
        };

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
        Owner = tile;

        var work = SystemParameters.WorkArea;
        var left = tile.Left + (tile.Width / 2) - (Width / 2);
        var top = tile.Top + tile.Height + 8;
        var opensBelow = true;

        left = Math.Max(work.Left + 12, Math.Min(left, work.Right - Width - 12));

        if (top + Height > work.Bottom - 12)
        {
            top = tile.Top - Height - 8;
            opensBelow = false;
        }

        top = Math.Max(work.Top + 12, top);

        Left = left;
        Top = top;

        // Scale from the folder's horizontal position, which makes the popup
        // feel attached to the desktop folder rather than appearing centrally.
        var originX = Math.Clamp(
            (tile.Left + (tile.Width / 2) - Left) / Width,
            0.08,
            0.92);

        PopupRoot.RenderTransformOrigin = new Point(originX, opensBelow ? 0.03 : 0.97);

        Show();
        Activate();
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
        Opacity = 0;

        if (PopupRoot.RenderTransform is not ScaleTransform scale)
            return;

        scale.ScaleX = 0.80;
        scale.ScaleY = 0.80;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = ease
            });

        scale.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.80, 1, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = ease
            });

        scale.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.80, 1, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = ease
            });
    }

    private void AnimateClose(Action completed)
    {
        if (PopupRoot.RenderTransform is not ScaleTransform scale)
        {
            completed();
            return;
        }

        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
        var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(140))
        {
            EasingFunction = ease
        };
        fade.Completed += (_, _) => completed();

        BeginAnimation(OpacityProperty, fade);

        scale.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            new DoubleAnimation(scale.ScaleX, 0.84, TimeSpan.FromMilliseconds(155))
            {
                EasingFunction = ease
            });

        scale.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            new DoubleAnimation(scale.ScaleY, 0.84, TimeSpan.FromMilliseconds(155))
            {
                EasingFunction = ease
            });
    }
}
