using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CustomDock.Controls;

namespace CustomDock.Dock;

/// <summary>
/// "Wave" magnification effect where items close to the cursor grow slightly, like macOS Dock.
/// Intended solely for hover visual feedback (like a top-layer visual effect);
/// does not alter layout or resize panels (Panel.Children layout is unaffected).
/// </summary>
public sealed class DockMagnifier : IDisposable
{
    private const double MaxBoost = 0.16; // at most 16% magnification
    private const double RadiusMultiplier = 1.7; // effect radius = item size * multiplier

    private readonly Panel _host;
    private readonly Func<bool> _isVertical;
    private readonly MouseEventHandler _onMouseMove;
    private readonly MouseEventHandler _onMouseLeave;
    private readonly Action<bool> _onDragging;
    private readonly Dictionary<FrameworkElement, double> _lastTarget = new();
    private bool _active;

    public DockMagnifier(Panel host, Func<bool> isVertical)
    {
        _host = host;
        _isVertical = isVertical;
        _onMouseMove = OnMouseMove;
        _onMouseLeave = (_, _) => Reset();
        _onDragging = dragging => { if (dragging) Reset(); };

        _host.MouseMove += _onMouseMove;
        _host.MouseLeave += _onMouseLeave;
        DockDragHelper.DraggingChanged += _onDragging;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (DockDragHelper.JustDragged) return;

        bool vertical = _isVertical();
        var pointer = e.GetPosition(_host);
        double pointerAxis = vertical ? pointer.Y : pointer.X;
        bool any = false;

        foreach (var child in _host.Children)
        {
            // The wave (magnification) effect applies only to app icons (AppButton).
            // Prevents wide widget cards from overlapping neighboring elements when magnified.
            if (child is not AppButton { Visibility: Visibility.Visible } element)
                continue;

            double size = vertical ? element.ActualHeight : element.ActualWidth;
            if (size <= 0) continue;

            Point topLeft;
            try { topLeft = element.TranslatePoint(new Point(0, 0), _host); }
            catch (InvalidOperationException) { continue; } // not yet in visual tree

            double center = (vertical ? topLeft.Y : topLeft.X) + size / 2;
            double distance = Math.Abs(pointerAxis - center);
            double closeness = Math.Clamp(1 - distance / (size * RadiusMultiplier), 0, 1);
            double target = 1 + MaxBoost * (closeness * closeness); // quadratic falloff: smooth "wave" toward neighbors

            // Do not restart animation to the same target on every single pixel movement (avoids unnecessary overhead).
            if (_lastTarget.TryGetValue(element, out var previous) && Math.Abs(previous - target) < 0.004)
            {
                if (target > 1.001) any = true;
                continue;
            }
            _lastTarget[element] = target;

            var (scale, _) = Motion.GetItemTransform(element);
            Motion.Scale(scale, target, 90);
            any = true;
        }

        _active = any;
    }

    private void Reset()
    {
        if (!_active) return;
        _active = false;
        foreach (var child in _host.Children)
        {
            if (child is not AppButton { RenderTransform: TransformGroup { Children: [ScaleTransform scale, _] } })
                continue;
            Motion.Scale(scale, 1, 200);
        }
        _lastTarget.Clear();
    }

    public void Dispose()
    {
        _host.MouseMove -= _onMouseMove;
        _host.MouseLeave -= _onMouseLeave;
        DockDragHelper.DraggingChanged -= _onDragging;
    }
}
