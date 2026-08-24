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

public partial class FolderTileWindow : Window
{
    private readonly FolderConfig _folder;
    private readonly IconService _icons;
    private readonly LauncherService _launcher;
    private readonly ShortcutImportService _importer;
    private readonly Action _save;
    private readonly Action<Point> _newFolder;
    private readonly Action<FolderConfig, FolderTileWindow> _deleteFolder;
    private readonly Action _exitApp;

    private Point _mouseDown;
    private bool _isDragging;
    private FolderPopupWindow? _popup;

    public FolderTileWindow(
        FolderConfig folder,
        IconService icons,
        LauncherService launcher,
        ShortcutImportService importer,
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
        _save = save;
        _newFolder = newFolder;
        _deleteFolder = deleteFolder;
        _exitApp = exitApp;

        Left = folder.X;
        Top = folder.Y;

        Loaded += (_, _) => Refresh();
        SourceInitialized += (_, _) => WindowEffects.HideFromAltTab(this);

        PreviewMouseLeftButtonDown += OnMouseDown;
        PreviewMouseMove += OnMouseMove;
        PreviewMouseLeftButtonUp += OnMouseUp;

        DragEnter += OnDragEnter;
        DragLeave += OnDragLeave;
        Drop += OnDrop;

        MouseRightButtonUp += (_, _) => OpenContextMenu();
    }

    public void Refresh()
    {
        FolderName.Text = _folder.Name;
        PreviewGrid.Children.Clear();

        foreach (var item in _folder.Items.Take(9))
        {
            var image = new Image
            {
                Source = _icons.GetIcon(item.Path),
                Width = 19,
                Height = 19,
                Margin = new Thickness(2),
                Stretch = Stretch.Uniform
            };
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

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        _mouseDown = e.GetPosition(this);
        _isDragging = false;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _isDragging)
            return;

        var now = e.GetPosition(this);
        if (Math.Abs(now.X - _mouseDown.X) < 5 && Math.Abs(now.Y - _mouseDown.Y) < 5)
            return;

        _isDragging = true;

        try
        {
            DragMove();
        }
        catch
        {
            // DragMove puede lanzar InvalidOperationException si el botón se libera
            // justo al empezar el drag. No es un error crítico.
        }

        _folder.X = Left;
        _folder.Y = Top;
        _save();
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging)
            OpenFolder();

        _folder.X = Left;
        _folder.Y = Top;
        _save();
    }

    private void OpenFolder()
    {
        try
        {
            if (_popup is { IsVisible: true })
            {
                _popup.Close();
                return;
            }

            var popup = new FolderPopupWindow(
                _folder,
                _icons,
                _launcher,
                _importer,
                _save,
                Refresh);

            _popup = popup;
            popup.Closed += (_, _) =>
            {
                if (ReferenceEquals(_popup, popup))
                    _popup = null;
            };

            popup.ShowNear(this);
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
                    "No se pudo añadir",
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

        var open = new MenuItem { Header = "Abrir" };
        open.Click += (_, _) => OpenFolder();

        var rename = new MenuItem { Header = "Renombrar" };
        rename.Click += (_, _) =>
        {
            var result = PromptDialog.Show("Nombre de la carpeta", _folder.Name);
            if (!string.IsNullOrWhiteSpace(result))
            {
                _folder.Name = result.Trim();
                _save();
                Refresh();
            }
        };

        var add = new MenuItem { Header = "Nueva carpeta" };
        add.Click += (_, _) => _newFolder(new Point(Left, Top));

        var delete = new MenuItem { Header = "Eliminar carpeta" };
        delete.Click += (_, _) =>
        {
            if (MessageBox.Show(
                    $"¿Eliminar «{_folder.Name}»?\n\nNo se desinstala ni borra ningún juego.",
                    "Eliminar carpeta",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                _deleteFolder(_folder, this);
            }
        };

        var exit = new MenuItem { Header = "Salir de SmoothFolder" };
        exit.Click += (_, _) => _exitApp();

        menu.Items.Add(open);
        menu.Items.Add(rename);
        menu.Items.Add(add);
        menu.Items.Add(new Separator());
        menu.Items.Add(delete);
        menu.Items.Add(new Separator());
        menu.Items.Add(exit);

        menu.IsOpen = true;
    }
}
