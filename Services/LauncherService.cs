using System.IO;
using System.Diagnostics;

namespace SmoothFolder.Services;

public sealed class LauncherService
{
    public void Launch(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
            throw new FileNotFoundException("The shortcut or executable could not be found.", path);

        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }
}
