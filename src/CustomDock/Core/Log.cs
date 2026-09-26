using System.Diagnostics;

namespace CustomDock.Core;

/// <summary>Simple, size-limited file logger.</summary>
public static class Log
{
    private const long MaxSize = 512 * 1024;
    private static readonly object Gate = new();

    /// <summary>Verbose diagnostics: "debugLogging": true in config.json or the DOCKHUB_DEBUG=1 environment variable.</summary>
    public static bool DebugEnabled { get; set; } = Environment.GetEnvironmentVariable("DOCKHUB_DEBUG") == "1";

    public static void Debug(string message)
    {
        if (DebugEnabled) Write("DEBUG", message);
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(Exception ex, string context) => Write("ERROR", $"{context}: {ex}");

    public static void Write(string level, string message)
    {
        System.Diagnostics.Debug.WriteLine($"[{level}] {message}");
        try
        {
            lock (Gate)
            {
                var file = new FileInfo(AppPaths.LogFile);
                if (file.Exists && file.Length > MaxSize)
                    File.Move(file.FullName, file.FullName + ".old", overwrite: true);
                File.AppendAllText(AppPaths.LogFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Do not crash application if logging fails.
        }
    }
}
