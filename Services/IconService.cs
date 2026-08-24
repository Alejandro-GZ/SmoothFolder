using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SmoothFolder.Services;

public sealed class IconService
{
    public ImageSource? GetIcon(string path)
    {
        try
        {
            // Steam/Epic suelen usar .url y muchos incluyen IconFile=...
            if (Path.GetExtension(path).Equals(".url", StringComparison.OrdinalIgnoreCase))
            {
                var iconFile = ReadInternetShortcutIcon(path);
                if (!string.IsNullOrWhiteSpace(iconFile) && File.Exists(iconFile))
                {
                    var fromIconFile = ExtractShellIcon(iconFile);
                    if (fromIconFile is not null)
                        return fromIconFile;
                }
            }

            return ExtractShellIcon(path);
        }
        catch
        {
            return null;
        }
    }

    private static string? ReadInternetShortcutIcon(string path)
    {
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (!line.StartsWith("IconFile=", StringComparison.OrdinalIgnoreCase))
                continue;

            var value = line["IconFile=".Length..].Trim().Trim('"');
            return Environment.ExpandEnvironmentVariables(value);
        }

        return null;
    }

    private static ImageSource? ExtractShellIcon(string path)
    {
        var info = new SHFILEINFO();
        var result = SHGetFileInfo(
            path,
            0,
            ref info,
            (uint)Marshal.SizeOf(info),
            SHGFI_ICON | SHGFI_LARGEICON);

        if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero)
            return null;

        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(
                info.hIcon,
                Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(96, 96));

            source.Freeze();
            return source;
        }
        finally
        {
            DestroyIcon(info.hIcon);
        }
    }

    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_LARGEICON = 0x000000000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(
        string pszPath,
        uint dwFileAttributes,
        ref SHFILEINFO psfi,
        uint cbFileInfo,
        uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
