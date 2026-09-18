using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using CustomDock.Core;
using CustomDock.Native;

namespace CustomDock.Services;

public sealed record RecycleBinInfo(long ItemCount, long TotalBytes)
{
    public bool IsEmpty => ItemCount == 0;

    public string FormattedSize
    {
        get
        {
            if (TotalBytes <= 0) return "0 B";
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double bytes = TotalBytes;
            int unitIndex = 0;
            while (bytes >= 1024 && unitIndex < units.Length - 1)
            {
                bytes /= 1024;
                unitIndex++;
            }
            return $"{bytes:0.#} {units[unitIndex]}";
        }
    }
}

public sealed class RecycleBinService
{
    private RecycleBinInfo _current = new(0, 0);

    public RecycleBinService()
    {
        Refresh();
        AppServices.Clock.MinuteTick += (_, _) => Refresh();
    }

    public RecycleBinInfo Current => _current;

    public event EventHandler? Updated;

    public void Refresh()
    {
        try
        {
            var info = new NativeMethods.SHQUERYRBINFO { cbSize = Marshal.SizeOf<NativeMethods.SHQUERYRBINFO>() };
            int hr = NativeMethods.SHQueryRecycleBin(null, ref info);
            if (hr == 0)
            {
                var next = new RecycleBinInfo(info.i64NumItems, info.i64Size);
                if (next != _current)
                {
                    _current = next;
                    Application.Current?.Dispatcher.BeginInvoke(() => Updated?.Invoke(this, EventArgs.Empty));
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Çöp Kutusu durumu sorgulanamadı");
        }
    }

    public void Empty()
    {
        try
        {
            // Kullanıcı onay iletişim kutusunu Windows açar (0: standart onay ve ses).
            NativeMethods.SHEmptyRecycleBin(IntPtr.Zero, null, 0);
            Refresh();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Çöp Kutusu boşaltılamadı");
        }
    }

    public void Open()
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", "shell:RecycleBinFolder") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Çöp Kutusu açılamadı");
        }
    }

    public void SendToRecycleBin(IEnumerable<string> paths)
    {
        try
        {
            var validPaths = paths.Where(p => !string.IsNullOrWhiteSpace(p) && (File.Exists(p) || Directory.Exists(p))).ToList();
            if (validPaths.Count == 0) return;

            // SHFileOperation için pFrom çift null ile sonlanan null ayrımlı string olmalı
            string pFrom = string.Join("\0", validPaths) + "\0\0";

            var op = new NativeMethods.SHFILEOPSTRUCT
            {
                hwnd = IntPtr.Zero,
                wFunc = NativeMethods.FO_DELETE,
                pFrom = pFrom,
                pTo = null,
                fFlags = NativeMethods.FOF_ALLOWUNDO | NativeMethods.FOF_NOCONFIRMATION | NativeMethods.FOF_SILENT | NativeMethods.FOF_NOERRORUI,
            };

            int res = NativeMethods.SHFileOperation(ref op);
            if (res == 0 && !op.fAnyOperationsAborted)
            {
                Refresh();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Dosyalar Çöp Kutusuna taşınamadı");
        }
    }
}
