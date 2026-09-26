using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CustomDock.Core;

namespace CustomDock.Services;

/// <summary>One copied text or image.</summary>
public sealed class ClipboardEntry : ObservableObject
{
    private bool _pinned;

    public string? Text { get; init; }

    public BitmapSource? Image { get; init; }

    public DateTime CopiedAt { get; set; } = DateTime.Now;

    public bool IsPinned { get => _pinned; set => Set(ref _pinned, value); }

    /// <summary>One-line preview for lists.</summary>
    public string Preview
    {
        get
        {
            if (Text is null) return L.T("Image {0}×{1}", Image?.PixelWidth ?? 0, Image?.PixelHeight ?? 0);
            string line = string.Join(" ", Text.Split(new[] { '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)).Trim();
            return line.Length > 90 ? line[..90] + "…" : line;
        }
    }
}

/// <summary>
/// Keeps the last copied texts and images while DockHub runs (memory only, never written to disk). Content that
/// apps mark as private (password managers set ExcludeClipboardContentFromMonitorProcessing or
/// CanIncludeInClipboardHistory = 0) is never recorded.
/// </summary>
public sealed class ClipboardHistoryService : IDisposable
{
    private const int WM_CLIPBOARDUPDATE = 0x031D;
    private const int Capacity = 25;
    private const int MaxTextLength = 20_000;

    private HwndSource? _window;
    private int _subscribers;
    private bool _restoring;

    public List<ClipboardEntry> Entries { get; } = new();

    public event Action? Changed;

    /// <summary>Starts listening when the first widget attaches; stops when the last one detaches.</summary>
    public void Subscribe()
    {
        if (_subscribers++ > 0) return;
        _window = new HwndSource(new HwndSourceParameters("DockHubClipboard") { ParentWindow = new IntPtr(-3) });
        _window.AddHook(WndProc);
        AddClipboardFormatListener(_window.Handle);
    }

    public void Unsubscribe()
    {
        if (--_subscribers > 0 || _window is null) return;
        RemoveClipboardFormatListener(_window.Handle);
        _window.RemoveHook(WndProc);
        _window.Dispose();
        _window = null;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_CLIPBOARDUPDATE && !_restoring)
            Application.Current?.Dispatcher.BeginInvoke(Capture);
        return IntPtr.Zero;
    }

    private void Capture()
    {
        try
        {
            var data = RetryClipboard(Clipboard.GetDataObject);
            if (data is null || IsPrivate(data)) return;

            ClipboardEntry? entry = null;
            if (data.GetDataPresent(DataFormats.UnicodeText) && data.GetData(DataFormats.UnicodeText) is string text && text.Trim().Length > 0)
                entry = new ClipboardEntry { Text = text.Length > MaxTextLength ? text[..MaxTextLength] : text };
            else if (data.GetDataPresent(DataFormats.Bitmap) && RetryClipboard(Clipboard.GetImage) is { } image)
                entry = new ClipboardEntry { Image = Thumbnail(image) };
            if (entry is null) return;

            // Copying the same text again moves it to the top instead of adding a duplicate.
            var existing = entry.Text is null ? null : Entries.FirstOrDefault(e => e.Text == entry.Text);
            if (existing is not null)
            {
                Entries.Remove(existing);
                existing.CopiedAt = DateTime.Now;
                entry = existing;
            }
            Entries.Insert(0, entry);
            while (Entries.Count > Capacity && Entries.LastOrDefault(e => !e.IsPinned) is { } oldest) Entries.Remove(oldest);
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            Log.Debug($"Clipboard capture failed: {ex.Message}");
        }
    }

    /// <summary>Puts an entry back on the clipboard (and at the top of the list).</summary>
    public void Restore(ClipboardEntry entry)
    {
        try
        {
            _restoring = true;
            if (entry.Text is { } text) RetryClipboard(() => { Clipboard.SetText(text); return true; });
            else if (entry.Image is { } image) RetryClipboard(() => { Clipboard.SetImage(image); return true; });
            Entries.Remove(entry);
            entry.CopiedAt = DateTime.Now;
            Entries.Insert(0, entry);
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to restore clipboard entry");
        }
        finally
        {
            Application.Current?.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () => _restoring = false);
        }
    }

    public void Remove(ClipboardEntry entry)
    {
        Entries.Remove(entry);
        Changed?.Invoke();
    }

    public void TogglePin(ClipboardEntry entry)
    {
        entry.IsPinned = !entry.IsPinned;
        Changed?.Invoke();
    }

    /// <summary>Clears everything except pinned entries.</summary>
    public void Clear()
    {
        Entries.RemoveAll(e => !e.IsPinned);
        Changed?.Invoke();
    }

    private static bool IsPrivate(IDataObject data)
    {
        if (data.GetDataPresent("ExcludeClipboardContentFromMonitorProcessing")) return true;
        if (data.GetDataPresent("CanIncludeInClipboardHistory") && data.GetData("CanIncludeInClipboardHistory") is System.IO.MemoryStream stream)
        {
            var bytes = stream.ToArray();
            if (bytes.Length >= 4 && BitConverter.ToInt32(bytes, 0) == 0) return true;
        }
        return false;
    }

    private static BitmapSource Thumbnail(BitmapSource image)
    {
        double scale = Math.Min(1, 320.0 / Math.Max(image.PixelWidth, image.PixelHeight));
        BitmapSource result = scale < 1 ? new TransformedBitmap(image, new ScaleTransform(scale, scale)) : image;
        if (result.CanFreeze) result.Freeze();
        return result;
    }

    /// <summary>The clipboard is often briefly locked by the app that just wrote to it.</summary>
    private static T? RetryClipboard<T>(Func<T> action)
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try { return action(); }
            catch (COMException) { Thread.Sleep(40); }
            catch (ExternalException) { Thread.Sleep(40); }
        }
        return default;
    }

    public void Dispose()
    {
        _subscribers = 1;
        Unsubscribe();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
}
