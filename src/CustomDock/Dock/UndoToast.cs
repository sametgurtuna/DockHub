using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using CustomDock.Controls;
using CustomDock.Core;
using CustomDock.Native;

namespace CustomDock.Dock;

/// <summary>"Weather widget removed · Undo" bubble next to the dock after destructive changes.</summary>
public sealed class UndoToast : Window
{
    private static readonly TimeSpan VisibleFor = TimeSpan.FromSeconds(6);
    private static UndoToast? s_instance;

    private readonly TextBlock _message;
    private readonly DispatcherTimer _timer;

    private UndoToast()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        Left = -32000;
        Top = -32000;

        _message = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontSize = 12.5, MaxWidth = 360, TextTrimming = TextTrimming.CharacterEllipsis };
        _message.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");

        var undo = new Button
        {
            Content = "Undo",
            Margin = new Thickness(14, 0, 0, 0),
            Padding = new Thickness(12, 4, 12, 4),
            Cursor = Cursors.Hand,
            FontWeight = FontWeights.SemiBold,
            BorderThickness = new Thickness(0),
            Foreground = Brushes.White,
        };
        undo.SetResourceReference(BackgroundProperty, "AccentBlueBrush");
        Ui.SetCornerRadius(undo, new CornerRadius(5));
        undo.Click += (_, _) =>
        {
            HideToast();
            AppServices.ConfigService.Undo();
        };

        var close = new Button
        {
            Content = "",
            FontSize = 10,
            Width = 26,
            Height = 26,
            Margin = new Thickness(6, 0, 0, 0),
            ToolTip = "Dismiss",
            Style = Application.Current.TryFindResource("DockButton") as Style,
        };
        close.SetResourceReference(FontFamilyProperty, "IconFont");
        close.Click += (_, _) => HideToast();

        var border = new Border
        {
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 8, 8, 8),
            Margin = new Thickness(10),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { _message, undo, close } },
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 16, ShadowDepth = 2, Opacity = 0.3 },
        };
        border.SetResourceReference(Border.BackgroundProperty, "PopupBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "PopupBorderBrush");
        Content = border;

        _timer = new DispatcherTimer { Interval = VisibleFor };
        _timer.Tick += (_, _) => HideToast();
        MouseEnter += (_, _) => _timer.Stop();
        MouseLeave += (_, _) => { _timer.Stop(); _timer.Start(); };

        SourceInitialized += (_, _) => WindowEffects.MakeToolWindow(new WindowInteropHelper(this).Handle, noActivate: true);
    }

    /// <summary>Shows the toast for destructive history entries.</summary>
    public static void Attach(ConfigHistory history)
    {
        history.Changed += (entry, undone) =>
        {
            if (undone) { s_instance?.HideToast(); return; }
            if (!entry.Destructive) return;
            s_instance ??= new UndoToast();
            s_instance.ShowFor(entry.Description);
        };
    }

    private void ShowFor(string description)
    {
        _message.Text = description;
        if (!IsVisible) Show();
        UpdateLayout();
        PositionNearDock();
        Motion.Appear((FrameworkElement)Content, fromScale: 0.95, milliseconds: 180);
        _timer.Stop();
        _timer.Start();
    }

    private void PositionNearDock()
    {
        var dock = DockWindow.All.FirstOrDefault(d => d.IsMain);
        if (dock is null || !dock.IsVisible) return;
        var edge = AppServices.Config.Edge;
        double width = ActualWidth, height = ActualHeight;
        (Left, Top) = edge switch
        {
            DockEdge.Top => (dock.Left + (dock.ActualWidth - width) / 2, dock.Top + dock.ActualHeight),
            DockEdge.Left => (dock.Left + dock.ActualWidth, dock.Top + (dock.ActualHeight - height) / 2),
            DockEdge.Right => (dock.Left - width, dock.Top + (dock.ActualHeight - height) / 2),
            _ => (dock.Left + (dock.ActualWidth - width) / 2, dock.Top - height),
        };
    }

    private void HideToast()
    {
        _timer.Stop();
        Hide();
    }
}
