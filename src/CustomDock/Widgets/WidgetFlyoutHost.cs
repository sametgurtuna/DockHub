using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CustomDock.Controls;
using CustomDock.Dock;

namespace CustomDock.Widgets;

/// <summary>
/// What every widget panel does the same way: it opens next to its widget on the dock's side with the dock's
/// animation, keeps the dock from hiding while open, closes on a click elsewhere, with Esc or its close button, and,
/// when the dock is used with the keyboard (keyboard mode), takes the focus on its first control and gives it back to
/// the dock when it closes.
/// </summary>
public static class WidgetFlyoutHost
{
    private static readonly DependencyProperty CloseActionProperty = DependencyProperty.RegisterAttached(
        "CloseAction", typeof(Action), typeof(WidgetFlyoutHost), new PropertyMetadata(null));

    /// <summary>Opens <paramref name="popup"/> at <paramref name="anchor"/>; <paramref name="close"/> closes it (animated).</summary>
    /// <param name="closed">Runs once when it has closed.</param>
    public static void Open(Popup popup, FrameworkElement anchor, IWidgetHost host, double gap, Action close, Action? closed = null)
    {
        var edge = host.Edge;
        PopupPlacement.PlacePopup(popup, anchor, edge, gap);

        host.BeginInteraction();
        bool keyboard = host.IsKeyboardNavigating;
        void OnClosed(object? sender, EventArgs e)
        {
            popup.Closed -= OnClosed;
            try { closed?.Invoke(); }
            finally
            {
                host.EndInteraction();
                if (keyboard) ReturnFocus(popup, host.Window);
            }
        }
        popup.Closed += OnClosed;
        Attach(popup, close);
        GlobalPopupDismissHook.RegisterPopup(popup);
        PopupAnimationHelper.AnimateOpen(popup, edge, anchor);
        if (keyboard) FocusFirst(popup);
    }

    /// <summary>Esc and the panel's close button call <paramref name="close"/>. Safe to call again for the same popup.</summary>
    public static void Attach(Popup popup, Action close)
    {
        bool first = popup.GetValue(CloseActionProperty) is null;
        popup.SetValue(CloseActionProperty, close);
        if (!first || popup.Child is not UIElement child) return;
        // Bubbling, not preview: a control that uses Esc itself (an open drop-down) gets it first.
        child.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler((_, e) =>
        {
            if (e.Key != Key.Escape || popup.GetValue(CloseActionProperty) is not Action action) return;
            e.Handled = true;
            action();
        }));
        child.AddHandler(WidgetFlyout.CloseRequestedEvent, new RoutedEventHandler((_, e) =>
        {
            e.Handled = true;
            (popup.GetValue(CloseActionProperty) as Action)?.Invoke();
        }));
    }

    /// <summary>
    /// The first control of the panel's content gets the focus (then its header's buttons, then its close button),
    /// unless the widget already put it somewhere (a text box).
    /// </summary>
    public static void FocusFirst(Popup popup)
    {
        popup.Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (!popup.IsOpen || popup.Child is not UIElement child || child.IsKeyboardFocusWithin) return;
            var roots = child is WidgetFlyout flyout
                ? new[] { flyout.Content as DependencyObject, flyout.HeaderActions as DependencyObject, flyout }
                : new[] { (DependencyObject?)child };
            foreach (var root in roots)
            {
                if (root is not null && FirstFocusable(root) is { } target)
                {
                    target.Focus();
                    return;
                }
            }
        });
    }

    /// <summary>After the panel closed, keyboard mode goes on at the dock, unless the focus has already moved elsewhere.</summary>
    public static void ReturnFocus(Popup popup, Window window)
    {
        if (!window.IsActive) return;
        var focused = Keyboard.FocusedElement as DependencyObject;
        if (focused is null || ReferenceEquals(focused, window) || (popup.Child is { } child && IsWithin(focused, child)))
            window.Focus();
    }

    private static UIElement? FirstFocusable(DependencyObject root)
    {
        if (root is UIElement { Focusable: true, IsEnabled: true, IsVisible: true } element && KeyboardNavigation.GetIsTabStop(element))
            return element;
        int count = root is Visual ? VisualTreeHelper.GetChildrenCount(root) : 0;
        for (int i = 0; i < count; i++)
            if (FirstFocusable(VisualTreeHelper.GetChild(root, i)) is { } found) return found;
        return null;
    }

    private static bool IsWithin(DependencyObject element, DependencyObject root)
    {
        for (var node = element; node is not null; node = (node is Visual ? VisualTreeHelper.GetParent(node) : null) ?? LogicalTreeHelper.GetParent(node))
            if (ReferenceEquals(node, root)) return true;
        return false;
    }
}
