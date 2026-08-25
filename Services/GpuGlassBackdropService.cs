using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Windows.System;
using Windows.UI.Composition;
using WinRT;
using Windows.UI.Composition.Desktop;

namespace SmoothFolder.Services;

/// <summary>
/// Hosts a live GPU backdrop behind a transparent WPF glass surface.
///
/// The helper HWND has WS_EX_NOREDIRECTIONBITMAP, so it has no traditional DWM
/// backing surface of its own. Windows.UI.Composition supplies the pixels with a
/// HostBackdropBrush and executes the Gaussian blur on the compositor/GPU.
///
/// WPF remains the sole renderer of tint, highlights, borders, text and icons.
/// The composition visual is inset and rounded to the same inner card geometry,
/// preventing the second-corner / second-shadow artifacts produced by system
/// Acrylic materials.
/// </summary>
public sealed class GpuGlassBackdropService : IDisposable
{
    private const string WindowClassName =
        "SmoothFolder.GpuGlassBackdrop";

    private const double VisualInsetDip = 1.0;

    // Give the blur some room outside the visible card so the kernel can sample
    // neighboring pixels instead of collapsing abruptly at the edge.
    private const double BlurOverscanDip = 20.0;

    // A direct Gaussian blur is more reliable with HostBackdropBrush than
    // attempting to emulate a reduced render target through coordinate
    // transforms. The broader radius suppresses fine wallpaper detail without
    // introducing sampling artifacts.
    private const float BlurAmount = 3.0f;
    private const string BlurPropertyPath =
        "Blur.BlurAmount";

    private const int WsPopup = unchecked((int)0x80000000);

    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoRedirectionBitmap = 0x00200000;
    private const int WsExNoActivate = 0x08000000;

    private const int WmNcHitTest = 0x0084;
    private const int WmMouseActivate = 0x0021;
    private const int HtTransparent = -1;
    private const int MaNoActivate = 3;

    private const int SwHide = 0;
    private const int SwShowNoActivate = 4;

    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoOwnerZOrder = 0x0200;
    private const uint SwpShowWindow = 0x0040;

    private const int DqtypeThreadCurrent = 2;
    private const int DqtatComSta = 2;

    private static readonly object WindowClassSync = new();
    private static readonly NativeWndProc StaticWndProc =
        BackdropWndProc;

    private static bool _windowClassRegistered;
    private static int _enabledLogWritten;
    private static int _fallbackLogWritten;

    private readonly Window _owner;
    private readonly FrameworkElement _surface;
    private readonly double _cornerRadiusDip;

    private object? _dispatcherQueueController;

    private IntPtr _hwnd;
    private Compositor? _compositor;
    private DesktopWindowTarget? _target;
    private SpriteVisual? _visual;
    private CompositionRoundedRectangleGeometry? _clipGeometry;
    private CompositionGeometricClip? _clip;
    private CompositionEffectBrush? _effectBrush;
    private CompositionBackdropBrush? _backdropBrush;

    private string _initializationStage =
        "not started";

    private bool _requestedVisible;
    private bool _trackingAnimation;
    private bool _disposed;

    private GpuGlassBackdropService(
        Window owner,
        FrameworkElement surface,
        double cornerRadiusDip)
    {
        _owner = owner;
        _surface = surface;
        _cornerRadiusDip = cornerRadiusDip;

        try
        {
            Initialize();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"GPU glass initialization failed while {_initializationStage}.",
                ex);
        }

