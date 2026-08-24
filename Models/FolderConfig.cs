using SmoothFolder.Services;

namespace SmoothFolder.Models;

public sealed class FolderConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Folder";
    // Legacy absolute WPF-DIP coordinates. Kept for backwards compatibility.
    public double X { get; set; } = 120;
    public double Y { get; set; } = 120;

    // v0.2+ multi-monitor position. Physical pixel offsets are relative to the
    // monitor bounds so negative virtual-desktop coordinates and mixed DPI
    // layouts remain stable across launches.
    public string? MonitorDeviceName { get; set; }
    public int? MonitorOffsetX { get; set; }
    public int? MonitorOffsetY { get; set; }
    public string GlassTint { get; set; } = GlassAppearanceService.DefaultTint;
    public double GlassOpacity { get; set; } = GlassAppearanceService.DefaultOpacity;
    public List<AppItem> Items { get; set; } = [];
}
