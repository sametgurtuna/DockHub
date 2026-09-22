using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace CustomDock.Core;

/// <summary>
/// Context menu command "Pin to DockHub" when right-clicking .exe and shortcut files in Explorer (HKCU, no admin privileges required).
/// The command runs <c>DockHub.exe --pin "file"</c>; the running instance adds the item to the end of pinned applications.
/// </summary>
/// <remarks>
/// Windows 11's modern context menu only shows packaged (signed) app commands directly;
/// this command appears under "Show more options" (or via Shift+Right Click).
/// </remarks>
public static class ExplorerPinMenu
{
    public const string PinArgument = "--pin";
    private const string VerbName = "DockHub.Pin";
    private const string LegacyVerbName = "CustomDock.Pin";
    private static readonly string[] FileClasses = { "exefile", "lnkfile" };

    private static string Command => $"\"{Environment.ProcessPath}\" {PinArgument} \"%1\"";

    public static void Set(bool enabled)
    {
        try
        {
            bool changed = false;
            foreach (var fileClass in FileClasses)
            {
                string legacyPath = $@"Software\Classes\{fileClass}\shell\{LegacyVerbName}";
                if (Registry.CurrentUser.OpenSubKey(legacyPath) is { } legacy)
                {
                    legacy.Dispose();
                    Registry.CurrentUser.DeleteSubKeyTree(legacyPath, throwOnMissingSubKey: false);
                    changed = true;
                }

                string keyPath = $@"Software\Classes\{fileClass}\shell\{VerbName}";
                if (enabled)
                {
                    using (var existingKey = Registry.CurrentUser.OpenSubKey(keyPath))
                    using (var existingCommand = Registry.CurrentUser.OpenSubKey(keyPath + @"\command"))
                    {
                        if (existingCommand?.GetValue(null) as string == Command &&
                            existingKey?.GetValue("MUIVerb") as string == AppInfo.PinLabel)
                            continue;
                    }

                    using var key = Registry.CurrentUser.CreateSubKey(keyPath);
                    key.SetValue("MUIVerb", AppInfo.PinLabel);
                    key.SetValue("Icon", $"\"{Environment.ProcessPath}\",0");
                    using var command = key.CreateSubKey("command");
                    command.SetValue(null, Command);
                    changed = true;
                }
                else if (Registry.CurrentUser.OpenSubKey(keyPath) is { } key)
                {
                    key.Dispose();
                    Registry.CurrentUser.DeleteSubKeyTree(keyPath, throwOnMissingSubKey: false);
                    changed = true;
                }
            }

            if (changed)
                SHChangeNotify(SHCNE_ASSOCCHANGED, 0, IntPtr.Zero, IntPtr.Zero);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to update Explorer menu registry entry");
        }
    }

    // ------------------------------------------------------------------ Pin requests (inter-instance)

    private const string MutexName = @"Local\DockHub.PinQueue.Mutex";
    private static string QueueFile => Path.Combine(AppPaths.Root, "pin-requests.txt");

    /// <summary>Second instance: writes request to queue; running instance reads via signal.</summary>
    public static void Enqueue(string path)
    {
        try
        {
            using var mutex = new Mutex(false, MutexName);
            bool acquired = false;
            try
            {
                acquired = mutex.WaitOne(TimeSpan.FromSeconds(3));
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }

            if (acquired)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(QueueFile)!);
                    File.AppendAllLines(QueueFile, new[] { Path.GetFullPath(path) });
                }
                finally
                {
                    mutex.ReleaseMutex();
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to write pin request");
        }
    }

    /// <summary>Retrieves queued requests and clears the queue.</summary>
    public static List<string> Dequeue()
    {
        try
        {
            using var mutex = new Mutex(false, MutexName);
            bool acquired = false;
            try
            {
                acquired = mutex.WaitOne(TimeSpan.FromSeconds(3));
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }

            if (acquired)
            {
                try
                {
                    if (!File.Exists(QueueFile)) return new List<string>();
                    var lines = File.ReadAllLines(QueueFile)
                        .Where(l => !string.IsNullOrWhiteSpace(l))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    File.Delete(QueueFile);
                    return lines;
                }
                finally
                {
                    mutex.ReleaseMutex();
                }
            }
            return new List<string>();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to read pin requests");
            return new List<string>();
        }
    }

    private const int SHCNE_ASSOCCHANGED = 0x08000000;

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
}
