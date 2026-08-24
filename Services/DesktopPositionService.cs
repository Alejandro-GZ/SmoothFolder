using SmoothFolder.Models;

namespace SmoothFolder.Services;

/// <summary>
/// Persists compact folder positions relative to a physical monitor.
///
/// Older SmoothFolder versions stored only absolute WPF-DIP coordinates. Those
/// values remain supported and are migrated the next time a tile is positioned.
/// </summary>
public static class DesktopPositionService
{
    private const double TileWidthDip = 112;
    private const double TileHeightDip = 132;

    public static ScreenPixelPoint Resolve(FolderConfig folder)
    {
        var monitor = MonitorService.FindByDeviceName(
            folder.MonitorDeviceName);

        if (folder.MonitorOffsetX is not null &&
            folder.MonitorOffsetY is not null)
        {
            if (monitor is not null)
            {
                return ClampToWorkArea(
                    monitor,
                    new ScreenPixelPoint(
                        monitor.Bounds.Left + folder.MonitorOffsetX.Value,
                        monitor.Bounds.Top + folder.MonitorOffsetY.Value));
            }

            // The persisted monitor was disconnected. Preserve the folder's
            // monitor-relative offset on the current primary display and clamp
            // it to the available work area. Capture() will persist the new
            // monitor identity after the HWND is moved successfully.
            var primary = MonitorService.GetPrimary();

            return ClampToWorkArea(
                primary,
                new ScreenPixelPoint(
                    primary.Bounds.Left + folder.MonitorOffsetX.Value,
                    primary.Bounds.Top + folder.MonitorOffsetY.Value));
        }

        return ResolveLegacy(folder);
    }

    public static bool Capture(
        FolderConfig folder,
        ScreenPixelRect bounds)
    {
        var monitor = MonitorService.GetForRect(bounds);

        var offsetX = bounds.Left - monitor.Bounds.Left;
        var offsetY = bounds.Top - monitor.Bounds.Top;

        // Keep X/Y populated as a backwards-compatible approximation. New
        // builds use MonitorDeviceName + physical offsets as the source of truth.
        var legacyX = MonitorService.PixelsToDip(
            bounds.Left,
            monitor.DpiX);

        var legacyY = MonitorService.PixelsToDip(
            bounds.Top,
            monitor.DpiY);

        var changed =
            !string.Equals(
                folder.MonitorDeviceName,
                monitor.DeviceName,
                StringComparison.OrdinalIgnoreCase) ||
            folder.MonitorOffsetX != offsetX ||
            folder.MonitorOffsetY != offsetY ||
            Math.Abs(folder.X - legacyX) > 0.01 ||
            Math.Abs(folder.Y - legacyY) > 0.01;

        folder.MonitorDeviceName = monitor.DeviceName;
        folder.MonitorOffsetX = offsetX;
        folder.MonitorOffsetY = offsetY;
        folder.X = legacyX;
        folder.Y = legacyY;

        return changed;
    }

    public static void PlaceAtPixels(
        FolderConfig folder,
        ScreenPixelPoint topLeft)
    {
        var monitor = MonitorService.GetForPoint(topLeft);

        var width = MonitorService.DipToPixels(
            TileWidthDip,
            monitor.DpiX);

        var height = MonitorService.DipToPixels(
            TileHeightDip,
            monitor.DpiY);

        var clamped = Clamp(
            topLeft,
            monitor.WorkArea,
            width,
            height);

        folder.MonitorDeviceName = monitor.DeviceName;
        folder.MonitorOffsetX = clamped.X - monitor.Bounds.Left;
        folder.MonitorOffsetY = clamped.Y - monitor.Bounds.Top;
        folder.X = MonitorService.PixelsToDip(
            clamped.X,
            monitor.DpiX);
        folder.Y = MonitorService.PixelsToDip(
            clamped.Y,
            monitor.DpiY);
    }

    public static ScreenPixelPoint OffsetByDip(
        ScreenPixelPoint origin,
        double xDip,
        double yDip)
    {
        var monitor = MonitorService.GetForPoint(origin);

        return new ScreenPixelPoint(
            origin.X + MonitorService.DipToPixels(xDip, monitor.DpiX),
            origin.Y + MonitorService.DipToPixels(yDip, monitor.DpiY));
    }

    private static ScreenPixelPoint ResolveLegacy(FolderConfig folder)
    {
        var monitors = MonitorService.GetMonitors();

        // Legacy builds converted the absolute screen coordinate with the
        // window's current monitor DPI. Reconstruct one candidate per monitor
        // and prefer a candidate that actually lies inside that monitor.
        foreach (var monitor in monitors)
        {
            var candidate = new ScreenPixelPoint(
                MonitorService.DipToPixels(folder.X, monitor.DpiX),
                MonitorService.DipToPixels(folder.Y, monitor.DpiY));

            if (monitor.Bounds.Contains(candidate))
                return ClampToWorkArea(monitor, candidate);
        }

        var primary = MonitorService.GetPrimary();

        return ClampToWorkArea(
            primary,
            new ScreenPixelPoint(
                MonitorService.DipToPixels(folder.X, primary.DpiX),
                MonitorService.DipToPixels(folder.Y, primary.DpiY)));
    }

    private static ScreenPixelPoint ClampToWorkArea(
        MonitorSnapshot monitor,
        ScreenPixelPoint point)
    {
        var width = MonitorService.DipToPixels(
            TileWidthDip,
            monitor.DpiX);

        var height = MonitorService.DipToPixels(
            TileHeightDip,
            monitor.DpiY);

        return Clamp(
            point,
            monitor.WorkArea,
            width,
            height);
    }

    private static ScreenPixelPoint Clamp(
        ScreenPixelPoint point,
        ScreenPixelRect workArea,
        int width,
        int height)
    {
        var maxX = Math.Max(
            workArea.Left,
            workArea.Right - width);

        var maxY = Math.Max(
            workArea.Top,
            workArea.Bottom - height);

        return new ScreenPixelPoint(
            Math.Clamp(point.X, workArea.Left, maxX),
            Math.Clamp(point.Y, workArea.Top, maxY));
    }
}
