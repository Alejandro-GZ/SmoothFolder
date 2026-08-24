using System.IO;
using SmoothFolder.Models;

namespace SmoothFolder.Services;

public sealed class ShortcutImportService
{
    private readonly ConfigService _config;

    public ShortcutImportService(ConfigService config)
    {
        _config = config;
    }

    public AppItem Import(string sourcePath, FolderConfig folder)
    {
        if (!File.Exists(sourcePath) && !Directory.Exists(sourcePath))
            throw new FileNotFoundException("The dropped item no longer exists.", sourcePath);

        var ext = Path.GetExtension(sourcePath).ToLowerInvariant();
        var storedPath = sourcePath;

        // .lnk/.url shortcuts are copied to AppData so the desktop can be
        // cleaned later without breaking the SmoothFolder entry.
        if (File.Exists(sourcePath) && (ext == ".lnk" || ext == ".url"))
        {
            var targetDir = _config.GetFolderItemsDirectory(folder.Id);
            var fileName = Path.GetFileName(sourcePath);
            var target = MakeUnique(Path.Combine(targetDir, fileName));
            File.Copy(sourcePath, target);
            storedPath = target;
        }

        return new AppItem
        {
            DisplayName = Path.GetFileNameWithoutExtension(sourcePath),
            Path = storedPath
        };
    }

    private static string MakeUnique(string path)
    {
        if (!File.Exists(path))
            return path;

        var dir = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);

        for (var i = 2; i < 10000; i++)
        {
            var candidate = Path.Combine(dir, $"{name} ({i}){ext}");
            if (!File.Exists(candidate))
                return candidate;
        }

        return Path.Combine(dir, $"{name}-{Guid.NewGuid():N}{ext}");
    }
}
