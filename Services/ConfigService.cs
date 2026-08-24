using System.IO;
using System.Text.Json;
using SmoothFolder.Models;

namespace SmoothFolder.Services;

public sealed class ConfigService
{
    private readonly string _root =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SmoothFolder");

    private string ConfigPath => Path.Combine(_root, "config.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public string DataRoot => _root;

    public AppConfig Load()
    {
        Directory.CreateDirectory(_root);

        if (!File.Exists(ConfigPath))
        {
            var initial = new AppConfig
            {
                Folders =
                [
                    new FolderConfig
                    {
                        Name = "Games",
                        X = 120,
                        Y = 140
                    }
                ]
            };
            Save(initial);
            return initial;
        }

        try
        {
            return JsonSerializer.Deserialize<AppConfig>(
                File.ReadAllText(ConfigPath), JsonOptions) ?? new AppConfig();
        }
        catch
        {
            var backup = ConfigPath + ".broken-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            File.Copy(ConfigPath, backup, overwrite: true);
            return new AppConfig();
        }
    }

    public void Save(AppConfig config)
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(config, JsonOptions));
    }

    public string GetFolderItemsDirectory(string folderId)
    {
        var path = Path.Combine(_root, "Items", folderId);
        Directory.CreateDirectory(path);
        return path;
    }

    public void DeleteFolderStorage(string folderId)
    {
        var path = Path.Combine(_root, "Items", folderId);
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }
}