        _owner.LocationChanged += OnOwnerGeometryChanged;
        _owner.SizeChanged += OnOwnerGeometryChanged;
        _owner.Activated += OnOwnerActivated;
        _surface.SizeChanged += OnSurfaceSizeChanged;
        _surface.IsVisibleChanged += OnSurfaceVisibilityChanged;
    }

    public bool IsActive =>
        !_disposed &&
        _hwnd != IntPtr.Zero &&
        _visual is not null;

    public static GpuGlassBackdropService? TryCreate(
        Window owner,
        FrameworkElement surface,
        double cornerRadiusDip)
    {
        if (SystemParameters.HighContrast)
        {
            LogFallbackOnce(
                "High Contrast mode is enabled.");
            return null;
        }

        if (!OperatingSystem.IsWindowsVersionAtLeast(
                10,
                0,
                19041))
        {
            LogFallbackOnce(
                "The current Windows build is below the supported composition target.");
            return null;
        }

        try
        {
            var service =
                new GpuGlassBackdropService(
                    owner,
                    surface,
                    cornerRadiusDip);

            if (!service.IsActive)
            {
                service.Dispose();
                return null;
            }

            if (System.Threading.Interlocked.Exchange(
                    ref _enabledLogWritten,
                    1) == 0)
            {
                CrashLogService.LogMessage(
                    "GPU glass backdrop enabled",
                    $"Windows.UI.Composition raw BackdropBrush + native D2D Gaussian blur is active. " +
                    $"{BlurPropertyPath}={BlurAmount:0.#}. " +
                    "HostBackdropBrush is not used, so no compositor-managed pre-blur masks the custom value. " +
                    "The blur amount is set explicitly on CompositionEffectBrush.Properties. " +
                    "No external graphics runtime, screen capture, or CPU blur loop is used.");
            }

            return service;
        }
        catch (Exception ex)
        {
            LogFallbackOnce(
                ex.ToString());
            return null;
        }
    }

    public void Show()
    {
        if (_disposed)
            return;

        _requestedVisible = true;
        Synchronize();

        if (_hwnd != IntPtr.Zero)
            _ = ShowWindow(
                _hwnd,
                SwShowNoActivate);
    }

    public void Hide()
    {
        if (_disposed)
            return;

        _requestedVisible = false;
        StopAnimationTracking();

        if (_hwnd != IntPtr.Zero)
            _ = ShowWindow(
                _hwnd,
                SwHide);
    }

    public void BeginAnimationTracking()
    {
        if (_disposed)
            return;

        _requestedVisible = true;

        if (!_trackingAnimation)
        {
            _trackingAnimation = true;
            System.Windows.Media.CompositionTarget.Rendering +=
                OnCompositionRendering;
        }

        SynchronizeCore();

        if (_hwnd != IntPtr.Zero)
            _ = ShowWindow(
                _hwnd,
                SwShowNoActivate);
    }

    public void EndAnimationTracking()
    {
        if (_disposed)
            return;

        StopAnimationTracking();
        Synchronize();
    }

    public void Synchronize()
    {
        if (_disposed)
            return;

        _ = _owner.Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Render,
            new Action(SynchronizeCore));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _requestedVisible = false;
        StopAnimationTracking();

        _owner.LocationChanged -= OnOwnerGeometryChanged;
        _owner.SizeChanged -= OnOwnerGeometryChanged;
        _owner.Activated -= OnOwnerActivated;
        _surface.SizeChanged -= OnSurfaceSizeChanged;
        _surface.IsVisibleChanged -= OnSurfaceVisibilityChanged;

        try
        {
            _target!.Root = null;
        }
        catch
        {
            // Best-effort composition teardown.
        }

        _effectBrush?.Dispose();
        _backdropBrush?.Dispose();
        _clip?.Dispose();
        _clipGeometry?.Dispose();
        _visual?.Dispose();
        _target?.Dispose();
        _compositor?.Dispose();

        _effectBrush = null;
        _backdropBrush = null;
        _clip = null;
        _clipGeometry = null;
        _visual = null;
        _target = null;
        _compositor = null;

        if (_hwnd != IntPtr.Zero)
        {
            _ = DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }

        // The dispatcher queue is an RCW created only when the WPF UI thread
        // did not already own one. Releasing our reference lets Windows tear it
        // down after the compositor objects are gone.
        _dispatcherQueueController = null;
    }

    private void Initialize()
    {
        _initializationStage = "registering helper window class";
        EnsureWindowClass();
        _initializationStage = "creating dispatcher queue";
        EnsureDispatcherQueue();

        _initializationStage = "creating helper HWND";
        _hwnd = CreateWindowEx(
            WsExTransparent |
            WsExToolWindow |
            WsExNoActivate |
            WsExNoRedirectionBitmap,
            WindowClassName,
            "SmoothFolder GPU Glass",
            WsPopup,
            -32000,
            -32000,
            1,
            1,
            IntPtr.Zero,
            IntPtr.Zero,
            GetModuleHandle(null),
            IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
        {
            throw new System.ComponentModel.Win32Exception(
                Marshal.GetLastWin32Error(),
                "Could not create the GPU glass helper HWND.");
        }

        _initializationStage = "activating Windows.UI.Composition.Compositor";
        _compositor =
            new Compositor();

        // Windows.UI.Composition.Compositor is a C#/WinRT projected object.
        // Modern .NET does not support directly casting that RCW to a custom
        // ComImport interface. Query the extension interface through C#/WinRT
        // and keep the method signature ABI-only.
        _initializationStage = "querying ICompositorDesktopInterop";
        var interop =
            _compositor.As<ICompositorDesktopInterop>();

        _initializationStage = "creating DesktopWindowTarget";
        interop.CreateDesktopWindowTarget(
            _hwnd,
            0,
            out var rawTarget);

        if (rawTarget == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                "ICompositorDesktopInterop returned a null DesktopWindowTarget.");
        }

        try
        {
            _target =
                MarshalInspectable<DesktopWindowTarget>.FromAbi(
                    rawTarget);
        }
        finally
        {
            // FromAbi creates the managed projection. Release the ABI reference
            // returned by CreateDesktopWindowTarget after the projection owns
            // its corresponding COM reference.
            MarshalInspectable<DesktopWindowTarget>.DisposeAbi(
                rawTarget);
        }

        if (_target is null)
        {
            throw new InvalidOperationException(
                "Could not project the DesktopWindowTarget returned by Composition.");
        }

        _initializationStage = "creating composition visual";
        _visual =
            _compositor.CreateSpriteVisual();

        // RectangleClip is not available in every Windows SDK projection that
        // SmoothFolder targets. RoundedRectangleGeometry + GeometricClip has
        // been available since Windows 10 1809 and gives us the same rounded
        // visual clipping without raising the minimum OS contract.
        _clipGeometry =
            _compositor.CreateRoundedRectangleGeometry();

        _clip =
            _compositor.CreateGeometricClip(
                _clipGeometry);

        _visual.Clip =
            _clip;

        var source =
            new CompositionEffectSourceParameter(
                "Backdrop");

        var blur =
            new GaussianBlurEffectDescriptor
            {
                Name = "Blur",
                BlurAmount = BlurAmount,
                Source = source
            };

        _initializationStage = "creating Gaussian blur effect factory";
        var effectFactory =
            _compositor.CreateEffectFactory(
                blur,
                new[]
                {
                    BlurPropertyPath
                });

        _effectBrush =
            effectFactory.CreateBrush();

        var descriptorDiagnostics =
            blur.TakeDiagnostics();

        var beforeStatus =
            _effectBrush.Properties.TryGetScalar(
                BlurPropertyPath,
                out var beforeValue);

        // Do not rely only on the descriptor value captured while the effect
        // factory is compiled. Expose the blur standard deviation as a
        // Composition property and set it explicitly on this brush instance.
        // This makes runtime/material tuning deterministic.
        _effectBrush.Properties.InsertScalar(
            BlurPropertyPath,
            BlurAmount);

        var afterStatus =
            _effectBrush.Properties.TryGetScalar(
                BlurPropertyPath,
                out var afterValue);

        CrashLogService.LogMessage(
            "GPU blur property diagnostics",
            $"Requested={BlurAmount:0.###}; " +
            $"descriptor.BlurAmount={blur.BlurAmount:0.###}; " +
            $"brush-before: status={beforeStatus}, value={beforeValue:0.###}; " +
            $"brush-after: status={afterStatus}, value={afterValue:0.###}. " +
            $"Descriptor callbacks: {descriptorDiagnostics}");

        _initializationStage = "creating raw BackdropBrush";

        // HostBackdropBrush carries a compositor-managed blur of its own.
        // That pre-blur masks most differences between Gaussian standard
        // deviations such as 0, 3, 8 and 16. BackdropBrush supplies the raw
        // pixels behind this transparent composition visual instead, so the
        // Direct2D Gaussian blur below is the only controllable blur stage.
        _backdropBrush =
            _compositor.CreateBackdropBrush();

        _effectBrush.SetSourceParameter(
            "Backdrop",
            _backdropBrush);

        _visual.Brush =
            _effectBrush;

        _initializationStage = "attaching composition visual tree";
        _target.Root =
            _visual;

        _initializationStage = "complete";
    }

    private void EnsureDispatcherQueue()
    {
        if (DispatcherQueue.GetForCurrentThread() is not null)
            return;

        var options =
            new DispatcherQueueOptions
            {
                dwSize =
                    Marshal.SizeOf<DispatcherQueueOptions>(),
                threadType =
                    DqtypeThreadCurrent,
                apartmentType =
                    DqtatComSta
            };

        var result =
            CreateDispatcherQueueController(
                options,
                out var controller);

        if (result != 0)
            Marshal.ThrowExceptionForHR(result);

        _dispatcherQueueController =
            controller;
    }

    private void SynchronizeCore()
    {
        if (_disposed ||
            _hwnd == IntPtr.Zero ||
            _visual is null ||
            _clip is null ||
            _clipGeometry is null ||
            !_owner.IsVisible ||
            !_surface.IsVisible ||
            _surface.ActualWidth <= 0 ||
            _surface.ActualHeight <= 0)
        {
            if (_hwnd != IntPtr.Zero)
                _ = ShowWindow(
                    _hwnd,
                    SwHide);

            return;
        }

        try
        {
            // PointToScreen includes the WPF RenderTransform. During open/close
            // animations this therefore yields the live transformed card
            // rectangle, while during DragMove it follows the real HWND position.
            var maxInsetX =
                Math.Max(
                    0,
                    (_surface.ActualWidth / 2.0) - 0.5);

            var maxInsetY =
                Math.Max(
                    0,
                    (_surface.ActualHeight / 2.0) - 0.5);

            var insetX =
                Math.Min(
                    VisualInsetDip,
                    maxInsetX);

            var insetY =
                Math.Min(
                    VisualInsetDip,
                    maxInsetY);

            var topLeft =
                _surface.PointToScreen(
                    new Point(
                        insetX,
                        insetY));

            var bottomRight =
                _surface.PointToScreen(
                    new Point(
                        _surface.ActualWidth - insetX,
                        _surface.ActualHeight - insetY));

            var visibleLeft =
                Math.Min(
                    topLeft.X,
                    bottomRight.X);

            var visibleTop =
                Math.Min(
                    topLeft.Y,
                    bottomRight.Y);

            var visibleWidth =
                Math.Max(
                    1.0,
                    Math.Abs(
                        bottomRight.X -
                        topLeft.X));

            var visibleHeight =
                Math.Max(
                    1.0,
                    Math.Abs(
                        bottomRight.Y -
                        topLeft.Y));

            var helperScale =
                PresentationSource.FromVisual(
                    _surface)?
                    .CompositionTarget?
                    .TransformToDevice.M11
                ?? 1.0;

            var overscanPixels =
                Math.Max(
                    1,
                    (int)Math.Ceiling(
                        BlurOverscanDip *
                        helperScale));

            var left =
                (int)Math.Round(
                    visibleLeft) -
                overscanPixels;

            var top =
                (int)Math.Round(
                    visibleTop) -
                overscanPixels;

            var visibleWidthPixels =
                Math.Max(
                    1,
                    (int)Math.Round(
                        visibleWidth));

            var visibleHeightPixels =
                Math.Max(
                    1,
                    (int)Math.Round(
                        visibleHeight));

            var width =
                visibleWidthPixels +
                (overscanPixels * 2);

            var height =
                visibleHeightPixels +
                (overscanPixels * 2);

            var ownerHwnd =
                new WindowInteropHelper(
                    _owner).Handle;

            if (ownerHwnd == IntPtr.Zero)
                return;

            // Keep the helper directly below the WPF popup. It has no native
            // owner relation and cannot activate or receive pointer input.
            _ = SetWindowPos(
                _hwnd,
                ownerHwnd,
                left,
                top,
                width,
                height,
                SwpNoActivate |
                SwpNoOwnerZOrder |
                (_requestedVisible
                    ? SwpShowWindow
                    : 0));

            _visual.Size =
                new Vector2(
                    width,
                    height);

            var logicalWidth =
                Math.Max(
                    1.0,
                    _surface.ActualWidth -
                    (2 * insetX));

            var logicalHeight =
                Math.Max(
                    1.0,
                    _surface.ActualHeight -
                    (2 * insetY));

            var scaleX =
                visibleWidthPixels / logicalWidth;

            var scaleY =
                visibleHeightPixels / logicalHeight;

            var radiusPixels =
                (float)Math.Max(
                    1.0,
                    Math.Max(
                        0,
                        _cornerRadiusDip -
                        Math.Max(
                            insetX,
                            insetY)) *
                    Math.Min(
                        scaleX,
                        scaleY));

            var radius =
                new Vector2(
                    radiusPixels,
                    radiusPixels);

            _clipGeometry.Offset =
                new Vector2(
                    overscanPixels,
                    overscanPixels);

            _clipGeometry.Size =
                new Vector2(
                    visibleWidthPixels,
                    visibleHeightPixels);

            _clipGeometry.CornerRadius =
                radius;

            if (_requestedVisible)
                _ = ShowWindow(
                    _hwnd,
                    SwShowNoActivate);
        }
        catch (InvalidOperationException)
        {
            // The WPF visual may be temporarily disconnected during close/DPI
            // transitions. The next geometry event will synchronize it again.
        }
    }

    private void OnCompositionRendering(
        object? sender,
        EventArgs e)
    {
        if (!_trackingAnimation ||
            _disposed)
        {
            return;
        }

        SynchronizeCore();
    }

    private void StopAnimationTracking()
    {
        if (!_trackingAnimation)
            return;

        _trackingAnimation = false;

        System.Windows.Media.CompositionTarget.Rendering -=
            OnCompositionRendering;
    }

    private void OnOwnerGeometryChanged(
        object? sender,
        EventArgs e) =>
        Synchronize();

    private void OnSurfaceSizeChanged(
        object sender,
        SizeChangedEventArgs e) =>
        Synchronize();

    private void OnSurfaceVisibilityChanged(
        object sender,
        DependencyPropertyChangedEventArgs e) =>
        Synchronize();

    private void OnOwnerActivated(
        object? sender,
        EventArgs e) =>
        Synchronize();

    private static void EnsureWindowClass()
    {
        if (_windowClassRegistered)
            return;

        lock (WindowClassSync)
        {
            if (_windowClassRegistered)
                return;

            var windowClass =
                new WNDCLASSEX
                {
                    cbSize =
                        (uint)Marshal.SizeOf<WNDCLASSEX>(),
                    lpfnWndProc =
                        StaticWndProc,
                    hInstance =
                        GetModuleHandle(null),
                    lpszClassName =
                        WindowClassName
                };

            var atom =
                RegisterClassEx(
                    ref windowClass);

            if (atom == 0)
            {
                var error =
                    Marshal.GetLastWin32Error();

                // ERROR_CLASS_ALREADY_EXISTS
                if (error != 1410)
                {
                    throw new System.ComponentModel.Win32Exception(
                        error,
                        "Could not register the GPU glass helper window class.");
                }
            }

            _windowClassRegistered = true;
        }
    }

    private static void LogFallbackOnce(string reason)
    {
        if (System.Threading.Interlocked.Exchange(
                ref _fallbackLogWritten,
                1) != 0)
        {
            return;
        }

        CrashLogService.LogMessage(
            "GPU glass backdrop unavailable",
            reason + Environment.NewLine +
            "SmoothFolder will use the existing translucent WPF renderer.");
    }

    private static IntPtr BackdropWndProc(
        IntPtr hwnd,
        uint msg,
        IntPtr wParam,
        IntPtr lParam)
    {
        if (msg == WmNcHitTest)
            return new IntPtr(
                HtTransparent);

        if (msg == WmMouseActivate)
            return new IntPtr(
                MaNoActivate);

        return DefWindowProc(
            hwnd,
            msg,
            wParam,
            lParam);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DispatcherQueueOptions
    {
        public int dwSize;
        public int threadType;
        public int apartmentType;
    }

    private delegate IntPtr NativeWndProc(
        IntPtr hwnd,
        uint msg,
        IntPtr wParam,
        IntPtr lParam);

    [StructLayout(
        LayoutKind.Sequential,
        CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public NativeWndProc lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpszMenuName;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string lpszClassName;

        public IntPtr hIconSm;
    }

    [ComImport]
    [Guid("29E691FA-4567-4DCA-B319-D0F207EB6807")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICompositorDesktopInterop
    {
        // Keep this custom interop interface ABI-only. BOOL is a 32-bit int and
        // the returned WinRT interface is an IInspectable pointer. C#/WinRT
        // performs the QueryInterface via Compositor.As<T>() and we manually
        // project the returned pointer with MarshalInspectable<T>.FromAbi.
        void CreateDesktopWindowTarget(
            IntPtr hwndTarget,
            int isTopmost,
            out IntPtr target);
    }

    [DllImport(
        "CoreMessaging.dll",
        ExactSpelling = true,
        CallingConvention = CallingConvention.StdCall)]
    private static extern int CreateDispatcherQueueController(
        DispatcherQueueOptions options,
        [MarshalAs(UnmanagedType.IUnknown)]
        out object dispatcherQueueController);

    [DllImport(
        "kernel32.dll",
        CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(
        string? lpModuleName);

    [DllImport(
        "user32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    private static extern ushort RegisterClassEx(
        ref WNDCLASSEX lpwcx);

    [DllImport(
        "user32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        int dwExStyle,
        string lpClassName,
        string lpWindowName,
        int dwStyle,
        int x,
        int y,
        int width,
        int height,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(
        IntPtr hWnd,
        uint msg,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(
        IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(
        IntPtr hWnd,
        int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint flags);
}
