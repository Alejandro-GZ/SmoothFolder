using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;

namespace SmoothFolder.Services;

public readonly record struct ScreenPixelPoint(int X, int Y);

public readonly record struct ScreenPixelRect(
    int Left,
    int Top,
    int Right,
    int Bottom)
{
    public int Width => Math.Max(0, Right - Left);
    public int Height => Math.Max(0, Bottom - Top);

    public ScreenPixelPoint TopLeft => new(Left, Top);

    public ScreenPixelPoint Center => new(
        Left + (Width / 2),
        Top + (Height / 2));

    public bool Contains(ScreenPixelPoint point) =>
        point.X >= Left &&
        point.X < Right &&
        point.Y >= Top &&
        point.Y < Bottom;
}

public sealed record MonitorSnapshot(
    IntPtr Handle,
    string DeviceName,
    ScreenPixelRect Bounds,
    ScreenPixelRect WorkArea,
    uint DpiX,
    uint DpiY,
    bool IsPrimary)
{
    public double ScaleX => DpiX / 96.0;
    public double ScaleY => DpiY / 96.0;
}

/// <summary>
/// Monitor geometry uses physical desktop pixels end-to-end.
///
/// WPF window coordinates are DIPs whose absolute meaning changes with the
/// window's current monitor DPI. Keeping the shell/desktop layer in physical
/// pixels avoids discontinuities when a tile crosses monitors with different
/// scale factors.
/// </summary>
public static class MonitorService
{
    private const uint MonitorDefaultToNearest = 0x00000002;
    private const uint MonitorInfoPrimary = 0x00000001;
    private const int MdtEffectiveDpi = 0;

    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    public static IReadOnlyList<MonitorSnapshot> GetMonitors()
    {
        var result = new List<MonitorSnapshot>();

        _ = EnumDisplayMonitors(
            IntPtr.Zero,
            IntPtr.Zero,
            (monitor, _, _, _) =>
            {
                if (TryBuildSnapshot(monitor, out var snapshot))
                    result.Add(snapshot);

                return true;
            },
            IntPtr.Zero);

        return result;
    }

    public static MonitorSnapshot GetPrimary()
    {
        var monitors = GetMonitors();

        return monitors.FirstOrDefault(x => x.IsPrimary)
            ?? monitors.FirstOrDefault()
            ?? CreateFallbackMonitor();
    }

    public static MonitorSnapshot GetForWindow(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;

        if (hwnd == IntPtr.Zero)
            return GetPrimary();

        var monitor = MonitorFromWindow(
            hwnd,
            MonitorDefaultToNearest);

        return TryBuildSnapshot(monitor, out var snapshot)
            ? snapshot
            : GetPrimary();
    }

    public static MonitorSnapshot GetForPoint(ScreenPixelPoint point)
    {
        var monitor = MonitorFromPoint(
            new POINT { X = point.X, Y = point.Y },
            MonitorDefaultToNearest);

        return TryBuildSnapshot(monitor, out var snapshot)
            ? snapshot
            : GetPrimary();
    }

    public static MonitorSnapshot GetForRect(ScreenPixelRect rect)
    {
        var nativeRect = new RECT
        {
            Left = rect.Left,
            Top = rect.Top,
            Right = rect.Right,
            Bottom = rect.Bottom
        };

        var monitor = MonitorFromRect(
            ref nativeRect,
            MonitorDefaultToNearest);

        return TryBuildSnapshot(monitor, out var snapshot)
            ? snapshot
            : GetPrimary();
    }

