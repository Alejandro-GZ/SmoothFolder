namespace SmoothFolder.Models;

public sealed class FolderConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Folder";
    public double X { get; set; } = 120;
    public double Y { get; set; } = 120;
    public List<AppItem> Items { get; set; } = [];
}
