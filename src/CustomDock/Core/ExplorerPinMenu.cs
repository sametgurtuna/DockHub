using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace CustomDock.Core;

/// <summary>
/// Explorer'da .exe ve kısayollara sağ tıklayınca çıkan "Custom Dock'a sabitle" komutu (HKCU, yönetici izni gerekmez).
/// Komut <c>CustomDock.exe --pin "dosya"</c> çalıştırır; açık olan örnek öğeyi sabitlenmiş uygulamaların sonuna ekler.
/// </summary>
/// <remarks>
/// Windows 11'in yeni kısa menüsü yalnızca paketlenmiş (imzalı) uygulamaların komutlarını gösterir;
/// bu komut orada "Daha fazla seçenek göster" altında (ya da Shift+sağ tık ile) görünür.
/// </remarks>
public static class ExplorerPinMenu
{
    public const string PinArgument = "--pin";
    private const string VerbName = "CustomDock.Pin";
    private static readonly string[] FileClasses = { "exefile", "lnkfile" };

    private static string Command => $"\"{Environment.ProcessPath}\" {PinArgument} \"%1\"";

    public static void Set(bool enabled)
    {
        try
        {
            bool changed = false;
            foreach (var fileClass in FileClasses)
            {
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
            Log.Error(ex, "Explorer menü kaydı güncellenemedi");
        }
    }

    // ------------------------------------------------------------------ Sabitleme istekleri (örnekler arası)

    private static string QueueFile => Path.Combine(AppPaths.Root, "pin-requests.txt");

    /// <summary>İkinci örnek: isteği kuyruğa yazar; çalışan örnek sinyal ile okur.</summary>
    public static void Enqueue(string path)
    {
        try
        {
            File.AppendAllLines(QueueFile, new[] { Path.GetFullPath(path) });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Sabitleme isteği yazılamadı");
        }
    }

    /// <summary>Kuyruktaki istekleri alır ve kuyruğu boşaltır.</summary>
    public static List<string> Dequeue()
    {
        try
        {
            if (!File.Exists(QueueFile)) return new List<string>();
            var lines = File.ReadAllLines(QueueFile).Where(l => !string.IsNullOrWhiteSpace(l)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            File.Delete(QueueFile);
            return lines;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Sabitleme istekleri okunamadı");
            return new List<string>();
        }
    }

    private const int SHCNE_ASSOCCHANGED = 0x08000000;

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
}
