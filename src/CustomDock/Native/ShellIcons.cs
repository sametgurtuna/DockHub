using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CustomDock.Core;
using static CustomDock.Native.NativeMethods;

namespace CustomDock.Native;

/// <summary>Yüksek çözünürlüklü kabuk ikonları ve kısayol (.lnk) bilgileri.</summary>
public static class ShellIcons
{
    private static readonly Dictionary<string, ImageSource?> IconCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, (string? Target, string? AppId)> LinkCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Dosya, klasör, kısayol veya "shell:AppsFolder\AUMID" için ikon (piksel boyutu).</summary>
    public static ImageSource? GetIcon(string path, int sizePx = 96)
    {
        string key = $"{sizePx}|{path}";
        if (IconCache.TryGetValue(key, out var cached)) return cached;

        ImageSource? image = null;
        try
        {
            image = GetShellItemImage(path, sizePx) ?? GetFileInfoIcon(path);
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"İkon alınamadı: {path}");
        }

        IconCache[key] = image;
        return image;
    }

    public static void ClearCache(string path)
    {
        foreach (var key in IconCache.Keys.Where(k => k.EndsWith("|" + path, StringComparison.OrdinalIgnoreCase)).ToList())
            IconCache.Remove(key);
    }

    private static ImageSource? GetShellItemImage(string path, int size)
    {
        var iid = typeof(IShellItemImageFactory).GUID;
        if (SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var factory) != 0 || factory is null)
            return null;

        try
        {
            const int SIIGBF_BIGGERSIZEOK = 0x1, SIIGBF_ICONONLY = 0x4;
            if (factory.GetImage(new NativeSize { cx = size, cy = size }, SIIGBF_BIGGERSIZEOK | SIIGBF_ICONONLY, out var hbitmap) != 0)
                return null;
            try
            {
                return BitmapFromHBitmap(hbitmap);
            }
            finally
            {
                DeleteObject(hbitmap);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(factory);
        }
    }

    /// <summary>Alfa kanalını koruyarak HBITMAP → BitmapSource.</summary>
    private static BitmapSource? BitmapFromHBitmap(IntPtr hbitmap)
    {
        var dib = new DIBSECTION();
        if (GetObject(hbitmap, Marshal.SizeOf<DIBSECTION>(), ref dib) == 0 || dib.dsBm.bmBits == IntPtr.Zero || dib.dsBm.bmBitsPixel != 32)
        {
            var fallback = Imaging.CreateBitmapSourceFromHBitmap(hbitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            fallback.Freeze();
            return fallback;
        }

        int width = dib.dsBm.bmWidth, height = Math.Abs(dib.dsBm.bmHeight), stride = width * 4;
        var pixels = new byte[stride * height];
        Marshal.Copy(dib.dsBm.bmBits, pixels, 0, pixels.Length);

        bool hasAlpha = false;
        for (int i = 3; i < pixels.Length; i += 4)
        {
            if (pixels[i] != 0) { hasAlpha = true; break; }
        }
        if (!hasAlpha)
        {
            for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
        }

        if (dib.dsBmih.biHeight > 0)
        {
            var flipped = new byte[pixels.Length];
            for (int y = 0; y < height; y++)
                Buffer.BlockCopy(pixels, y * stride, flipped, (height - 1 - y) * stride, stride);
            pixels = flipped;
        }

        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, pixels, stride);
        bitmap.Freeze();
        return bitmap;
    }

    private static ImageSource? GetFileInfoIcon(string path)
    {
        const uint SHGFI_ICON = 0x100, SHGFI_LARGEICON = 0x0;
        var info = new SHFILEINFO();
        SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_ICON | SHGFI_LARGEICON);
        if (info.hIcon == IntPtr.Zero) return null;
        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            DestroyIcon(info.hIcon);
        }
    }

    /// <summary>Kısayolun hedef yolu ve AppUserModelID'si.</summary>
    public static (string? Target, string? AppId) ReadShortcut(string lnkPath)
    {
        if (LinkCache.TryGetValue(lnkPath, out var cached)) return cached;

        (string? Target, string? AppId) result = (null, null);
        object? link = null;
        try
        {
            link = new ShellLinkCoClass();
            ((IPersistFile)link).Load(lnkPath, 0);
            var sb = new StringBuilder(1024);
            ((IShellLinkW)link).GetPath(sb, sb.Capacity, IntPtr.Zero, 0);
            string? target = sb.ToString();
            result.Target = string.IsNullOrWhiteSpace(target) ? null : target;

            if (link is IPropertyStore store)
            {
                var key = new PropertyKey(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5); // PKEY_AppUserModel_ID
                if (store.GetValue(ref key, out var value) == 0)
                {
                    try
                    {
                        if (value.vt == 31 /* VT_LPWSTR */ && value.pointer != IntPtr.Zero)
                            result.AppId = Marshal.PtrToStringUni(value.pointer);
                    }
                    finally
                    {
                        PropVariantClear(ref value);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Kısayol okunamadı: {lnkPath}");
        }
        finally
        {
            if (link is not null) Marshal.ReleaseComObject(link);
        }

        LinkCache[lnkPath] = result;
        return result;
    }

    public static string? ResolveShortcut(string lnkPath) => ReadShortcut(lnkPath).Target;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(
        string path, IntPtr bindContext, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory? factory);

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant pvar);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize
    {
        public int cx;
        public int cy;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct PropertyKey
    {
        public Guid fmtid;
        public uint pid;

        public PropertyKey(Guid fmtid, uint pid)
        {
            this.fmtid = fmtid;
            this.pid = pid;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant
    {
        public ushort vt;
        public ushort r1;
        public ushort r2;
        public ushort r3;
        public IntPtr pointer;
        public IntPtr pointer2;
    }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(NativeSize size, int flags, out IntPtr phbm);
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLinkCoClass
    {
    }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int cch, IntPtr findData, int flags);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }
}
