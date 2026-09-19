using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace CustomDock.Dock;

/// <summary>Common drag initiator for reordering dock items via drag and drop.</summary>
public static class DockDragHelper
{
    public const string ItemFormat = "DockHub.ItemId";
    public const string RunningAppFormat = "DockHub.AppKey";

    private static FrameworkElement? _source;
    private static Point _start;

    /// <summary>True immediately after drag ends; click handlers use this to suppress click.</summary>
    public static bool JustDragged { get; private set; }

    public static event Action<bool>? DraggingChanged;

    public static void Attach(FrameworkElement element, Func<DataObject?> dataFactory)
    {
        element.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (IsInsideInteractive(e.OriginalSource as DependencyObject, element))
            {
                _source = null;
                return;
            }
            _source = element;
            _start = e.GetPosition(element);
        };

        element.PreviewMouseLeftButtonUp += (_, _) => _source = null;

        element.PreviewMouseMove += (_, e) =>
        {
            if (!ReferenceEquals(_source, element) || e.LeftButton != MouseButtonState.Pressed) return;
            var delta = e.GetPosition(element) - _start;
            if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance * 2.5 &&
                Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance * 2.5)
                return;

            _source = null;
            var data = dataFactory();
            if (data is null) return;

            JustDragged = true;
            DraggingChanged?.Invoke(true);
            try
            {
                DragDrop.DoDragDrop(element, data, DragDropEffects.Move);
            }
            finally
            {
                DraggingChanged?.Invoke(false);
                element.Dispatcher.BeginInvoke(DispatcherPriority.Input, () => JustDragged = false);
            }
        };
    }

    private static bool IsInsideInteractive(DependencyObject? source, FrameworkElement root)
    {
        for (var d = source; d is not null && !ReferenceEquals(d, root); d = VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d))
        {
            if (d is TextBoxBase or Slider or ScrollBar) return true;
        }
        return false;
    }
}
