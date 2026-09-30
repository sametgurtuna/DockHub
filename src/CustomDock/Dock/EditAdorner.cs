using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using CustomDock.Core;

namespace CustomDock.Dock;

/// <summary>
/// Edit mode decorations over one dock item: a remove badge in its corner and, for widgets that have layouts of
/// other widths, a handle on the right edge that switches the layout while it is dragged. (On a left or right dock
/// widgets are tiles of one size, so the handle is only ever horizontal.)
/// </summary>
internal sealed class EditAdorner : Adorner
{
    private const double BadgeSize = 17;
    private const double HandleThickness = 5;
    private const double HandleLength = 18;
    /// <summary>Hit area across the handle, wider than the bar itself.</summary>
    private const double HandleHitArea = 14;
    /// <summary>Drag distance (DIP) that switches to the next width class.</summary>
    private const double ResizeStep = 28;

    private readonly VisualCollection _visuals;
    private readonly Canvas _canvas = new();
    private readonly Border _badge;
    private readonly Border? _handle;
    private readonly Func<int, bool>? _resize;
    private Point? _anchor;

    /// <param name="item">The dock item.</param>
    /// <param name="name">Its name, for screen readers.</param>
    /// <param name="remove">Removes the item.</param>
    /// <param name="resize">Switches to a narrower (-1) or wider (+1) layout; false when there is none. Null: no handle.</param>
    public EditAdorner(FrameworkElement item, string name, Action remove, Func<int, bool>? resize) : base(item)
    {
        _resize = resize;
        _visuals = new VisualCollection(this) { _canvas };

        _badge = CreateBadge(name, remove);
        _canvas.Children.Add(_badge);
        if (resize is not null)
        {
            _handle = CreateHandle(name);
            _canvas.Children.Add(_handle);
        }
    }

    private static Border CreateBadge(string name, Action remove)
    {
        var glyph = new TextBlock
        {
            Text = "",
            FontSize = 7.5,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        glyph.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        var badge = new Border
        {
            Width = BadgeSize,
            Height = BadgeSize,
            CornerRadius = new CornerRadius(BadgeSize / 2),
            Background = new SolidColorBrush(Color.FromArgb(0xF0, 0x3B, 0x3B, 0x41)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            Child = glyph,
            ToolTip = L.T("Remove {0}", name),
        };
        System.Windows.Automation.AutomationProperties.SetName(badge, L.T("Remove {0}", name));
        badge.MouseLeftButtonDown += (_, e) => e.Handled = true;
        badge.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            remove();
        };
        return badge;
    }

    private Border CreateHandle(string name)
    {
        var bar = new Border
        {
            Width = HandleThickness,
            Height = HandleLength,
            CornerRadius = new CornerRadius(HandleThickness / 2),
            Background = new SolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x59, 0x00, 0x00, 0x00)),
            BorderThickness = new Thickness(0.5),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var handle = new Border
        {
            // Transparent, not null: the whole area takes the mouse.
            Background = Brushes.Transparent,
            Width = HandleHitArea,
            Height = HandleLength + 8,
            Cursor = Cursors.SizeWE,
            Child = bar,
            ToolTip = L.T("Drag to resize"),
        };
        System.Windows.Automation.AutomationProperties.SetName(handle, L.T("Resize {0}", name));
        handle.MouseLeftButtonDown += OnHandleDown;
        handle.MouseMove += OnHandleMove;
        handle.MouseLeftButtonUp += OnHandleUp;
        handle.LostMouseCapture += (_, _) => _anchor = null;
        return handle;
    }

    // Positions in screen pixels: the card changes width while it is dragged, and a dock that fits its content moves
    // with it, so neither the card nor the window is a steady reference.
    private Point ScreenPoint(MouseEventArgs e) => AdornedElement.PointToScreen(e.GetPosition(AdornedElement));

    private double StepPixels => ResizeStep * VisualTreeHelper.GetDpi(this).DpiScaleX;

    private void OnHandleDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _anchor = ScreenPoint(e);
        ((UIElement)sender).CaptureMouse();
    }

    private void OnHandleMove(object sender, MouseEventArgs e)
    {
        if (_anchor is not { } anchor || _resize is null || !((UIElement)sender).IsMouseCaptured) return;
        var point = ScreenPoint(e);
        double delta = point.X - anchor.X;
        double step = StepPixels;
        if (Math.Abs(delta) < step) return;

        int direction = delta > 0 ? 1 : -1;
        if (_resize(direction))
        {
            // One step used; the rest of the drag counts from here.
            _anchor = new Point(anchor.X + direction * step, anchor.Y);
        }
        else
        {
            // No layout further that way: start over from where the pointer is, so dragging back works at once.
            _anchor = point;
        }
    }

    private void OnHandleUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _anchor = null;
        ((UIElement)sender).ReleaseMouseCapture();
    }

    protected override int VisualChildrenCount => _visuals.Count;

    protected override Visual GetVisualChild(int index) => _visuals[index];

    protected override Size MeasureOverride(Size constraint)
    {
        _canvas.Measure(constraint);
        return AdornedElement.RenderSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var size = AdornedElement.RenderSize;
        // Inside the item: the dock clips anything drawn beyond its own edge.
        Canvas.SetLeft(_badge, 1);
        Canvas.SetTop(_badge, 1);
        if (_handle is not null)
        {
            Canvas.SetLeft(_handle, size.Width - _handle.Width);
            Canvas.SetTop(_handle, (size.Height - _handle.Height) / 2);
        }
        _canvas.Arrange(new Rect(size));
        return finalSize;
    }
}
