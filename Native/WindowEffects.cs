using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SmoothFolder.Native;

public static class WindowEffects
{
    // Windows 11
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMWCP_ROUND = 2;
    private const int DWMSBT_TRANSIENTWINDOW = 3; // Acrylic-like system backdrop

    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOOLWINDOW = 0x00000080L;

    public static void ApplyPopupEffects(Window window, double cornerRadius = 30)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        // All DWM decoration is optional. The popup must remain usable even if
        // a Windows build, graphics configuration, or compatibility layer does
        // not support one of these attributes.
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            TrySetDwmAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, DWMWCP_ROUND);
            TrySetDwmAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, DWMSBT_TRANSIENTWINDOW);
        }

        // WPF's border can be rounded while the native HWND remains rectangular.
        // Clip the real window too, which removes the square corner artifacts.
        ApplyRoundedRegion(hwnd, cornerRadius);
        window.SizeChanged += (_, _) => ApplyRoundedRegion(hwnd, cornerRadius);

        HideFromAltTab(hwnd);
    }

    public static void HideFromAltTab(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd != IntPtr.Zero)
            HideFromAltTab(hwnd);
    }

    private static void HideFromAltTab(IntPtr hwnd)
    {
        try
        {
            var exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(exStyle | WS_EX_TOOLWINDOW));
        }
        catch (EntryPointNotFoundException)
        {
            // Cosmetic integration only; never make window creation fail.
        }
        catch (DllNotFoundException)
        {
            // Cosmetic integration only; never make window creation fail.
        }
    }

    private static void ApplyRoundedRegion(IntPtr hwnd, double cornerRadiusDip)
    {
        try
        {
            if (!GetWindowRect(hwnd, out var rect))
                return;

            var width = rect.Right - rect.Left;
            var height = rect.Bottom - rect.Top;
            if (width <= 0 || height <= 0)
                return;

            var dpi = GetDpiForWindow(hwnd);
            var scale = dpi == 0 ? 1.0 : dpi / 96.0;
            var diameter = Math.Max(2, (int)Math.Round(cornerRadiusDip * 2 * scale));

            var region = CreateRoundRectRgn(0, 0, width + 1, height + 1, diameter, diameter);
            if (region == IntPtr.Zero)
                return;

            // After a successful SetWindowRgn call, Windows owns the region.
            if (SetWindowRgn(hwnd, region, true) == 0)
                DeleteObject(region);
        }
        catch (EntryPointNotFoundException)
        {
            // Optional visual correction.
        }
        catch (DllNotFoundException)
        {
            // Optional visual correction.
        }
    }

    private static void TrySetDwmAttribute(IntPtr hwnd, int attribute, int value)
    {
        try
        {
            DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(int));
        }
        catch (EntryPointNotFoundException)
        {
            // Optional visual effect.
        }
        catch (DllNotFoundException)
        {
            // Optional visual effect.
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int dwAttribute,
        ref int pvAttribute,
        int cbAttribute);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(
        int nLeftRect,
        int nTopRect,
        int nRightRect,
        int nBottomRect,
        int nWidthEllipse,
        int nHeightEllipse);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    private static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex) =>
        IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : new IntPtr(GetWindowLong32(hWnd, nIndex));

    private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr value) =>
        IntPtr.Size == 8
            ? SetWindowLongPtr64(hWnd, nIndex, value)
            : new IntPtr(SetWindowLong32(hWnd, nIndex, value.ToInt32()));
}