    public static MonitorSnapshot? FindByDeviceName(string? deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName))
            return null;

        return GetMonitors().FirstOrDefault(
            x => string.Equals(
                x.DeviceName,
                deviceName,
                StringComparison.OrdinalIgnoreCase));
    }

    public static (uint DpiX, uint DpiY) GetWindowDpi(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;

        if (hwnd != IntPtr.Zero)
        {
            try
            {
                var dpi = GetDpiForWindow(hwnd);
                if (dpi != 0)
                    return (dpi, dpi);
            }
            catch
            {
                // Fall through to monitor DPI.
            }
        }

        var monitor = GetForWindow(window);
        return (monitor.DpiX, monitor.DpiY);
    }

    public static int DipToPixels(double value, uint dpi) =>
        (int)Math.Round(value * dpi / 96.0);

    public static double PixelsToDip(int value, uint dpi) =>
        value * 96.0 / dpi;

    public static bool PositionWindowPixels(
        Window window,
        ScreenPixelPoint topLeft)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            return false;

        return SetWindowPos(
            hwnd,
            IntPtr.Zero,
            topLeft.X,
            topLeft.Y,
            0,
            0,
            SwpNoSize |
            SwpNoZOrder |
            SwpNoActivate);
    }

    public static string GetTopologyFingerprint()
    {
        var monitors = GetMonitors()
            .OrderBy(x => x.DeviceName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Bounds.Left)
            .ThenBy(x => x.Bounds.Top)
            .ToArray();

        if (monitors.Length == 0)
            return "<no-monitors>";

        return string.Join(
            "|",
            monitors.Select(
                x => $"{x.DeviceName};" +
                     $"{x.Bounds.Left},{x.Bounds.Top},{x.Bounds.Right},{x.Bounds.Bottom};" +
                     $"{x.WorkArea.Left},{x.WorkArea.Top},{x.WorkArea.Right},{x.WorkArea.Bottom};" +
                     $"{x.DpiX},{x.DpiY};" +
                     $"{(x.IsPrimary ? 1 : 0)}"));
    }

    public static string DescribeDesktop()
    {
        var monitors = GetMonitors();

        if (monitors.Count == 0)
            return "No monitors were enumerated.";

        var builder = new StringBuilder();

        foreach (var monitor in monitors)
        {
            builder.Append(monitor.DeviceName)
                .Append(monitor.IsPrimary ? " [primary]" : string.Empty)
                .Append(" bounds=(")
                .Append(monitor.Bounds.Left).Append(',')
                .Append(monitor.Bounds.Top).Append(")-(")
                .Append(monitor.Bounds.Right).Append(',')
                .Append(monitor.Bounds.Bottom).Append(')')
                .Append(" work=(")
                .Append(monitor.WorkArea.Left).Append(',')
                .Append(monitor.WorkArea.Top).Append(")-(")
                .Append(monitor.WorkArea.Right).Append(',')
                .Append(monitor.WorkArea.Bottom).Append(')')
                .Append(" dpi=")
                .Append(monitor.DpiX).Append('x').Append(monitor.DpiY)
                .Append(" scale=")
                .Append((monitor.ScaleX * 100).ToString("0"))
                .Append('%')
                .AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    private static bool TryBuildSnapshot(
        IntPtr monitor,
        out MonitorSnapshot snapshot)
    {
        snapshot = null!;

        if (monitor == IntPtr.Zero)
            return false;

        var info = new MONITORINFOEX
        {
            cbSize = Marshal.SizeOf<MONITORINFOEX>(),
            szDevice = string.Empty
        };

        if (!GetMonitorInfo(monitor, ref info))
            return false;

        var dpiX = 96u;
        var dpiY = 96u;

        try
        {
            if (GetDpiForMonitor(
                    monitor,
                    MdtEffectiveDpi,
                    out var monitorDpiX,
                    out var monitorDpiY) == 0)
            {
                dpiX = monitorDpiX == 0 ? 96u : monitorDpiX;
                dpiY = monitorDpiY == 0 ? 96u : monitorDpiY;
            }
        }
        catch (DllNotFoundException)
        {
            // shcore.dll exists on supported Windows 11 systems, but keep the
            // geometry service safe if the API is ever unavailable.
        }
        catch (EntryPointNotFoundException)
        {
            // Use 96 DPI fallback.
        }

        snapshot = new MonitorSnapshot(
            monitor,
            info.szDevice,
            ToScreenRect(info.rcMonitor),
            ToScreenRect(info.rcWork),
            dpiX,
            dpiY,
            (info.dwFlags & MonitorInfoPrimary) != 0);

        return true;
    }

    private static MonitorSnapshot CreateFallbackMonitor() =>
        new(
            IntPtr.Zero,
            "DISPLAY",
            new ScreenPixelRect(0, 0, 1920, 1080),
            new ScreenPixelRect(0, 0, 1920, 1080),
            96,
            96,
            true);

    private static ScreenPixelRect ToScreenRect(RECT rect) =>
        new(
            rect.Left,
            rect.Top,
            rect.Right,
            rect.Bottom);

    private delegate bool MonitorEnumProc(
        IntPtr hMonitor,
        IntPtr hdcMonitor,
        IntPtr lprcMonitor,
        IntPtr dwData);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(
        IntPtr hdc,
        IntPtr lprcClip,
        MonitorEnumProc lpfnEnum,
        IntPtr dwData);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(
        IntPtr hwnd,
        uint dwFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(
        POINT pt,
        uint dwFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromRect(
        ref RECT lprc,
        uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(
        IntPtr hMonitor,
        ref MONITORINFOEX lpmi);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(
        IntPtr hmonitor,
        int dpiType,
        out uint dpiX,
        out uint dpiY);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int X,
        int Y,
        int cx,
        int cy,
        uint uFlags);
}
