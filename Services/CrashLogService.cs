using System.IO;
using System.Text;

namespace SmoothFolder.Services;

public static class CrashLogService
{
    private const long MaxLogFileBytes = 512 * 1024;
    private const int RetainedHistoryFiles = 2;
    private const int MaxEntryCharacters = 64 * 1024;

    private static readonly object Sync = new();

    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SmoothFolder",
        "Logs");

    public static string LogPath =>
        Path.Combine(LogDirectory, "smoothfolder.log");

    public static void Log(Exception exception, string context)
    {
        WriteEntry(
            context,
            exception.ToString());
    }

    public static void LogMessage(string context, string message)
    {
        WriteEntry(context, message);
    }

    private static void WriteEntry(string context, string body)
    {
        try
        {
            var entry = BuildEntry(context, body);

            lock (Sync)
            {
                Directory.CreateDirectory(LogDirectory);
                RotateIfNeeded(entry);
                File.AppendAllText(
                    LogPath,
                    entry,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
        }
        catch
        {
            // Logging must never turn a recoverable UI error into another crash.
        }
    }

    private static string BuildEntry(string context, string body)
    {
        context = LimitText(context);
        body = LimitText(body);

        return new StringBuilder(
                context.Length + body.Length + 128)
            .AppendLine(new string('-', 72))
            .AppendLine(DateTimeOffset.Now.ToString("O"))
            .AppendLine(context)
            .AppendLine(body)
            .ToString();
    }

    private static string LimitText(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        if (value.Length <= MaxEntryCharacters)
            return value;

        return value[..MaxEntryCharacters] +
               Environment.NewLine +
               "[log entry truncated]";
    }

    private static void RotateIfNeeded(string pendingEntry)
    {
        if (!File.Exists(LogPath))
            return;

        var currentLength = new FileInfo(LogPath).Length;
        var pendingLength = Encoding.UTF8.GetByteCount(pendingEntry);

        if (currentLength + pendingLength <= MaxLogFileBytes)
            return;

        // Delete the oldest retained log first.
        var oldest = GetHistoryPath(RetainedHistoryFiles);
        if (File.Exists(oldest))
            File.Delete(oldest);

        // Shift history from newest to oldest:
        // smoothfolder.1.log -> smoothfolder.2.log
        for (var index = RetainedHistoryFiles - 1; index >= 1; index--)
        {
            var source = GetHistoryPath(index);
            if (!File.Exists(source))
                continue;

            File.Move(
                source,
                GetHistoryPath(index + 1));
        }

        // Active log becomes the newest historical file.
        File.Move(
            LogPath,
            GetHistoryPath(1));
    }

    private static string GetHistoryPath(int index) =>
        Path.Combine(
            LogDirectory,
            $"smoothfolder.{index}.log");
}
