using System.Windows.Controls;
using System.Windows.Media;
using CustomDock.Core;
using ManagedShell.WindowsTasks;

namespace CustomDock.Dock;

public sealed partial class WindowPreviewWindow
{
    // ------------------------------------------------------------------ Aero Peek and window cycling

    private void StartPeek(ApplicationWindow window)
    {
        if (!AppServices.Config.PreviewPeek) return;
        _peekTarget = window;
        _peekTimer.Stop();
        _peekTimer.Start();
    }

    private void BeginPeek(ApplicationWindow window)
    {
        try
        {
            ManagedShell.Common.Helpers.WindowHelper.PeekWindow(true, window.Handle, _hwnd);
            _peeking = window;
        }
        catch (Exception ex)
        {
            Log.Debug($"Peek failed: {ex.Message}");
        }
    }

    private void EndPeek()
    {
        _peekTimer.Stop();
        if (_peeking is not { } window) return;
        _peeking = null;
        try { ManagedShell.Common.Helpers.WindowHelper.PeekWindow(false, window.Handle, _hwnd); }
        catch { /* ignore */ }
    }

    /// <summary>Mouse wheel over the preview (or its app button): brings the app's next or previous window forward.</summary>
    public bool CycleWindows(int delta)
    {
        if (!IsVisible || _previewItems.Count < 2) return false;
        EndPeek();
        var windows = _previewItems.Select(p => p.Window).ToList();
        int index = windows.FindIndex(w => w.State == ApplicationWindow.WindowState.Active);
        int next = delta < 0 ? (index + 1) % windows.Count : (index <= 0 ? windows.Count - 1 : index - 1);
        var window = windows[next];
        if (window.IsMinimized) window.Restore();
        window.BringToFront();

        for (int i = 0; i < _cardsPanel.Children.Count; i++)
        {
            if (_cardsPanel.Children[i] is not Border card) continue;
            if (i == next) card.SetResourceReference(Border.BackgroundProperty, "DockHoverBrush");
            else card.Background = Brushes.Transparent;
        }
        return true;
    }
}
