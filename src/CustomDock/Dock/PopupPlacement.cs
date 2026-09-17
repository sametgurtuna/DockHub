using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using CustomDock.Core;

namespace CustomDock.Dock;

/// <summary>
/// Dock'tan açılan menü ve panelleri dock'un dışına, kenarına yaslı olarak yerleştirir
/// (aksi halde imleç konumunda açılıp dock'un üstüne/altına taşarlar).
/// </summary>
public static class PopupPlacement
{
    public readonly record struct Result(PlacementMode Mode, Rect Rectangle);

    /// <param name="target">Popup'ın bağlı olduğu öğe (dock içinde).</param>
    /// <param name="edge">Dock kenarı.</param>
    /// <param name="gap">Dock ile popup arasındaki boşluk (hedef koordinatlarında).</param>
    /// <param name="along">Kenar boyunca popup'ın başlangıcı (hedef koordinatlarında); null → hedefin başı.</param>
    public static Result Compute(FrameworkElement target, DockEdge edge, double gap, double? along)
    {
        var dock = DockBounds(target);
        double a = along ?? 0;
        return edge switch
        {
            DockEdge.Top => new(PlacementMode.Bottom, new Rect(a, dock.Bottom + gap, 0, 0)),
            DockEdge.Left => new(PlacementMode.Right, new Rect(dock.Right + gap, a, 0, 0)),
            DockEdge.Right => new(PlacementMode.Left, new Rect(dock.Left - gap, a, 0, 0)),
            _ => new(PlacementMode.Top, new Rect(a, dock.Top - gap, 0, 0)),
        };
    }

    /// <summary>Dock penceresinin içeriğinin, hedefin koordinat sistemindeki sınırları.</summary>
    private static Rect DockBounds(FrameworkElement target)
    {
        if (Window.GetWindow(target)?.Content is FrameworkElement root && root.IsAncestorOf(target))
        {
            var transform = root.TransformToDescendant(target);
            if (transform is not null)
                return transform.TransformBounds(new Rect(0, 0, root.ActualWidth, root.ActualHeight));
        }
        return new Rect(0, 0, target.ActualWidth, target.ActualHeight);
    }

    /// <summary>Bağlam menüsünü imlecin hizasında, dock'un dışına yerleştirir.</summary>
    public static void PlaceMenu(ContextMenu menu, FrameworkElement owner, DockEdge edge)
    {
        var pointer = System.Windows.Input.Mouse.GetPosition(owner);
        bool vertical = edge is DockEdge.Left or DockEdge.Right;
        // Menü şablonunun gölge için bıraktığı 6 DIP'lik dış boşluk
        const double shadowMargin = 6;
        var result = Compute(owner, edge, 2 - shadowMargin, (vertical ? pointer.Y : pointer.X) - shadowMargin);
        menu.PlacementTarget = owner;
        menu.Placement = result.Mode;
        menu.PlacementRectangle = result.Rectangle;
        menu.HorizontalOffset = 0;
        menu.VerticalOffset = 0;
    }

    /// <summary>Popup'ı hedefe ortalanmış olarak dock'un dışına yerleştirir.</summary>
    public static void PlacePopup(Popup popup, FrameworkElement target, DockEdge edge, double gap = 6)
    {
        bool vertical = edge is DockEdge.Left or DockEdge.Right;
        double along = 0;
        if (popup.Child is FrameworkElement child)
        {
            child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            along = vertical
                ? (target.ActualHeight - child.DesiredSize.Height) / 2
                : (target.ActualWidth - child.DesiredSize.Width) / 2;
        }

        var result = Compute(target, edge, gap, along);
        popup.PlacementTarget = target;
        popup.Placement = result.Mode;
        popup.PlacementRectangle = result.Rectangle;
        popup.HorizontalOffset = 0;
        popup.VerticalOffset = 0;
    }

    /// <summary>Popup açılırken dock'tan dışarı doğru kayma yönü.</summary>
    public static Vector EnterOffset(DockEdge edge) => edge switch
    {
        DockEdge.Top => new Vector(0, -8),
        DockEdge.Left => new Vector(-8, 0),
        DockEdge.Right => new Vector(8, 0),
        _ => new Vector(0, 8),
    };

    /// <summary>Olay kaynağından yukarı doğru, bağlam menüsü tanımlı ilk öğeyi bulur.</summary>
    public static FrameworkElement? FindMenuOwner(DependencyObject? source)
    {
        for (var d = source; d is not null; d = d is Visual or System.Windows.Media.Media3D.Visual3D
                 ? VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d)
                 : LogicalTreeHelper.GetParent(d))
        {
            if (d is FrameworkElement { ContextMenu: not null } fe)
                return fe;
        }
        return null;
    }
}
