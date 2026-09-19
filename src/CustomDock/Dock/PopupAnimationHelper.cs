using System.Windows;
using System.Windows.Controls.Primitives;
using CustomDock.Controls;
using CustomDock.Core;

namespace CustomDock.Dock;

/// <summary>
/// Coordinates macOS-style fluid popup opening (spring zoom + fade) and closing (shrink + fade)
/// across folder stacks, widget popups, and tray overflow.
/// </summary>
public static class PopupAnimationHelper
{
    private static readonly HashSet<Popup> s_closingPopups = new();

    public static bool IsClosing(Popup popup)
    {
        if (popup is null) return false;
        lock (s_closingPopups)
        {
            return s_closingPopups.Contains(popup);
        }
    }

    public static Point GetOrigin(DockEdge edge) => edge switch
    {
        DockEdge.Top => new Point(0.5, 0.0),
        DockEdge.Left => new Point(0.0, 0.5),
        DockEdge.Right => new Point(1.0, 0.5),
        _ => new Point(0.5, 1.0),
    };

    /// <summary>Opens popup with macOS-style spring zoom and fade-in.</summary>
    public static void AnimateOpen(Popup popup, DockEdge edge, int durationMs = 230)
    {
        if (popup is null) return;
        lock (s_closingPopups)
        {
            s_closingPopups.Remove(popup);
        }

        popup.AllowsTransparency = true;
        popup.PopupAnimation = PopupAnimation.None;
        popup.StaysOpen = true;

        if (popup.Child is FrameworkElement child)
        {
            var offset = PopupPlacement.EnterOffset(edge);
            var origin = GetOrigin(edge);
            Motion.PopIn(child, offset, origin, durationMs);
        }

        popup.IsOpen = true;
    }

    /// <summary>Closes popup with macOS-style shrink and fade-out, then sets IsOpen = false.</summary>
    public static void ClosePopup(Popup popup, DockEdge edge, Action? onClosed = null, int durationMs = 160)
    {
        if (popup is null || !popup.IsOpen)
        {
            onClosed?.Invoke();
            return;
        }

        lock (s_closingPopups)
        {
            if (!s_closingPopups.Add(popup))
                return; // Already closing
        }

        if (popup.Child is FrameworkElement child)
        {
            var offset = PopupPlacement.EnterOffset(edge);
            Motion.PopOut(child, offset, () =>
            {
                lock (s_closingPopups)
                {
                    s_closingPopups.Remove(popup);
                }

                popup.IsOpen = false;
                child.Opacity = 1.0;
                onClosed?.Invoke();
            }, durationMs);
        }
        else
        {
            lock (s_closingPopups)
            {
                s_closingPopups.Remove(popup);
            }
            popup.IsOpen = false;
            onClosed?.Invoke();
        }
    }
}
