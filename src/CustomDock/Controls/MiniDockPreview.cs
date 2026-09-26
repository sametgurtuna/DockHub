using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using CustomDock.Core;

namespace CustomDock.Controls;

/// <summary>
/// A small picture of the desktop with the dock drawn on it, updated live as appearance settings change
/// (edge, size, shape, width, alignment, backdrop and tint). Drawn with the same theme brushes as the dock.
/// </summary>
public sealed class MiniDockPreview : Border
{
    private const double ScreenAspect = 16.0 / 9.0;
    private readonly Canvas _canvas = new() { ClipToBounds = true };
    private readonly Image _wallpaper = new() { Stretch = Stretch.UniformToFill };
    private AppConfig? _config;

    public MiniDockPreview()
    {
        CornerRadius = new CornerRadius(8);
        ClipToBounds = true;
        BorderThickness = new Thickness(1);
        SetResourceReference(BorderBrushProperty, "SurfaceBorderBrush");
        Height = 190;
        Child = new Grid { Children = { _wallpaper, _canvas } };
        _wallpaper.Source = LoadWallpaper();
        if (_wallpaper.Source is null)
        {
            Background = new LinearGradientBrush(Color.FromRgb(0x0B, 0x1D, 0x4D), Color.FromRgb(0x1D, 0x6F, 0xFF), 35);
        }
        else
        {
            _wallpaper.Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 2 };
        }
        SizeChanged += (_, _) => Redraw();
        ThemeManager.ThemeChanged += Redraw;
        Unloaded += (_, _) => { ThemeManager.ThemeChanged -= Redraw; if (_config is not null) _config.PropertyChanged -= OnConfigChanged; };
    }

    public void Bind(AppConfig config)
    {
        _config = config;
        config.PropertyChanged += OnConfigChanged;
        Redraw();
    }

    private void OnConfigChanged(object? sender, PropertyChangedEventArgs e) => Dispatcher.BeginInvoke(Redraw);

    private void Redraw()
    {
        _canvas.Children.Clear();
        if (_config is not { } c || ActualWidth <= 0) return;

        double width = ActualWidth, height = ActualHeight;
        // The preview is a scaled 16:9 screen; one "DIP" of the real dock is this many preview pixels.
        double unit = width / 960.0;
        bool vertical = c.Edge is DockEdge.Left or DockEdge.Right;
        bool floating = c.Layout == DockLayout.Floating;
        double thickness = c.Size switch { DockSize.Small => 48, DockSize.Medium => 56, _ => 66 } * unit;
        double margin = floating ? Math.Max(2, c.EdgeMargin * unit * 1.5) : 0;

        // Content: start button, 5 apps, 2 widgets, clock.
        double button = thickness * 0.72;
        double content = button * 6 + button * 2.6 * 2 + button * 1.6 + thickness * 0.4;
        double available = (vertical ? height : width) - margin * 2;
        double length = c.WidthMode == DockWidthMode.Full ? available : Math.Min(available, content);

        double along = c.WidthMode == DockWidthMode.Full || c.Alignment == DockAlignment.Center
            ? ((vertical ? height : width) - length) / 2
            : margin;
        (double x, double y, double w, double h) = c.Edge switch
        {
            DockEdge.Top => (along, margin, length, thickness),
            DockEdge.Left => (margin, along, thickness, length),
            DockEdge.Right => (width - margin - thickness, along, thickness, length),
            _ => (along, height - margin - thickness, length, thickness),
        };

        var tint = (TryFindResource(c.Backdrop == BackdropKind.Solid ? "DockSolidBrush" : "DockTintBrush") as SolidColorBrush)?.Color ?? Color.FromRgb(20, 20, 24);
        byte alpha = c.Backdrop == BackdropKind.Solid ? (byte)245 : (byte)Math.Clamp(90 + c.TintOpacity * 150, 0, 255);
        var bar = new Border
        {
            Width = w,
            Height = h,
            CornerRadius = new CornerRadius(floating ? thickness * 0.28 : 0),
            Background = new SolidColorBrush(Color.FromArgb(alpha, tint.R, tint.G, tint.B)),
            BorderThickness = new Thickness(floating ? 0.8 : 0),
            BorderBrush = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)),
        };
        if (c.Backdrop != BackdropKind.Solid)
            bar.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 10, ShadowDepth = 1, Opacity = 0.35 };
        Canvas.SetLeft(bar, x);
        Canvas.SetTop(bar, y);
        _canvas.Children.Add(bar);

        // Items along the bar.
        var items = new StackPanel
        {
            Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        string[] appColors = { "AccentBlueBrush", "AccentYellowBrush", "AccentGreenBrush", "AccentPurpleBrush", "AccentRedBrush" };
        items.Children.Add(Dot(button, "TextPrimaryBrush", 0.9));
        foreach (var color in appColors) items.Children.Add(Dot(button, color, 1));
        foreach (var _ in new[] { 0, 1 })
            items.Children.Add(Card(vertical ? button : button * 2.6, vertical ? button : button * 0.8));
        bar.Child = new Grid
        {
            Children =
            {
                new Viewbox
                {
                    Stretch = Stretch.Uniform,
                    StretchDirection = StretchDirection.DownOnly,
                    Margin = new Thickness(thickness * 0.12),
                    Child = items,
                    HorizontalAlignment = c.Alignment == DockAlignment.Start && !vertical ? HorizontalAlignment.Left : HorizontalAlignment.Center,
                    VerticalAlignment = c.Alignment == DockAlignment.Start && vertical ? VerticalAlignment.Top : VerticalAlignment.Center,
                },
            },
        };
    }

    private FrameworkElement Dot(double size, string brushKey, double opacity)
    {
        var tile = new Rectangle
        {
            Width = size * 0.72,
            Height = size * 0.72,
            RadiusX = size * 0.2,
            RadiusY = size * 0.2,
            Margin = new Thickness(size * 0.14),
            Opacity = opacity,
        };
        tile.SetResourceReference(Shape.FillProperty, brushKey);
        return tile;
    }

    private FrameworkElement Card(double width, double height)
    {
        var card = new Rectangle
        {
            Width = width,
            Height = height,
            RadiusX = height * 0.25,
            RadiusY = height * 0.25,
            Margin = new Thickness(height * 0.12),
        };
        card.SetResourceReference(Shape.FillProperty, "CardBrush");
        return card;
    }

    private static ImageSource? LoadWallpaper()
    {
        try
        {
            var sb = new StringBuilder(520);
            if (!SystemParametersInfo(0x0073 /* SPI_GETDESKWALLPAPER */, (uint)sb.Capacity, sb, 0)) return null;
            string path = sb.ToString();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(path);
            image.DecodePixelWidth = 640;
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SystemParametersInfo(uint action, uint param, StringBuilder value, uint winIni);
}
