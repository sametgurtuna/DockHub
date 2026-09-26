using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CustomDock.Core;

namespace CustomDock.Controls;

/// <summary>
/// Captures a keyboard shortcut: click, then press the keys. Esc cancels, Backspace/Delete clears.
/// A shortcut needs Ctrl, Alt or Win.
/// </summary>
public sealed class HotkeyBox : Border
{
    private readonly TextBlock _text;
    private HotkeyGesture? _gesture;
    private bool _capturing;

    public HotkeyBox()
    {
        MinWidth = 160;
        Height = 32;
        CornerRadius = new CornerRadius(5);
        BorderThickness = new Thickness(1);
        Padding = new Thickness(10, 0, 10, 0);
        Focusable = true;
        Cursor = Cursors.Hand;
        SetResourceReference(BackgroundProperty, "ControlBrush");
        SetResourceReference(BorderBrushProperty, "ControlBorderBrush");

        _text = new TextBlock { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        _text.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        Child = _text;

        MouseLeftButtonUp += (_, e) => { e.Handled = true; Focus(); };
        GotKeyboardFocus += (_, _) => { _capturing = true; UpdateText(); SetResourceReference(BorderBrushProperty, "AccentBrush"); };
        LostKeyboardFocus += (_, _) => { _capturing = false; UpdateText(); SetResourceReference(BorderBrushProperty, "ControlBorderBrush"); };
        PreviewKeyDown += OnPreviewKeyDown;
        UpdateText();
    }

    public HotkeyGesture? Gesture
    {
        get => _gesture;
        set { _gesture = value; UpdateText(); }
    }

    /// <summary>Raised when the user records or clears a shortcut.</summary>
    public event Action<HotkeyGesture?>? GestureChanged;

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        switch (key)
        {
            case Key.Escape:
            case Key.Tab:
                Keyboard.ClearFocus();
                MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                return;
            case Key.Back:
            case Key.Delete:
                Commit(null);
                return;
        }
        if (HotkeyGesture.FromKeys(Keyboard.Modifiers, key) is { } gesture)
            Commit(gesture);
    }

    private void Commit(HotkeyGesture? gesture)
    {
        _gesture = gesture;
        GestureChanged?.Invoke(gesture);
        Keyboard.ClearFocus();
        UpdateText();
    }

    private void UpdateText()
    {
        _text.Text = _capturing ? "Press a shortcut…" : _gesture?.ToString().Replace("+", " + ") ?? "Not set";
        _text.Opacity = _capturing || _gesture is not null ? 1 : 0.6;
        ToolTip = "Click, then press the keys. Backspace clears, Esc cancels.";
    }
}
