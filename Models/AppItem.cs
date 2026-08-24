namespace SmoothFolder.Models;

public sealed class AppItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string DisplayName { get; set; } = "";
    public string Path { get; set; } = "";
}
