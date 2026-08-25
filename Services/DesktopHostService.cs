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
/// SmoothFolder does not assume that all Windows 11 builds expose the same
/// Progman/WorkerW hierarchy. Discovery classifies the current Explorer layout
/// and validates both HWND identity and Explorer process ownership.
///
/// Folder tiles remain independent top-level WPF layered windows. They are not
/// made WS_CHILD windows (which broke per-pixel WPF rendering on some Windows
/// 11 systems), and they are not owned by WorkerW/Progman (owned top-level
/// windows can float above normal application windows).
/// </summary>
public sealed class DesktopHostService
{
    private const uint WorkerWMessage = 0x052C;
    private const uint SmtoAbortIfHung = 0x0002;

    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private const uint SwpShowWindow = 0x0040;
    private const uint SwpNoOwnerZOrder = 0x0200;

    private const int SwShowNoActivate = 4;

    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;
    private const int GwlHwndParent = -8;

    private const long WsChild = 0x40000000L;
    private const long WsPopup = unchecked((long)0x80000000);
    private const long WsExLayered = 0x00080000L;
    private const long WsExNoRedirectionBitmap = 0x00200000L;

    private static readonly IntPtr HwndTop = IntPtr.Zero;

    private IntPtr _progman;
    private IntPtr _shellView;
    private IntPtr _desktopHost;
    private IntPtr _wallpaperWorker;
    private uint _explorerProcessId;

    private readonly HashSet<IntPtr> _configuredTiles = [];
    private bool _reportedCurrentHost;
    private bool _reportedDiscoveryFailure;

    public long Generation { get; private set; }

    public DesktopShellLayout Layout { get; private set; } =
        DesktopShellLayout.Unknown;

    public void InvalidateHost(string reason)
    {
        if (HasHostState())
        {
            CrashLogService.LogMessage(
                "Desktop host invalidated",
                $"{reason}{Environment.NewLine}" +
                $"Previous generation={Generation}; layout={Layout}; " +
                $"Explorer PID={_explorerProcessId}; host={FormatHandle(_desktopHost)}.");
        }

        ResetHostState();
    }

    public bool RefreshHost()
    {
        if (ValidateHost(out _))
            return true;

        if (HasHostState() && !ValidateHost(out var validationFailure))
        {
            CrashLogService.LogMessage(
                "Desktop host validation failed",
                validationFailure);
        }

        ResetHostState();

        var discovered = DiscoverDesktopHost();
        if (!discovered && !_reportedDiscoveryFailure)
        {
            _reportedDiscoveryFailure = true;
            CrashLogService.LogMessage(
                "Desktop host discovery failed",
                "SmoothFolder could not find a supported Explorer desktop hierarchy. " +
                "Folder tiles will remain normal hidden-from-taskbar WPF windows.");
        }

        return discovered;
    }

    public bool TryAttachPixels(
        Window window,
        ScreenPixelPoint topLeft)
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

