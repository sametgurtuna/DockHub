using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ManagedShell.Common.Helpers;
using TrayIcon = ManagedShell.WindowsTray.NotifyIcon;

namespace CustomDock.Dock;

/// <summary>
/// A single system tray icon. Forwards mouse events to the owning application (context menu, double click, etc. belong to the app itself).
/// Behavior adapted from RetroBar (Apache-2.0).
/// </summary>
public sealed class TrayIconView : Border
{
    private readonly Image _image;

    public TrayIconView()
    {
        Width = 28;
        Height = 46;
        Background = Brushes.Transparent;
        Focusable = false;

        var hover = new Border { CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 9, 0, 9) };
        _image = new Image
        {
            Width = 17,
            Height = 17,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);

        var grid = new Grid();
        grid.Children.Add(hover);
        grid.Children.Add(_image);
        Child = grid;

        MouseEnter += (_, e) =>
        {
            hover.SetResourceReference(Border.BackgroundProperty, "DockHoverBrush");
            OnMouseEnterIcon(e);
        };
        MouseLeave += (_, e) =>
        {
            hover.Background = null;
            Icon?.IconMouseLeave(MouseHelper.GetCursorPositionParam());
            e.Handled = true;
        };
        MouseMove += (_, e) =>
        {
            Icon?.IconMouseMove(MouseHelper.GetCursorPositionParam());
            e.Handled = true;
        };
        MouseDown += OnMouseDownIcon;
        MouseUp += OnMouseUpIcon;
        DataContextChanged += (_, _) => Bind();
        // Right-click opens application's own menu; prevent dock menu from opening.
        ContextMenuOpening += (_, e) => e.Handled = true;
    }

    /// <summary>Called before clicking (so Start/app menus can position properly).</summary>
    public static event Action? Interacting;

    private TrayIcon? Icon => DataContext as TrayIcon;

    private void Bind()
    {
        if (Icon is not { } icon) return;
        _image.SetBinding(Image.SourceProperty, new System.Windows.Data.Binding(nameof(TrayIcon.Icon)) { Source = icon, Mode = System.Windows.Data.BindingMode.OneWay });
        SetBinding(ToolTipProperty, new System.Windows.Data.Binding(nameof(TrayIcon.Title)) { Source = icon, Mode = System.Windows.Data.BindingMode.OneWay });
    }

    private void OnMouseEnterIcon(MouseEventArgs e)
    {
        e.Handled = true;
        if (Icon is not { } icon || PresentationSource.FromVisual(this) is not { } source) return;

        // Icon location for Shell_NotifyIconGetRect
        var location = PointToScreen(new Point(0, 0));
        double scale = source.CompositionTarget.TransformToDevice.M11;
        int left = (int)Math.Round(location.X);
        int top = (int)Math.Round(location.Y);
        int width = (int)Math.Round(ActualWidth * scale);
        int height = (int)Math.Round(ActualHeight * scale);

        icon.Placement = new ManagedShell.Interop.NativeMethods.Rect
        {
            Left = left,
            Top = top,
            Right = left + width,
            Bottom = top + height,
        };
        icon.IconMouseEnter(MouseHelper.GetCursorPositionParam());
    }

    private void OnMouseDownIcon(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        Interacting?.Invoke();
        if (e.ChangedButton != MouseButton.Left)
            Icon?.IconMouseDown(e.ChangedButton, MouseHelper.GetCursorPositionParam(), System.Windows.Forms.SystemInformation.DoubleClickTime);
    }

    private void OnMouseUpIcon(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        int doubleClick = System.Windows.Forms.SystemInformation.DoubleClickTime;
        if (e.ChangedButton == MouseButton.Left)
            Icon?.IconMouseDown(e.ChangedButton, MouseHelper.GetCursorPositionParam(), doubleClick);
        Icon?.IconMouseUp(e.ChangedButton, MouseHelper.GetCursorPositionParam(), doubleClick);
    }
}
