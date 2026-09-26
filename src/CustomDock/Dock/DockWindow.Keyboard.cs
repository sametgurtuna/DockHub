using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CustomDock.Core;
using CustomDock.Shell;
using static CustomDock.Native.NativeMethods;

namespace CustomDock.Dock;

/// <summary>
/// Keyboard mode (the "Move focus to the dock" shortcut): arrow keys move a focus ring over the dock's items,
/// Enter activates, the menu key (or Shift+F10) opens the item's menu, Esc returns to the previous window.
/// </summary>
public partial class DockWindow
{
    private Border? _focusRing;
    private int _keyboardIndex = -1;
    private IntPtr _windowBeforeKeyboard;

    public bool IsKeyboardMode => _keyboardIndex >= 0;

    /// <summary>Global shortcut target: puts the main dock into keyboard mode.</summary>
    public static void FocusMainDock()
    {
        var dock = s_docks.FirstOrDefault(d => d.IsMain);
        dock?.EnterKeyboardMode();
    }

    private List<FrameworkElement> KeyboardItems() =>
        ItemsPanel.Children.OfType<FrameworkElement>()
            .Concat(EndItemsPanel.Children.OfType<FrameworkElement>())
            .Where(e => e is AppButton or WidgetItemView or GroupItemView && e.IsVisible)
            .ToList();

    public void EnterKeyboardMode()
    {
        if (_closing || _hwnd == IntPtr.Zero) return;
        _windowBeforeKeyboard = GetForegroundWindow();
        Reveal();
        ActivateForInput();
        Focus();
        _keyboardIndex = 0;
        PreviewKeyDown -= OnKeyboardModeKey;
        PreviewKeyDown += OnKeyboardModeKey;
        Deactivated -= OnKeyboardModeDeactivated;
        Deactivated += OnKeyboardModeDeactivated;
        MoveKeyboardFocus(0);
    }

    private void ExitKeyboardMode(bool restoreWindow)
    {
        if (!IsKeyboardMode) return;
        _keyboardIndex = -1;
        PreviewKeyDown -= OnKeyboardModeKey;
        Deactivated -= OnKeyboardModeDeactivated;
        if (_focusRing is not null) _focusRing.Visibility = Visibility.Collapsed;
        if (restoreWindow && _windowBeforeKeyboard != IntPtr.Zero && IsWindow(_windowBeforeKeyboard))
            SetForegroundWindow(_windowBeforeKeyboard);
    }

    private void OnKeyboardModeDeactivated(object? sender, EventArgs e)
    {
        // A menu opened from the keyboard keeps the mode; clicking elsewhere ends it.
        if (_openMenus.Count == 0) ExitKeyboardMode(restoreWindow: false);
    }

    private void OnKeyboardModeKey(object sender, KeyEventArgs e)
    {
        var items = KeyboardItems();
        if (items.Count == 0) { ExitKeyboardMode(true); return; }
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        bool back = IsVertical ? key == Key.Up : key == Key.Left;
        bool forward = IsVertical ? key == Key.Down : key == Key.Right;

        if (back || forward) MoveKeyboardFocus(Math.Clamp(_keyboardIndex + (forward ? 1 : -1), 0, items.Count - 1));
        else if (key == Key.Home) MoveKeyboardFocus(0);
        else if (key == Key.End) MoveKeyboardFocus(items.Count - 1);
        else if (key is Key.Enter or Key.Space) ActivateKeyboardItem(items[_keyboardIndex], Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
        else if (key == Key.Apps || (key == Key.F10 && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))) OpenKeyboardItemMenu(items[_keyboardIndex]);
        else if (key == Key.Escape) ExitKeyboardMode(restoreWindow: true);
        else return;
        e.Handled = true;
    }

    private void MoveKeyboardFocus(int index)
    {
        var items = KeyboardItems();
        if (items.Count == 0) return;
        _keyboardIndex = Math.Clamp(index, 0, items.Count - 1);
        var item = items[_keyboardIndex];
        item.BringIntoView();

        if (_focusRing is null)
        {
            _focusRing = new Border
            {
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(8),
                IsHitTestVisible = false,
            };
            _focusRing.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
            CaretLayer.Children.Add(_focusRing);
        }

        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            if (_focusRing is null || !IsKeyboardMode) return;
            var host = EndItemsPanel.IsAncestorOf(item) ? null : CenterZone;
            if (host is null || !host.IsAncestorOf(item))
            {
                // Right-pinned widgets live outside the scroll area; the ring can't follow them there.
                _focusRing.Visibility = Visibility.Collapsed;
            }
            else
            {
                var bounds = item.TransformToAncestor(host).TransformBounds(new Rect(item.RenderSize));
                Canvas.SetLeft(_focusRing, bounds.X - 1);
                Canvas.SetTop(_focusRing, bounds.Y + 1);
                _focusRing.Width = bounds.Width + 2;
                _focusRing.Height = Math.Max(0, bounds.Height - 2);
                _focusRing.Visibility = Visibility.Visible;
            }
            // Screen readers follow the item.
            if (System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(item) is { } peer)
                peer.RaiseAutomationEvent(System.Windows.Automation.Peers.AutomationEvents.AutomationFocusChanged);
        });
    }

    private void ActivateKeyboardItem(FrameworkElement item, bool newInstance)
    {
        switch (item)
        {
            case AppButton app:
                ExitKeyboardMode(restoreWindow: false);
                app.InvokeShortcut(newInstance ? AppShortcutMode.NewInstance : AppShortcutMode.Activate);
                break;
            case GroupItemView group:
                group.OpenFan();
                break;
            case WidgetItemView widget:
                widget.ActivateFromKeyboard();
                break;
        }
    }

    private static void OpenKeyboardItemMenu(FrameworkElement item)
    {
        switch (item)
        {
            case AppButton app: app.OpenContextMenu(); break;
            case GroupItemView group: group.OpenContextMenu(); break;
            case WidgetItemView widget: widget.OpenContextMenu(); break;
        }
    }
}
