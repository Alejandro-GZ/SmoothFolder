using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using SmoothFolder.Native;

namespace SmoothFolder.Services;

/// <summary>
/// Discovers Explorer's desktop hierarchy and keeps SmoothFolder's compact
/// folder tiles directly above the Explorer desktop host in the top-level
/// Z-order.
///
/// Folder tiles remain independent top-level WPF layered windows. They are not
/// made WS_CHILD windows (which broke per-pixel WPF rendering on some Windows
/// 11 systems), and they are not owned by WorkerW/Progman (owned top-level
/// windows can float above normal application windows).
///
/// The result is a compatibility layer: tiles visually live on the desktop,
/// normal application windows stay above them, and the larger folder popup
/// remains a conventional top-level window.
/// </summary>
public sealed class DesktopHostService
{
    private const uint WorkerWMessage = 0x052C;
    private const uint SmtoAbortIfHung = 0x0002;

    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private const uint SwpShowWindow = 0x0040;
    private const uint SwpNoOwnerZOrder = 0x0200;

    private const int SwShowNoActivate = 4;

    private const int GwlStyle = -16;
    private const int GwlHwndParent = -8;
    private const long WsChild = 0x40000000L;
    private const long WsPopup = unchecked((long)0x80000000);

    private static readonly IntPtr HwndTop = IntPtr.Zero;

    private IntPtr _progman;
    private IntPtr _shellView;
    private IntPtr _desktopHost;

    private readonly HashSet<IntPtr> _configuredTiles = [];
    private bool _reportedCurrentHost;
    private bool _reportedDiscoveryFailure;

    public bool RefreshHost()
    {
        if (IsHostValid())
            return true;

        _progman = IntPtr.Zero;
        _shellView = IntPtr.Zero;
        _desktopHost = IntPtr.Zero;
        _reportedCurrentHost = false;

        var discovered = DiscoverDesktopHost();
        if (!discovered && !_reportedDiscoveryFailure)
        {
            _reportedDiscoveryFailure = true;
            CrashLogService.LogMessage(
                "Desktop host discovery failed",
                "SmoothFolder could not find a valid Progman/WorkerW + SHELLDLL_DefView hierarchy. " +
                "Folder tiles will remain normal hidden-from-taskbar WPF windows.");
        }

        return discovered;
    }

    public bool TryAttach(Window window, double screenX, double screenY)
    {
        if (!RefreshHost())
            return false;

        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero || !IsWindow(hwnd))
        {
            CrashLogService.LogMessage(
                "Desktop tile attachment failed",
                "The WPF window does not have a valid HWND.");
            return false;
        }

        if (!PrepareDesktopTile(window, hwnd))
            return false;

