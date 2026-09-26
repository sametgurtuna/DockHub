using System.Windows;
using System.Windows.Controls.Primitives;
using CustomDock.Controls;
using CustomDock.Core;

namespace CustomDock.Dock;

/// <summary>
/// Coordinates macOS Genie Effect opening and closing animations across all popups
/// (folders, widgets, compact flyouts, tray overflow).
/// </summary>
public static class PopupAnimationHelper
{
    public static bool IsClosing(Popup popup) => GenieEffectHelper.IsClosing(popup);

    /// <summary>Opens popup with authentic macOS Genie Effect emerging from anchor.</summary>
    public static void AnimateOpen(Popup popup, DockEdge edge, FrameworkElement? anchor = null, Action? onOpened = null)
    {
        if (Motion.IsReduced)
        {
            popup.AllowsTransparency = true;
            popup.IsOpen = true;
            if (popup.Child is FrameworkElement child) Motion.Appear(child);
            onOpened?.Invoke();
            return;
        }
        GenieEffectHelper.AnimateOpen(popup, edge, anchor ?? (popup.PlacementTarget as FrameworkElement), onOpened);
    }

    /// <summary>Closes popup with authentic macOS Genie Effect sucking into anchor.</summary>
    public static void ClosePopup(Popup popup, DockEdge edge, FrameworkElement? anchor = null, Action? onClosed = null)
    {
        if (Motion.IsReduced)
        {
            popup.IsOpen = false;
            onClosed?.Invoke();
            return;
        }
        GenieEffectHelper.ClosePopup(popup, edge, anchor ?? (popup.PlacementTarget as FrameworkElement), onClosed);
    }
}
