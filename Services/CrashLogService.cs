using System.IO;
using System.Text;

namespace SmoothFolder.Services;

public static class CrashLogService
{
    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SmoothFolder",
        "Logs");

    public static string LogPath => Path.Combine(LogDirectory, "smoothfolder.log");

    public static void Log(Exception exception, string context)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);

            var entry = new StringBuilder()
                .AppendLine(new string('-', 72))
                .AppendLine(DateTimeOffset.Now.ToString("O"))
                .AppendLine(context)
                .AppendLine(exception.ToString())
                .ToString();

            File.AppendAllText(LogPath, entry);
        }
        catch
        {
            // Logging must never turn a recoverable UI error into another crash.
        }
    }
}
