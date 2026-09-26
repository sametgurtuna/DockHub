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

/// <summary>High-resolution shell icons and shortcut (.lnk) information.</summary>
public static class ShellIcons
{
    private const int IconCacheCapacity = 256;
    private static readonly TimeSpan FailedShortcutRetry = TimeSpan.FromSeconds(60);

    private static readonly Dictionary<string, LinkedListNode<(string Key, ImageSource Image)>> IconCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly LinkedList<(string Key, ImageSource Image)> IconOrder = new();
    private static readonly Dictionary<string, (ShortcutInfo Info, DateTime Stamp, bool Failed)> LinkCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> LoggedFailures = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Icon (pixel size) for a file, folder, shortcut, or "shell:AppsFolder\AUMID". Failures are not cached.</summary>
    public static ImageSource? GetIcon(string path, int sizePx = 96)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        string key = $"{sizePx}|{path}";
        if (IconCache.TryGetValue(key, out var node))
        {
            IconOrder.Remove(node);
            IconOrder.AddFirst(node);
            return node.Value.Image;
        }

        ImageSource? image = null;
        string? failure = null;
        try
        {
            image = GetShellItemImage(path, sizePx);
            if (image is null) { failure = "shell item image"; image = GetFileInfoIcon(path); }
            if (image is null && File.Exists(path))
            {
                failure += ", file info";
                try
                {
                    using var sysIcon = System.Drawing.Icon.ExtractAssociatedIcon(path);
                    if (sysIcon is not null)
                    {
                        var bs = Imaging.CreateBitmapSourceFromHIcon(sysIcon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                        bs.Freeze();
                        image = bs;
                    }
                }
                catch { /* ignore */ }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Failed to retrieve icon: {path}");
        }

        if (image is null)
        {
            if (LoggedFailures.Add(key))
                Log.Debug($"Icon not found ({failure}, exists: {File.Exists(path) || Directory.Exists(path)}): {path}");
            return null;
        }

        LoggedFailures.Remove(key);
        var added = IconOrder.AddFirst((key, image));
        IconCache[key] = added;
        if (IconCache.Count > IconCacheCapacity && IconOrder.Last is { } oldest)
        {
            IconOrder.RemoveLast();
            IconCache.Remove(oldest.Value.Key);
        }
        return image;
    }

    /// <summary>Icon from an icon resource location (e.g. a shortcut's "Change Icon" setting).</summary>
    public static ImageSource? GetIconFromLocation(string file, int index, int sizePx = 96)
    {
        if (string.IsNullOrWhiteSpace(file)) return null;
        file = Environment.ExpandEnvironmentVariables(file);
        if (!File.Exists(file)) return null;

        var icons = new IntPtr[1];
        try
        {
            if (PrivateExtractIcons(file, index, sizePx, sizePx, icons, null, 1, 0) == 0 || icons[0] == IntPtr.Zero)
                return null;
            var source = Imaging.CreateBitmapSourceFromHIcon(icons[0], Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch (Exception ex)
        {
            Log.Debug($"Icon location failed {file},{index}: {ex.Message}");
            return null;
        }
        finally
        {
            if (icons[0] != IntPtr.Zero) DestroyIcon(icons[0]);
        }
    }

    /// <summary>Retrieves icon from window handle (HWND) via WM_GETICON and window class.</summary>
    public static ImageSource? GetWindowIcon(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return null;
        try
        {
            IntPtr hIcon = SendMessage(hwnd, WM_GETICON, new IntPtr(ICON_BIG), IntPtr.Zero);
            if (hIcon == IntPtr.Zero)
                hIcon = SendMessage(hwnd, WM_GETICON, new IntPtr(ICON_SMALL2), IntPtr.Zero);
            if (hIcon == IntPtr.Zero)
                hIcon = SendMessage(hwnd, WM_GETICON, new IntPtr(ICON_SMALL), IntPtr.Zero);
            if (hIcon == IntPtr.Zero)
                hIcon = GetClassLongPtr(hwnd, GCLP_HICON);
            if (hIcon == IntPtr.Zero)
                hIcon = GetClassLongPtr(hwnd, GCLP_HICONSM);

            if (hIcon != IntPtr.Zero)
            {
                var source = Imaging.CreateBitmapSourceFromHIcon(hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to retrieve window icon");
        }
        return null;
    }

    private static ImageSource? s_defaultAppIcon;

    /// <summary>Elegant default application icon for custom apps whose icons cannot be found.</summary>
    public static ImageSource GetDefaultAppIcon()
    {
        if (s_defaultAppIcon is not null) return s_defaultAppIcon;

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var bgBrush = new SolidColorBrush(Color.FromRgb(48, 54, 68));
            bgBrush.Freeze();
            dc.DrawRoundedRectangle(bgBrush, null, new Rect(0, 0, 96, 96), 22, 22);

            var barBrush = new SolidColorBrush(Color.FromArgb(140, 255, 255, 255));
            barBrush.Freeze();
            dc.DrawRoundedRectangle(barBrush, null, new Rect(18, 18, 60, 12), 4, 4);

            var bodyBrush = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255));
            bodyBrush.Freeze();
            dc.DrawRoundedRectangle(bodyBrush, null, new Rect(18, 34, 60, 44), 4, 4);
        }

        var rtb = new RenderTargetBitmap(96, 96, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        rtb.Freeze();
        s_defaultAppIcon = rtb;
        return s_defaultAppIcon;
    }

    public static void ClearCache(string path)
    {
        foreach (var key in IconCache.Keys.Where(k => k.EndsWith("|" + path, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            IconOrder.Remove(IconCache[key]);
            IconCache.Remove(key);
        }
        LinkCache.Remove(path);
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

    /// <summary>Preserves alpha channel when converting HBITMAP -> BitmapSource.</summary>
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
            // A completely empty bitmap (icon not extracted yet) is a failure, not a black square.
            if (pixels.All(b => b == 0)) return null;
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

    /// <summary>Target path and AppUserModelID of shortcut.</summary>
    public static (string? Target, string? AppId) ReadShortcut(string lnkPath)
    {
        var info = ReadShortcutInfo(lnkPath);
        return (info.Target, info.AppId);
    }

    /// <summary>
    /// Full shortcut information. Successful reads are cached until the file changes; failed reads are retried
    /// after a minute, so a transient error (file being replaced by an updater) doesn't stick for the whole session.
    /// </summary>
    public static ShortcutInfo ReadShortcutInfo(string lnkPath)
    {
        DateTime stamp;
        try { stamp = File.GetLastWriteTimeUtc(lnkPath); }
        catch { stamp = DateTime.MinValue; }

        if (LinkCache.TryGetValue(lnkPath, out var cached))
        {
            if (!cached.Failed && cached.Stamp == stamp) return cached.Info;
            if (cached.Failed && DateTime.UtcNow - cached.Stamp < FailedShortcutRetry) return cached.Info;
        }

        var result = new ShortcutInfo();
        bool failed = false;
        object? link = null;
        try
        {
            link = new ShellLinkCoClass();
            ((IPersistFile)link).Load(lnkPath, 0);
            var shellLink = (IShellLinkW)link;

            var sb = new StringBuilder(1024);
            shellLink.GetPath(sb, sb.Capacity, IntPtr.Zero, 0);
            result.Target = NullIfEmpty(sb.ToString());

            sb.Clear();
            shellLink.GetArguments(sb, sb.Capacity);
            result.Arguments = NullIfEmpty(sb.ToString());

            sb.Clear();
            shellLink.GetIconLocation(sb, sb.Capacity, out int iconIndex);
            result.IconFile = NullIfEmpty(sb.ToString());
            result.IconIndex = iconIndex;

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
            failed = true;
            Log.Warn($"Failed to read shortcut {lnkPath}: {ex.Message}");
        }
        finally
        {
            if (link is not null) Marshal.ReleaseComObject(link);
        }

        LinkCache[lnkPath] = (result, failed ? DateTime.UtcNow : stamp, failed);
        return result;
    }

    public static string? ResolveShortcut(string lnkPath) => ReadShortcut(lnkPath).Target;

    private static string? NullIfEmpty(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint PrivateExtractIcons(string file, int iconIndex, int cx, int cy, IntPtr[] icons, int[]? iconIds, uint count, uint flags);

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

    /// <summary>IShellLinkW; the full vtable order is required to reach GetArguments / GetIconLocation.</summary>
    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int cch, IntPtr findData, int flags);
        void GetIDList(out IntPtr pidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int showCmd);
        void SetShowCmd(int showCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int cch, out int iconIndex);
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

/// <summary>Information read from a .lnk shortcut.</summary>
public sealed class ShortcutInfo
{
    public string? Target { get; set; }

    public string? Arguments { get; set; }

    public string? AppId { get; set; }

    public string? IconFile { get; set; }

    public int IconIndex { get; set; }
}
