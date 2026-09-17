using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CustomDock.Controls;

namespace CustomDock.Dock;

/// <summary>
/// macOS Dock'undaki gibi imlece yakın öğelerin hafifçe büyüdüğü "dalga" efekti.
/// Yalnızca fare üzerine gelme (üzerinde bir katman gibi görsel geri bildirim) amaçlıdır;
/// düzenle alakalı hiçbir şeyi değiştirmez, katmanı boyutlandırmaz (Panel.Children değişmez).
/// </summary>
public sealed class DockMagnifier : IDisposable
{
    private const double MaxBoost = 0.16; // en fazla %16 büyüme
    private const double RadiusMultiplier = 1.7; // etki alanı = öğe boyutu × bu kat

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
            if (child is not FrameworkElement { Visibility: Visibility.Visible } element || element is SeparatorView)
                continue;

            double size = vertical ? element.ActualHeight : element.ActualWidth;
            if (size <= 0) continue;

            Point topLeft;
            try { topLeft = element.TranslatePoint(new Point(0, 0), _host); }
            catch (InvalidOperationException) { continue; } // henüz görsel ağaçta değil

            double center = (vertical ? topLeft.Y : topLeft.X) + size / 2;
            double distance = Math.Abs(pointerAxis - center);
            double closeness = Math.Clamp(1 - distance / (size * RadiusMultiplier), 0, 1);
            double target = 1 + MaxBoost * (closeness * closeness); // karesel düşüş: komşulara doğru yumuşak "dalga"

            // Fare her piksel kaydığında aynı hedefe animasyonu yeniden başlatma (gereksiz nesne/iş).
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
            if (child is not FrameworkElement { RenderTransform: TransformGroup { Children: [ScaleTransform scale, _] } })
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