        return MoveToScreenPixels(window, topLeft);
    }

    public bool EnsureAttachedPixels(
        Window window,
        ScreenPixelPoint topLeft)
    {
        if (!RefreshHost())
            return false;

        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero || !IsWindow(hwnd))
            return false;

        if (!PrepareDesktopTile(window, hwnd))
            return false;

        return MoveToScreenPixels(window, topLeft);
    }

    public bool MoveToScreenPixels(
        Window window,
        ScreenPixelPoint topLeft)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero || !IsWindow(hwnd))
            return false;

        if (!RefreshHost())
            return false;

        if (!GetWindowRect(hwnd, out var rect))
            return false;

        var width = Math.Max(1, rect.Right - rect.Left);
        var height = Math.Max(1, rect.Bottom - rect.Top);

        return SetWindowPos(
            hwnd,
            GetDesktopInsertAfter(hwnd),
            topLeft.X,
            topLeft.Y,
            width,
            height,
            SwpNoActivate |
            SwpNoOwnerZOrder |
            SwpShowWindow);
    }

    public bool BeginTileDrag(Window window)
    {
        if (!RefreshHost())
            return false;

        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero || !IsWindow(hwnd))
            return false;

        if (!_configuredTiles.Contains(hwnd) &&
            !PrepareDesktopTile(window, hwnd))
        {
            return false;
        }

        // Promote only once: above every SmoothFolder tile/backdrop helper,
        // but still below the first normal application/shell window.
        return SetWindowPos(
            hwnd,
            GetDesktopDragInsertAfter(hwnd),
            0,
            0,
            0,
            0,
            SwpNoSize |
            SwpNoMove |
            SwpNoActivate |
            SwpNoOwnerZOrder |
            SwpShowWindow);
    }

    public bool MoveToScreenPixelsPreservingZOrder(
        Window window,
        ScreenPixelPoint topLeft)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero || !IsWindow(hwnd))
            return false;

        if (!GetWindowRect(hwnd, out var rect))
            return false;

        var width = Math.Max(1, rect.Right - rect.Left);
        var height = Math.Max(1, rect.Bottom - rect.Top);

        // Once dragging starts, changing only coordinates keeps DWM from
        // rebuilding the transparent desktop-band Z-order on every mouse move.
        return SetWindowPos(
            hwnd,
            IntPtr.Zero,
            topLeft.X,
            topLeft.Y,
            width,
            height,
            SwpNoZOrder |
            SwpNoActivate |
            SwpNoOwnerZOrder);
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

    public static ScreenPixelRect GetScreenBoundsPixels(Window window)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;

            if (hwnd == IntPtr.Zero ||
                !GetWindowRect(hwnd, out var rect))
            {
                var monitor = MonitorService.GetForWindow(window);
                var x = MonitorService.DipToPixels(window.Left, monitor.DpiX);
                var y = MonitorService.DipToPixels(window.Top, monitor.DpiY);
                var width = MonitorService.DipToPixels(
                    window.ActualWidth > 0 ? window.ActualWidth : window.Width,
                    monitor.DpiX);
                var height = MonitorService.DipToPixels(
                    window.ActualHeight > 0 ? window.ActualHeight : window.Height,
                    monitor.DpiY);

                return new ScreenPixelRect(
                    x,
                    y,
                    x + width,
                    y + height);
            }

            return new ScreenPixelRect(
                rect.Left,
                rect.Top,
                rect.Right,
                rect.Bottom);
        }
        catch
        {
            return new ScreenPixelRect(0, 0, 0, 0);
        }
    }

    public static ScreenPixelPoint GetCursorScreenPositionPixels()
    {
        if (!GetCursorPos(out var point))
            return new ScreenPixelPoint();

        return new ScreenPixelPoint(
            point.X,
            point.Y);
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

            // A tile must remain an independent top-level window. Only touch
            // GWLP_HWNDPARENT when there is actually an owner. Rewriting a
            // zero owner on every maintenance/recovery pass is unnecessary and
            // can race with WPF/Explorer teardown, producing ERROR_INVALID_WINDOW_HANDLE
            // even though the tile HWND is otherwise still usable.
            if (!EnsureNoNativeOwner(hwnd))
                return false;

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
                    $"Layout={Layout}; host={DescribeWindow(_desktopHost)}; " +
                    $"Visible={IsWindowVisible(hwnd)}.");
            }

            return true;
        }
        catch (Exception ex)
        {
            CrashLogService.Log(ex, "Attaching desktop tile");
            return false;
        }
    }

    private bool EnsureNoNativeOwner(IntPtr hwnd)
    {
        var owner = GetWindow(hwnd, GetWindowCommand.Owner);
        if (owner == IntPtr.Zero)
            return true;

        Marshal.SetLastPInvokeError(0);
        _ = SetWindowLongPtr(hwnd, GwlHwndParent, IntPtr.Zero);
        var error = Marshal.GetLastPInvokeError();

        // The owner may disappear while Explorer is rebuilding. If the owner
        // is already gone after the write attempt, the desired state has been
        // reached and the operation is effectively successful.
        if (GetWindow(hwnd, GetWindowCommand.Owner) == IntPtr.Zero)
            return true;

        if (error != 0)
        {
            LogAttachFailure(
                hwnd,
                $"Could not clear native owner {FormatHandle(owner)}. Win32 error: {error}.");
            return false;
        }

        LogAttachFailure(
            hwnd,
            $"Native owner {FormatHandle(owner)} remained set after GWLP_HWNDPARENT was cleared.");
        return false;
    }

    private IntPtr GetDesktopInsertAfter(IntPtr tileHwnd)
    {
        // The compact tile is kept immediately above the top-level window that
        // owns Explorer's icon view. This works for both:
        //
        //   Classic: top-level WorkerW -> SHELLDLL_DefView
        //   Raised:  top-level Progman -> layered SHELLDLL_DefView
        //
        // Normal application windows remain above the tile.
        var immediatelyAboveDesktop = GetWindow(
            _desktopHost,
            GetWindowCommand.Previous);

        if (immediatelyAboveDesktop == tileHwnd)
        {
            immediatelyAboveDesktop = GetWindow(
                tileHwnd,
                GetWindowCommand.Previous);
        }

        return immediatelyAboveDesktop == IntPtr.Zero
            ? HwndTop
            : immediatelyAboveDesktop;
    }

    private IntPtr GetDesktopDragInsertAfter(IntPtr tileHwnd)
    {
        var candidate = GetWindow(
            _desktopHost,
            GetWindowCommand.Previous);

        while (candidate != IntPtr.Zero)
        {
            var isSmoothFolderTile =
                candidate == tileHwnd ||
                _configuredTiles.Contains(candidate);

            var isSmoothFolderBackdrop =
                ClassEquals(
                    candidate,
                    "SmoothFolder.GpuGlassBackdrop");

            if (!isSmoothFolderTile &&
                !isSmoothFolderBackdrop)
            {
                return candidate;
            }

            candidate = GetWindow(
                candidate,
                GetWindowCommand.Previous);
        }

        return HwndTop;
    }

    private void ResetHostState()
    {
        _progman = IntPtr.Zero;
        _shellView = IntPtr.Zero;
        _desktopHost = IntPtr.Zero;
        _wallpaperWorker = IntPtr.Zero;
        _explorerProcessId = 0;
        Layout = DesktopShellLayout.Unknown;
        _reportedCurrentHost = false;
        _configuredTiles.Clear();
    }

    private bool DiscoverDesktopHost()
    {
        _progman = FindWindow("Progman", null);
        if (_progman == IntPtr.Zero)
            return false;

        _ = GetWindowThreadProcessId(_progman, out _explorerProcessId);
        if (_explorerProcessId == 0)
            return false;

        var raisedDesktop = HasExtendedStyle(
            _progman,
            WsExNoRedirectionBitmap);

        // Do not send Explorer's undocumented WorkerW message on every
        // discovery. SmoothFolder only needs the existing icon host for its
        // Z-order anchor. Forcing WorkerW creation is reserved as a last-resort
        // wake-up if SHELLDLL_DefView cannot be found at all.
        _shellView = FindExplorerShellView();

        if (_shellView == IntPtr.Zero)
        {
            RequestDesktopHierarchy(raisedDesktop);
            _shellView = FindExplorerShellView();
        }

        if (_shellView == IntPtr.Zero)
        {
            LogDiscoverySnapshot(
                "SHELLDLL_DefView was not found after the compatibility wake-up.");
            return false;
        }

        _desktopHost = GetParent(_shellView);
        if (_desktopHost == IntPtr.Zero)
            _desktopHost = _progman;

        if (!BelongsToExplorer(_shellView) ||
            !BelongsToExplorer(_desktopHost))
        {
            LogDiscoverySnapshot(
                "The discovered desktop hierarchy is not fully owned by the Progman Explorer process.");
            return false;
        }

        Layout = ClassifyLayout(raisedDesktop);
        _wallpaperWorker = FindWallpaperWorker();

        if (!ValidateHost(out var validationFailure))
        {
            LogDiscoverySnapshot(validationFailure);
            return false;
        }

        Generation++;
        _reportedDiscoveryFailure = false;

        if (!_reportedCurrentHost)
        {
            _reportedCurrentHost = true;

            var progmanExStyle = GetWindowLongPtr(
                _progman,
                GwlExStyle).ToInt64();

            var shellViewExStyle = GetWindowLongPtr(
                _shellView,
                GwlExStyle).ToInt64();

            CrashLogService.LogMessage(
                "Desktop host discovered",
                $"Generation={Generation}; layout={Layout}; Explorer PID={_explorerProcessId}{Environment.NewLine}" +
                $"Progman={DescribeWindow(_progman)} exStyle=0x{progmanExStyle:X}{Environment.NewLine}" +
                $"SHELLDLL_DefView={DescribeWindow(_shellView)} exStyle=0x{shellViewExStyle:X}{Environment.NewLine}" +
                $"Desktop host={DescribeWindow(_desktopHost)}{Environment.NewLine}" +
                $"Wallpaper WorkerW={DescribeWindow(_wallpaperWorker)}");
        }

        return true;
    }

    private IntPtr FindExplorerShellView()
    {
        // Fast paths first.
        var direct = FindWindowEx(
            _progman,
            IntPtr.Zero,
            "SHELLDLL_DefView",
            null);

        if (direct != IntPtr.Zero && BelongsToExplorer(direct))
            return direct;

        // Raised-desktop variants can move shell children while keeping them
        // somewhere under Progman. EnumChildWindows walks descendants too.
        IntPtr descendant = IntPtr.Zero;

        _ = EnumChildWindows(
            _progman,
            (child, _) =>
            {
                if (!ClassEquals(child, "SHELLDLL_DefView") ||
                    !BelongsToExplorer(child))
                {
                    return true;
                }

                descendant = child;
                return false;
            },
            IntPtr.Zero);

        if (descendant != IntPtr.Zero)
            return descendant;

        // Classic layouts commonly host SHELLDLL_DefView under a top-level
        // WorkerW sibling rather than under Progman.
        IntPtr topLevelShellView = IntPtr.Zero;

        _ = EnumWindows(
            (topLevel, _) =>
            {
                if (!BelongsToExplorer(topLevel))
                    return true;

                var candidate = FindWindowEx(
                    topLevel,
                    IntPtr.Zero,
                    "SHELLDLL_DefView",
                    null);

                if (candidate == IntPtr.Zero ||
                    !BelongsToExplorer(candidate))
                {
                    return true;
                }

                topLevelShellView = candidate;
                return false;
            },
            IntPtr.Zero);

        return topLevelShellView;
    }

    private DesktopShellLayout ClassifyLayout(bool raisedDesktop)
    {
        var hostClass = GetClassNameText(_desktopHost);

        if (_desktopHost == _progman && raisedDesktop)
            return DesktopShellLayout.RaisedProgman;

        if (string.Equals(
                hostClass,
                "WorkerW",
                StringComparison.Ordinal))
        {
            return DesktopShellLayout.ClassicWorkerW;
        }

        if (_desktopHost == _progman)
            return DesktopShellLayout.ProgmanHosted;

        // Accept an unknown class only when it is structurally valid and owned
        // by the same Explorer process. This is intentionally conservative but
        // gives future Windows builds a usable compatibility path.
        return DesktopShellLayout.CompatibleUnknown;
    }

    private IntPtr FindWallpaperWorker()
    {
        if (Layout == DesktopShellLayout.RaisedProgman ||
            Layout == DesktopShellLayout.ProgmanHosted)
        {
            return FindWindowEx(
                _progman,
                IntPtr.Zero,
                "WorkerW",
                null);
        }

        if (Layout == DesktopShellLayout.ClassicWorkerW)
        {
            // The classic wallpaper WorkerW is often the next top-level WorkerW
            // sibling after the WorkerW that hosts SHELLDLL_DefView.
            var sibling = FindWindowEx(
                IntPtr.Zero,
                _desktopHost,
                "WorkerW",
                null);

            return sibling;
        }

        return IntPtr.Zero;
    }

    private void RequestDesktopHierarchy(bool raisedDesktop)
    {
        // Modern raised desktops use 0xD/0x1. Classic layouts use 0/0.
        // The alternate form is tried only if the first request still does not
        // make SHELLDLL_DefView discoverable.
        SendWorkerWRequest(raisedDesktop);

        if (FindExplorerShellView() != IntPtr.Zero)
            return;

        SendWorkerWRequest(!raisedDesktop);
    }

    private void SendWorkerWRequest(bool raisedDesktop)
    {
        var wParam = raisedDesktop
            ? new UIntPtr(0xD)
            : UIntPtr.Zero;

        var lParam = raisedDesktop
            ? new IntPtr(0x1)
            : IntPtr.Zero;

        _ = SendMessageTimeout(
            _progman,
            WorkerWMessage,
            wParam,
            lParam,
            SmtoAbortIfHung,
            1000,
            out _);

        CrashLogService.LogMessage(
            "Explorer desktop compatibility wake-up",
            raisedDesktop
                ? "Sent Progman 0x052C with wParam=0xD, lParam=0x1."
                : "Sent Progman 0x052C with wParam=0, lParam=0.");
    }

    private bool ValidateHost(out string reason)
    {
        reason = string.Empty;

        if (_desktopHost == IntPtr.Zero ||
            _shellView == IntPtr.Zero ||
            _progman == IntPtr.Zero ||
            _explorerProcessId == 0)
        {
            reason = "One or more desktop host handles are not initialized.";
            return false;
        }

        if (!IsWindow(_desktopHost) ||
            !IsWindow(_shellView) ||
            !IsWindow(_progman))
        {
            reason = "One or more Explorer desktop HWNDs are no longer valid.";
            return false;
        }

        if (FindWindow("Progman", null) != _progman)
        {
            reason = "The canonical Progman HWND changed.";
            return false;
        }

        _ = GetWindowThreadProcessId(
            _progman,
            out var currentExplorerProcessId);

        if (currentExplorerProcessId != _explorerProcessId)
        {
            reason =
                $"Explorer PID changed from {_explorerProcessId} to {currentExplorerProcessId}.";
            return false;
        }

        if (!BelongsToExplorer(_shellView) ||
            !BelongsToExplorer(_desktopHost))
        {
            reason = "A desktop HWND is no longer owned by the expected Explorer process.";
            return false;
        }

        if (GetParent(_shellView) != _desktopHost)
        {
            reason = "SHELLDLL_DefView moved to a different parent.";
            return false;
        }

        var currentlyRaised = HasExtendedStyle(
            _progman,
            WsExNoRedirectionBitmap);

        switch (Layout)
        {
            case DesktopShellLayout.RaisedProgman:
                if (_desktopHost != _progman || !currentlyRaised)
                {
                    reason = "The raised Progman desktop signature changed.";
                    return false;
                }
                break;

            case DesktopShellLayout.ClassicWorkerW:
                if (!ClassEquals(_desktopHost, "WorkerW"))
                {
                    reason = "The classic WorkerW desktop host changed class.";
                    return false;
                }
                break;

            case DesktopShellLayout.ProgmanHosted:
                if (_desktopHost != _progman || currentlyRaised)
                {
                    reason = "The Progman-hosted desktop changed layout.";
                    return false;
                }
                break;

            case DesktopShellLayout.CompatibleUnknown:
                // Process ownership + parent relationship are the compatibility
                // contract for an otherwise unknown Explorer layout.
                break;

            default:
                reason = "Desktop layout classification is unknown.";
                return false;
        }

        return true;
    }

    private bool BelongsToExplorer(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || _explorerProcessId == 0)
            return false;

        _ = GetWindowThreadProcessId(
            hwnd,
            out var processId);

        return processId == _explorerProcessId;
    }

    private static bool HasExtendedStyle(
        IntPtr hwnd,
        long style)
    {
        if (hwnd == IntPtr.Zero)
            return false;

        var exStyle = GetWindowLongPtr(
            hwnd,
            GwlExStyle).ToInt64();

        return (exStyle & style) == style;
    }

    private static bool ClassEquals(
        IntPtr hwnd,
        string expected)
    {
        return string.Equals(
            GetClassNameText(hwnd),
            expected,
            StringComparison.Ordinal);
    }

    private static string GetClassNameText(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return string.Empty;

        var className = new StringBuilder(256);
        _ = GetClassName(
            hwnd,
            className,
            className.Capacity);

        return className.ToString();
    }

    private bool HasHostState() =>
        _progman != IntPtr.Zero ||
        _shellView != IntPtr.Zero ||
        _desktopHost != IntPtr.Zero;

    private void LogDiscoverySnapshot(string reason)
    {
        CrashLogService.LogMessage(
            "Desktop layout compatibility snapshot",
            $"{reason}{Environment.NewLine}" +
            $"Explorer PID={_explorerProcessId}; detected layout={Layout}{Environment.NewLine}" +
            $"Progman={DescribeWindow(_progman)}{Environment.NewLine}" +
            $"SHELLDLL_DefView={DescribeWindow(_shellView)}{Environment.NewLine}" +
            $"Desktop host={DescribeWindow(_desktopHost)}{Environment.NewLine}" +
            $"Wallpaper WorkerW={DescribeWindow(_wallpaperWorker)}");
    }

    private void LogAttachFailure(IntPtr hwnd, string reason)
    {
        CrashLogService.LogMessage(
            "Desktop tile attachment failed",
            $"{reason}{Environment.NewLine}" +
            $"Layout={Layout}; generation={Generation}{Environment.NewLine}" +
            $"Tile={DescribeWindow(hwnd)}{Environment.NewLine}" +
            $"Desktop host={DescribeWindow(_desktopHost)}{Environment.NewLine}" +
            $"SHELLDLL_DefView={DescribeWindow(_shellView)}");
    }

    private static string DescribeWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return "NULL";

        var className = GetClassNameText(hwnd);
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

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);
    private delegate bool EnumChildProc(IntPtr hwnd, IntPtr lParam);

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
    private static extern IntPtr FindWindow(
        string? lpClassName,
        string? lpWindowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(
        IntPtr hWndParent,
        IntPtr hWndChildAfter,
        string? lpszClass,
        string? lpszWindow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(
        EnumWindowsProc lpEnumFunc,
        IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumChildWindows(
        IntPtr hWndParent,
        EnumChildProc lpEnumFunc,
        IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr GetParent(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(
        IntPtr hWnd,
        GetWindowCommand uCmd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(
        IntPtr hWnd,
        out RECT lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(
        out POINT lpPoint);

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
    private static extern bool ShowWindow(
        IntPtr hWnd,
        int nCmdShow);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(
        IntPtr hWnd,
        int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(
        IntPtr hWnd,
        int nIndex,
        IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong32(
        IntPtr hWnd,
        int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(
        IntPtr hWnd,
        int nIndex,
        int dwNewLong);

    private static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex) =>
        IntPtr.Size == 8
            ? GetWindowLongPtr64(hWnd, nIndex)
            : new IntPtr(GetWindowLong32(hWnd, nIndex));

    private static IntPtr SetWindowLongPtr(
        IntPtr hWnd,
        int nIndex,
        IntPtr value) =>
        IntPtr.Size == 8
            ? SetWindowLongPtr64(hWnd, nIndex, value)
            : new IntPtr(SetWindowLong32(hWnd, nIndex, value.ToInt32()));

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(
        IntPtr hWnd,
        out uint lpdwProcessId);

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
