using System.Diagnostics;

namespace CustomDock.Core;

/// <summary>Basit, boyutu sınırlı dosya logu.</summary>
public static class Log
{
    private const long MaxSize = 512 * 1024;
    private static readonly object Gate = new();

    public static void Info(string message) => Write("INFO", message);

    public static void Error(Exception ex, string context) => Write("ERROR", $"{context}: {ex}");

    public static void Write(string level, string message)
    {
        Debug.WriteLine($"[{level}] {message}");
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
            // Log yazılamazsa uygulamayı düşürmeyelim.
        }
    }
}