        return MoveToScreen(window, screenX, screenY);
    }

    public bool EnsureAttached(Window window, double screenX, double screenY)
    {
        if (!RefreshHost())
            return false;

        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero || !IsWindow(hwnd))
            return false;

        if (!PrepareDesktopTile(window, hwnd))
            return false;

        return MoveToScreen(window, screenX, screenY);
    }

    public bool MoveToScreen(Window window, double screenX, double screenY)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero || !IsWindow(hwnd))
            return false;

        if (!RefreshHost())
            return false;

        if (!GetWindowRect(hwnd, out var rect))
            return false;

        var dpi = GetSafeDpi(hwnd);
        var targetX = DipToPixel(screenX, dpi);
        var targetY = DipToPixel(screenY, dpi);

        var width = Math.Max(1, rect.Right - rect.Left);
        var height = Math.Max(1, rect.Bottom - rect.Top);

        var insertAfter = GetDesktopInsertAfter(hwnd);

        return SetWindowPos(
            hwnd,
            insertAfter,
            targetX,
            targetY,
            width,
            height,
            SwpNoActivate |
            SwpNoOwnerZOrder |
            SwpShowWindow);
    }

    public static bool IsWindowAlive(Window window)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            return hwnd != IntPtr.Zero && IsWindow(hwnd);
        }
        catch
        {
            return false;
        }
    }

    public static Rect GetScreenBoundsDip(Window window)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var rect))
                return new Rect(window.Left, window.Top, window.ActualWidth, window.ActualHeight);

            var dpi = GetSafeDpi(hwnd);
            return new Rect(
                PixelToDip(rect.Left, dpi),
                PixelToDip(rect.Top, dpi),
                PixelToDip(rect.Right - rect.Left, dpi),
                PixelToDip(rect.Bottom - rect.Top, dpi));
        }
        catch
        {
            return new Rect(window.Left, window.Top, window.ActualWidth, window.ActualHeight);
        }
    }

    public static Point GetCursorScreenPositionDip(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        var dpi = hwnd == IntPtr.Zero ? 96u : GetSafeDpi(hwnd);

        if (!GetCursorPos(out var point))
            return new Point();

        return new Point(
            PixelToDip(point.X, dpi),
            PixelToDip(point.Y, dpi));
    }

    private bool PrepareDesktopTile(Window window, IntPtr hwnd)
    {
        try
        {
            var originalStyle = GetWindowLongPtr(hwnd, GwlStyle).ToInt64();
            var popupStyle = (originalStyle | WsPopup) & ~WsChild;

            Marshal.SetLastPInvokeError(0);
            _ = SetWindowLongPtr(hwnd, GwlStyle, new IntPtr(popupStyle));
            var styleError = Marshal.GetLastPInvokeError();

            if (styleError != 0)
            {
                LogAttachFailure(
                    hwnd,
                    $"Could not restore top-level popup style. Win32 error: {styleError}.");
                return false;
            }

            // Clear the native owner used by patch 0011. Keeping WorkerW/Progman
            // as GWLP_HWNDPARENT makes this an owned top-level window, and owned
            // windows are constrained to remain above their owner. That was the
            // source of tiles occasionally covering normal application windows.
            Marshal.SetLastPInvokeError(0);
            _ = SetWindowLongPtr(hwnd, GwlHwndParent, IntPtr.Zero);
            var ownerError = Marshal.GetLastPInvokeError();

            if (ownerError != 0)
            {
                LogAttachFailure(
                    hwnd,
                    $"Could not clear the Explorer desktop owner. Win32 error: {ownerError}.");
                return false;
            }

            WindowEffects.ConfigureDesktopTile(window);

            _ = SetWindowPos(
                hwnd,
                GetDesktopInsertAfter(hwnd),
                0,
                0,
                0,
                0,
                SwpNoSize |
                SwpNoMove |
                SwpNoActivate |
                SwpNoOwnerZOrder |
                SwpFrameChanged |
                SwpShowWindow);

            _ = ShowWindow(hwnd, SwShowNoActivate);

            if (_configuredTiles.Add(hwnd))
            {
                CrashLogService.LogMessage(
                    "Desktop tile attached",
                    $"Tile {FormatHandle(hwnd)} is using desktop Z-order mode. " +
                    $"Desktop host={DescribeWindow(_desktopHost)}; Visible={IsWindowVisible(hwnd)}.");
            }

            return true;
        }
        catch (Exception ex)
        {
            CrashLogService.Log(ex, "Attaching desktop tile");
            return false;
        }
    }

    private IntPtr GetDesktopInsertAfter(IntPtr tileHwnd)
    {
        // hWndInsertAfter names the window that should precede the positioned
        // window. The window immediately above the desktop host is therefore
        // the ideal anchor: SmoothFolder lands between that window and the
        // desktop host, beneath all normal application windows.
        var immediatelyAboveDesktop = GetWindow(
            _desktopHost,
            GetWindowCommand.Previous);

        // If this tile is already immediately above Explorer, use the window
        // above the tile so SetWindowPos does not receive the tile itself.
        if (immediatelyAboveDesktop == tileHwnd)
        {
            immediatelyAboveDesktop = GetWindow(
                tileHwnd,
                GetWindowCommand.Previous);
        }

        // With no window above the desktop host, HWND_TOP is safe: there are no
        // normal top-level windows to cover. Any subsequently activated app will
        // naturally enter the Z-order above the WS_EX_NOACTIVATE tile.
        return immediatelyAboveDesktop == IntPtr.Zero
            ? HwndTop
            : immediatelyAboveDesktop;
    }

    private bool DiscoverDesktopHost()
    {
        _progman = FindWindow("Progman", null);
        if (_progman == IntPtr.Zero)
            return false;

        _ = SendMessageTimeout(
            _progman,
            WorkerWMessage,
            new UIntPtr(0xD),
            new IntPtr(0x1),
            SmtoAbortIfHung,
            1000,
            out _);

        _ = SendMessageTimeout(
            _progman,
            WorkerWMessage,
            UIntPtr.Zero,
            IntPtr.Zero,
            SmtoAbortIfHung,
            1000,
            out _);

        _shellView = FindWindowEx(
            _progman,
            IntPtr.Zero,
            "SHELLDLL_DefView",
            null);

        if (_shellView == IntPtr.Zero)
        {
            _ = EnumWindows((topLevel, _) =>
            {
                var shellView = FindWindowEx(
                    topLevel,
                    IntPtr.Zero,
                    "SHELLDLL_DefView",
                    null);

                if (shellView == IntPtr.Zero)
                    return true;

                _shellView = shellView;
                return false;
            }, IntPtr.Zero);
        }

        if (_shellView == IntPtr.Zero)
            return false;

        _desktopHost = GetParent(_shellView);
        if (_desktopHost == IntPtr.Zero)
            _desktopHost = _progman;

        if (!IsHostValid())
            return false;

        _reportedDiscoveryFailure = false;

        if (!_reportedCurrentHost)
        {
            _reportedCurrentHost = true;
            CrashLogService.LogMessage(
                "Desktop host discovered",
                $"Progman={DescribeWindow(_progman)}{Environment.NewLine}" +
                $"SHELLDLL_DefView={DescribeWindow(_shellView)}{Environment.NewLine}" +
                $"Desktop host={DescribeWindow(_desktopHost)}");
        }

        return true;
    }

    private bool IsHostValid()
    {
        if (_desktopHost == IntPtr.Zero ||
            _shellView == IntPtr.Zero ||
            !IsWindow(_desktopHost) ||
            !IsWindow(_shellView))
        {
            return false;
        }

        return GetParent(_shellView) == _desktopHost;
    }

    private void LogAttachFailure(IntPtr hwnd, string reason)
    {
        CrashLogService.LogMessage(
            "Desktop tile attachment failed",
            $"{reason}{Environment.NewLine}" +
            $"Tile={DescribeWindow(hwnd)}{Environment.NewLine}" +
            $"Desktop host={DescribeWindow(_desktopHost)}{Environment.NewLine}" +
            $"SHELLDLL_DefView={DescribeWindow(_shellView)}");
    }

    private static string DescribeWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return "NULL";

        var className = new StringBuilder(256);
        _ = GetClassName(hwnd, className, className.Capacity);

        var visible = IsWindowVisible(hwnd);

        if (GetWindowRect(hwnd, out var rect))
        {
            return $"{FormatHandle(hwnd)} class='{className}' " +
                   $"visible={visible} rect=({rect.Left},{rect.Top})-({rect.Right},{rect.Bottom})";
        }

        return $"{FormatHandle(hwnd)} class='{className}' visible={visible} rect=<unavailable>";
    }

    private static string FormatHandle(IntPtr hwnd) =>
        $"0x{hwnd.ToInt64():X}";

    private static uint GetSafeDpi(IntPtr hwnd)
    {
        try
        {
            var dpi = GetDpiForWindow(hwnd);
            return dpi == 0 ? 96u : dpi;
        }
        catch
        {
            return 96u;
        }
    }

    private static int DipToPixel(double value, uint dpi) =>
        (int)Math.Round(value * dpi / 96.0);

    private static double PixelToDip(int value, uint dpi) =>
        value * 96.0 / dpi;

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    private enum GetWindowCommand : uint
    {
        Next = 2,
        Previous = 3,
        Owner = 4
    }

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

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(
        IntPtr hWndParent,
        IntPtr hWndChildAfter,
        string? lpszClass,
        string? lpszWindow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr GetParent(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, GetWindowCommand uCmd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int GetClassName(
        IntPtr hWnd,
        StringBuilder lpClassName,
        int nMaxCount);

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

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    private static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex) =>
        IntPtr.Size == 8
            ? GetWindowLongPtr64(hWnd, nIndex)
            : new IntPtr(GetWindowLong32(hWnd, nIndex));

    private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr value) =>
        IntPtr.Size == 8
            ? SetWindowLongPtr64(hWnd, nIndex, value)
            : new IntPtr(SetWindowLong32(hWnd, nIndex, value.ToInt32()));

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd,
        uint Msg,
        UIntPtr wParam,
        IntPtr lParam,
        uint fuFlags,
        uint uTimeout,
        out UIntPtr lpdwResult);
}
