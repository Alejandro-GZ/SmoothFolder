using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SmoothFolder.Services;

public sealed class IconService
{
    private const int PreferredIconSize = 128;

    public ImageSource? GetIcon(string path)
    {
        try
        {
            // Steam/Epic .url files often provide an explicit IconFile. Prefer
            // that source because asking Windows for the .url itself can return
            // the generic Internet Shortcut icon.
            if (Path.GetExtension(path).Equals(".url", StringComparison.OrdinalIgnoreCase))
            {
                var iconFile = ReadInternetShortcutIcon(path);
                if (!string.IsNullOrWhiteSpace(iconFile) && File.Exists(iconFile))
                {
                    var explicitIcon = ExtractHighResolutionIcon(iconFile);
                    if (explicitIcon is not null)
                        return explicitIcon;
                }
            }

            return ExtractHighResolutionIcon(path);
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

    private static ImageSource? ExtractHighResolutionIcon(string path)
    {
        // ICO files may contain several embedded sizes. Decode the largest
        // frame directly instead of asking the legacy Shell API for a 32px icon.
        if (Path.GetExtension(path).Equals(".ico", StringComparison.OrdinalIgnoreCase))
        {
            var ico = TryLoadLargestIcoFrame(path);
            if (ico is not null)
                return ico;
        }

        // Windows Vista+ Shell image factory can provide a much larger icon
        // than SHGetFileInfo(SHGFI_LARGEICON), avoiding the blurry upscale that
        // was visible in the folder grid.
        var shellImage = TryGetShellImage(path, PreferredIconSize);
        if (shellImage is not null)
            return shellImage;

        // Safe fallback for unusual shell items.
        return ExtractLegacyShellIcon(path);
    }

    private static ImageSource? TryLoadLargestIcoFrame(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);

            var frame = decoder.Frames
                .OrderByDescending(x => x.PixelWidth * x.PixelHeight)
                .FirstOrDefault();

            if (frame is null)
                return null;

            frame.Freeze();
            return frame;
        }
        catch
        {
            return null;
        }
    }

    private static ImageSource? TryGetShellImage(string path, int size)
    {
        IShellItemImageFactory? factory = null;
        IntPtr hBitmap = IntPtr.Zero;

        try
        {
            var iid = typeof(IShellItemImageFactory).GUID;
            var hr = SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out factory);
            if (hr < 0 || factory is null)
                return null;

            hr = factory.GetImage(
                new SIZE { cx = size, cy = size },
                SIIGBF.ICONONLY | SIIGBF.BIGGERSIZEOK,
                out hBitmap);

            if (hr < 0 || hBitmap == IntPtr.Zero)
                return null;

            var source = Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());

            source.Freeze();
            return source;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (hBitmap != IntPtr.Zero)
                DeleteObject(hBitmap);

            if (factory is not null && Marshal.IsComObject(factory))
                Marshal.FinalReleaseComObject(factory);
        }
    }

    private static ImageSource? ExtractLegacyShellIcon(string path)
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
                BitmapSizeOptions.FromEmptyOptions());

            source.Freeze();
            return source;
        }
        finally
        {
            DestroyIcon(info.hIcon);
        }
    }

    [Flags]
    private enum SIIGBF : uint
    {
        RESIZETOFIT = 0x00,
        BIGGERSIZEOK = 0x01,
        MEMORYONLY = 0x02,
        ICONONLY = 0x04,
        THUMBNAILONLY = 0x08,
        INCACHEONLY = 0x10
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int cx;
        public int cy;
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(SIZE size, SIIGBF flags, out IntPtr phbm);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
        IntPtr pbc,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

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

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);
}
