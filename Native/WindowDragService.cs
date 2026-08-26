using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SmoothFolder.Native;

public static class WindowDragService
{
    private const int WmNcLButtonDown = 0x00A1;
    private const int HtCaption = 2;

    public static bool BeginMove(
        Window window)
    {
        ArgumentNullException.ThrowIfNull(
            window);

        var hwnd =
            new WindowInteropHelper(
                window).Handle;

        if (hwnd == IntPtr.Zero)
            return false;

        _ = ReleaseCapture();

        _ = SendMessage(
            hwnd,
            WmNcLButtonDown,
            new IntPtr(
                HtCaption),
            IntPtr.Zero);

        return true;
    }

    [DllImport(
        "user32.dll",
        SetLastError = true)]
    [return:
        MarshalAs(
            UnmanagedType.Bool)]
    private static extern bool ReleaseCapture();

    [DllImport(
        "user32.dll",
        CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(
        IntPtr hWnd,
        int message,
        IntPtr wParam,
        IntPtr lParam);
}
