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

        SourceInitialized += (_, _) => WindowEffects.ApplyPopupEffects(this, 30);
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                Close();
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

        left = Math.Max(work.Left + 12, Math.Min(left, work.Right - Width - 12));

        if (top + Height > work.Bottom - 12)
            top = tile.Top - Height - 8;

        top = Math.Max(work.Top + 12, top);

        Left = left;
        Top = top;

        Show();
        Activate();
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
                FontSize = 14,
                Margin = new Thickness(6, 14, 0, 0)
            });
        }
    }

    private FrameworkElement BuildItem(AppItem item)
    {
        var icon = new Image
        {
            Source = _icons.GetIcon(item.Path),
            Width = 62,
            Height = 62,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var label = new TextBlock
        {
            Text = item.DisplayName,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            FontSize = 12,
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
            Width = 116,
            Height = 112,
            Padding = new Thickness(8),
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
                Close();
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

        border.MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            OpenItemContextMenu(item, border);
        };

        return border;
    }

    private void OpenItemContextMenu(AppItem item, FrameworkElement target)
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

        var remove = new MenuItem { Header = "Remove from folder" };
        remove.Click += (_, _) =>
        {
            _folder.Items.Remove(item);

            // If the shortcut was copied to AppData, delete that private copy.
            // The user's original file is never touched.
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
                // The configuration can still be cleaned even if a private copy
                // could not be deleted.
            }

            _save();
            RefreshItems();
            _refreshTile();
        };

        menu.Items.Add(rename);
        menu.Items.Add(remove);
        target.ContextMenu = menu;
        menu.IsOpen = true;
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

        if (PopupCard.RenderTransform is not ScaleTransform scale)
            return;

        scale.ScaleX = 0.94;
        scale.ScaleY = 0.94;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150))
            {
                EasingFunction = ease
            });

        scale.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.94, 1, TimeSpan.FromMilliseconds(170))
            {
                EasingFunction = ease
            });

        scale.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.94, 1, TimeSpan.FromMilliseconds(170))
            {
                EasingFunction = ease
            });
    }
}
