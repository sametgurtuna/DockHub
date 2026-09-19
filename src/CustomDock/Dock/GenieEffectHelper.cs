using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using CustomDock.Core;
using CustomDock.Native;
using static CustomDock.Native.NativeMethods;

namespace CustomDock.Dock;

/// <summary>
/// Implements the authentic macOS Genie Effect (funnel/lamp warp) for opening and closing popups
/// (folders, widgets, compact flyouts, tray) using hardware-accelerated 3D mesh deformation.
/// </summary>
public static class GenieEffectHelper
{
    private static readonly HashSet<Popup> s_closingPopups = new();

    public static bool IsClosing(Popup popup)
    {
        if (popup is null) return false;
        lock (s_closingPopups)
        {
            return s_closingPopups.Contains(popup);
        }
    }

    public static GeniePopupHost EnsureGenieHost(Popup popup)
    {
        if (popup.Child is GeniePopupHost existing)
            return existing;

        var original = popup.Child as FrameworkElement;
        popup.Child = null; // Detach
        var host = new GeniePopupHost(original);
        popup.Child = host;
        return host;
    }

    /// <summary>Opens the popup with the macOS Genie Effect emerging from the anchor element.</summary>
    public static void AnimateOpen(Popup popup, DockEdge edge, FrameworkElement? anchor = null, Action? onOpened = null)
    {
        if (popup is null) return;
        lock (s_closingPopups)
        {
            s_closingPopups.Remove(popup);
        }

        popup.AllowsTransparency = true;
        popup.PopupAnimation = PopupAnimation.None;
        popup.StaysOpen = true;

        var host = EnsureGenieHost(popup);

        // Open popup first so its HWND is created and in the visual tree
        popup.IsOpen = true;

        host.StartOpen(edge, anchor, onOpened);
    }

    /// <summary>Closes the popup with the macOS Genie Effect sucking into the anchor element.</summary>
    public static void ClosePopup(Popup popup, DockEdge edge, FrameworkElement? anchor = null, Action? onClosed = null)
    {
        if (popup is null || !popup.IsOpen)
        {
            onClosed?.Invoke();
            return;
        }

        lock (s_closingPopups)
        {
            if (!s_closingPopups.Add(popup))
                return; // Already closing
        }

        var host = EnsureGenieHost(popup);

        host.StartClose(edge, anchor, () =>
        {
            lock (s_closingPopups)
            {
                s_closingPopups.Remove(popup);
            }

            popup.IsOpen = false;
            onClosed?.Invoke();
        });
    }
}

/// <summary>
/// Host grid containing the interactive UI element and the 3D Genie mesh overlay.
/// </summary>
public sealed class GeniePopupHost : Grid
{
    private const int Columns = 20;
    private const int Rows = 28;

    private readonly FrameworkElement? _content;
    private readonly Viewport3D _viewport;
    private readonly MeshGeometry3D _mesh;
    private readonly GeometryModel3D _model;
    private readonly OrthographicCamera _camera;
    private EventHandler? _activeRenderingHandler;
    private bool _isAnimating;

    public FrameworkElement? InnerContent => _content;
    public bool IsAnimating => _isAnimating;

    public GeniePopupHost(FrameworkElement? content)
    {
        _content = content;
        if (_content is not null)
            Children.Add(_content);

        _mesh = CreateBaseMesh();
        _model = new GeometryModel3D { Geometry = _mesh };

        _camera = new OrthographicCamera
        {
            LookDirection = new Vector3D(0, 0, 1),
            UpDirection = new Vector3D(0, -1, 0),
        };

        _viewport = new Viewport3D
        {
            Camera = _camera,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
        };
        _viewport.Children.Add(new ModelVisual3D { Content = new AmbientLight(Colors.White) });
        _viewport.Children.Add(new ModelVisual3D { Content = _model });

        Children.Add(_viewport);
    }

    private static MeshGeometry3D CreateBaseMesh()
    {
        var mesh = new MeshGeometry3D();

        for (int r = 0; r <= Rows; r++)
        {
            double v = (double)r / Rows;
            for (int c = 0; c <= Columns; c++)
            {
                double u = (double)c / Columns;
                mesh.TextureCoordinates.Add(new Point(u, v));
            }
        }

        for (int r = 0; r < Rows; r++)
        {
            for (int c = 0; c < Columns; c++)
            {
                int p0 = r * (Columns + 1) + c;
                int p1 = p0 + 1;
                int p2 = (r + 1) * (Columns + 1) + c;
                int p3 = p2 + 1;

                mesh.TriangleIndices.Add(p0);
                mesh.TriangleIndices.Add(p2);
                mesh.TriangleIndices.Add(p1);

                mesh.TriangleIndices.Add(p1);
                mesh.TriangleIndices.Add(p2);
                mesh.TriangleIndices.Add(p3);
            }
        }

        return mesh;
    }

    public void StartOpen(DockEdge edge, FrameworkElement? anchor, Action? onCompleted = null)
    {
        if (_content is null)
        {
            onCompleted?.Invoke();
            return;
        }

        StopActiveAnimation();

        _content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double w = _content.DesiredSize.Width > 20 ? _content.DesiredSize.Width : (ActualWidth > 20 ? ActualWidth : 260);
        double h = _content.DesiredSize.Height > 20 ? _content.DesiredSize.Height : (ActualHeight > 20 ? ActualHeight : 200);

        _content.Arrange(new Rect(0, 0, w, h));
        _content.UpdateLayout();

        var (anchorX, anchorY) = GetAnchorCoordinates(this, anchor, w, h, edge);

        var rtb = CreateSnapshot(_content, w, h);
        var brush = new ImageBrush(rtb) { Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.HighQuality);

        var mat = new MaterialGroup();
        mat.Children.Add(new DiffuseMaterial(brush));
        mat.Children.Add(new EmissiveMaterial(brush));
        _model.Material = mat;
        _model.BackMaterial = mat;

        _camera.Position = new Point3D(w / 2.0, h / 2.0, -1000);
        _camera.Width = w;
        _viewport.Width = w;
        _viewport.Height = h;

        _content.Opacity = 0.0;
        _viewport.Visibility = Visibility.Visible;
        _isAnimating = true;

        var sw = Stopwatch.StartNew();
        const double durationMs = 320.0;

        void OnRendering(object? sender, EventArgs e)
        {
            double elapsed = sw.Elapsed.TotalMilliseconds;
            double p = Math.Clamp(elapsed / durationMs, 0.0, 1.0);
            double t = EaseOutCubic(p);

            UpdateMeshVertices(t, w, h, anchorX, anchorY, edge);
            _viewport.Opacity = Math.Clamp(p / 0.1, 0.0, 1.0);

            if (p >= 1.0)
            {
                StopActiveAnimation();
                _isAnimating = false;
                _content.Opacity = 1.0;
                _viewport.Visibility = Visibility.Collapsed;
                onCompleted?.Invoke();
            }
        }

        _activeRenderingHandler = OnRendering;
        CompositionTarget.Rendering += _activeRenderingHandler;
    }

    public void StartClose(DockEdge edge, FrameworkElement? anchor, Action? onCompleted = null)
    {
        if (_content is null)
        {
            onCompleted?.Invoke();
            return;
        }

        StopActiveAnimation();

        double w = ActualWidth > 20 ? ActualWidth : (_content.DesiredSize.Width > 20 ? _content.DesiredSize.Width : 260);
        double h = ActualHeight > 20 ? ActualHeight : (_content.DesiredSize.Height > 20 ? _content.DesiredSize.Height : 200);

        _content.Arrange(new Rect(0, 0, w, h));
        _content.UpdateLayout();

        var (anchorX, anchorY) = GetAnchorCoordinates(this, anchor, w, h, edge);

        var rtb = CreateSnapshot(_content, w, h);
        var brush = new ImageBrush(rtb) { Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.HighQuality);

        var mat = new MaterialGroup();
        mat.Children.Add(new DiffuseMaterial(brush));
        mat.Children.Add(new EmissiveMaterial(brush));
        _model.Material = mat;
        _model.BackMaterial = mat;

        _camera.Position = new Point3D(w / 2.0, h / 2.0, -1000);
        _camera.Width = w;
        _viewport.Width = w;
        _viewport.Height = h;

        _content.Opacity = 0.0;
        _viewport.Visibility = Visibility.Visible;
        _isAnimating = true;

        var sw = Stopwatch.StartNew();
        const double durationMs = 280.0;

        void OnRendering(object? sender, EventArgs e)
        {
            double elapsed = sw.Elapsed.TotalMilliseconds;
            double p = Math.Clamp(elapsed / durationMs, 0.0, 1.0);
            double t = 1.0 - EaseInCubic(p);

            UpdateMeshVertices(t, w, h, anchorX, anchorY, edge);
            _viewport.Opacity = Math.Clamp((1.0 - p) / 0.12, 0.0, 1.0);

            if (p >= 1.0)
            {
                StopActiveAnimation();
                _isAnimating = false;
                _content.Opacity = 1.0;
                _viewport.Visibility = Visibility.Collapsed;
                onCompleted?.Invoke();
            }
        }

        _activeRenderingHandler = OnRendering;
        CompositionTarget.Rendering += _activeRenderingHandler;
    }

    private void StopActiveAnimation()
    {
        if (_activeRenderingHandler is not null)
        {
            CompositionTarget.Rendering -= _activeRenderingHandler;
            _activeRenderingHandler = null;
        }
    }

    private static (double x, double y) GetAnchorCoordinates(GeniePopupHost host, FrameworkElement? anchor, double w, double h, DockEdge edge)
    {
        if (anchor is not null)
        {
            try
            {
                var anchorCenterDevice = anchor.PointToScreen(new Point(anchor.ActualWidth / 2.0, anchor.ActualHeight / 2.0));

                if (PresentationSource.FromVisual(host) is HwndSource hostSource && hostSource.Handle != IntPtr.Zero)
                {
                    if (GetWindowRect(hostSource.Handle, out RECT popupRectDevice))
                    {
                        var dpi = VisualTreeHelper.GetDpi(host);
                        double scaleX = dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1.0;
                        double scaleY = dpi.DpiScaleY > 0 ? dpi.DpiScaleY : 1.0;

                        double rx = (anchorCenterDevice.X - popupRectDevice.Left) / scaleX;
                        double ry = (anchorCenterDevice.Y - popupRectDevice.Top) / scaleY;

                        rx = Math.Clamp(rx, -w * 0.5, w * 1.5);
                        ry = Math.Clamp(ry, -h * 0.5, h * 1.5);
                        return (rx, ry);
                    }
                }

                var local = host.PointFromScreen(anchorCenterDevice);
                double lx = Math.Clamp(local.X, -w * 0.5, w * 1.5);
                double ly = Math.Clamp(local.Y, -h * 0.5, h * 1.5);
                return (lx, ly);
            }
            catch
            {
                // Fallback
            }
        }

        return edge switch
        {
            DockEdge.Bottom => (w / 2.0, h),
            DockEdge.Top => (w / 2.0, 0),
            DockEdge.Left => (0, h / 2.0),
            DockEdge.Right => (w, h / 2.0),
            _ => (w / 2.0, h),
        };
    }

    private static RenderTargetBitmap CreateSnapshot(FrameworkElement target, double w, double h)
    {
        double oldOpacity = target.Opacity;
        Visibility oldVis = target.Visibility;

        try
        {
            target.Opacity = 1.0;
            target.Visibility = Visibility.Visible;

            double dpiX = 96.0;
            double dpiY = 96.0;
            try
            {
                var dpi = VisualTreeHelper.GetDpi(target);
                dpiX = dpi.PixelsPerInchX > 0 ? dpi.PixelsPerInchX : 96.0;
                dpiY = dpi.PixelsPerInchY > 0 ? dpi.PixelsPerInchY : 96.0;
            }
            catch { }

            int pixelW = (int)Math.Max(1, Math.Ceiling(w * dpiX / 96.0));
            int pixelH = (int)Math.Max(1, Math.Ceiling(h * dpiY / 96.0));
            var rtb = new RenderTargetBitmap(pixelW, pixelH, dpiX, dpiY, PixelFormats.Pbgra32);

            target.Measure(new Size(w, h));
            target.Arrange(new Rect(0, 0, w, h));
            target.UpdateLayout();

            rtb.Render(target);
            return rtb;
        }
        finally
        {
            target.Opacity = oldOpacity;
            target.Visibility = oldVis;
        }
    }

    private void UpdateMeshVertices(double t, double w, double h, double anchorX, double anchorY, DockEdge edge)
    {
        var positions = new Point3DCollection((Rows + 1) * (Columns + 1));
        const double iconSize = 40.0;

        if (edge == DockEdge.Bottom)
        {
            // Popup sits ABOVE dock. Bottom (v=1) is anchored at dock icon (anchorX, h)
            double topProgress = Math.Pow(t, 0.72);
            double topY = (1.0 - topProgress) * h;
            double botY = h;

            double topW = iconSize + (w - iconSize) * Math.Pow(t, 0.45);
            double botW = iconSize + (w - iconSize) * Math.Pow(t, 3.2);

            for (int r = 0; r <= Rows; r++)
            {
                double v = (double)r / Rows;
                double baseY = topY + v * (botY - topY);

                double curve = Math.Pow(v, 2.2);
                double sliceW = topW * (1.0 - curve) + botW * curve;
                double sliceCenter = (w / 2.0) * (1.0 - curve) + (anchorX + (w / 2.0 - anchorX) * Math.Pow(t, 2.5)) * curve;

                for (int c = 0; c <= Columns; c++)
                {
                    double u = (double)c / Columns;
                    double x = sliceCenter + (u - 0.5) * sliceW;

                    // Classic macOS Genie curved arch & asymmetrical suction towards anchor
                    double uCenter = Math.Sin(u * Math.PI);
                    double archSag = uCenter * (1.0 - t) * Math.Sin(v * Math.PI) * (h * 0.12);

                    double distFromAnchor = Math.Abs(x - anchorX) / Math.Max(1.0, w);
                    double skewPull = (1.0 - Math.Pow(t, 0.8)) * (1.0 - Math.Clamp(distFromAnchor, 0.0, 1.0)) * (1.0 - v) * (h * 0.08);

                    double y = baseY + archSag + skewPull;
                    positions.Add(new Point3D(x, y, 0));
                }
            }
        }
        else if (edge == DockEdge.Top)
        {
            // Popup sits BELOW dock. Top (v=0) is anchored at dock icon (anchorX, 0)
            double botProgress = Math.Pow(t, 0.72);
            double topY = 0;
            double botY = botProgress * h;

            double topW = iconSize + (w - iconSize) * Math.Pow(t, 3.2);
            double botW = iconSize + (w - iconSize) * Math.Pow(t, 0.45);

            for (int r = 0; r <= Rows; r++)
            {
                double v = (double)r / Rows;
                double baseY = topY + v * (botY - topY);

                double curve = Math.Pow(1.0 - v, 2.2);
                double sliceW = botW * (1.0 - curve) + topW * curve;
                double sliceCenter = (w / 2.0) * (1.0 - curve) + (anchorX + (w / 2.0 - anchorX) * Math.Pow(t, 2.5)) * curve;

                for (int c = 0; c <= Columns; c++)
                {
                    double u = (double)c / Columns;
                    double x = sliceCenter + (u - 0.5) * sliceW;

                    double uCenter = Math.Sin(u * Math.PI);
                    double archSag = uCenter * (1.0 - t) * Math.Sin((1.0 - v) * Math.PI) * (h * 0.12);

                    double distFromAnchor = Math.Abs(x - anchorX) / Math.Max(1.0, w);
                    double skewPull = (1.0 - Math.Pow(t, 0.8)) * (1.0 - Math.Clamp(distFromAnchor, 0.0, 1.0)) * v * (h * 0.08);

                    double y = baseY - archSag - skewPull;
                    positions.Add(new Point3D(x, y, 0));
                }
            }
        }
        else if (edge == DockEdge.Left)
        {
            // Popup sits RIGHT of dock. Left (u=0) is anchored at dock icon (0, anchorY)
            double rightProgress = Math.Pow(t, 0.72);
            double leftX = 0;
            double rightX = rightProgress * w;

            double leftH = iconSize + (h - iconSize) * Math.Pow(t, 3.2);
            double rightH = iconSize + (h - iconSize) * Math.Pow(t, 0.45);

            for (int r = 0; r <= Rows; r++)
            {
                double v = (double)r / Rows;
                for (int c = 0; c <= Columns; c++)
                {
                    double u = (double)c / Columns;
                    double baseX = leftX + u * (rightX - leftX);

                    double curve = Math.Pow(1.0 - u, 2.2);
                    double sliceH = rightH * (1.0 - curve) + leftH * curve;
                    double sliceCenter = (h / 2.0) * (1.0 - curve) + (anchorY + (h / 2.0 - anchorY) * Math.Pow(t, 2.5)) * curve;

                    double y = sliceCenter + (v - 0.5) * sliceH;

                    double vCenter = Math.Sin(v * Math.PI);
                    double archSag = vCenter * (1.0 - t) * Math.Sin((1.0 - u) * Math.PI) * (w * 0.12);

                    double distFromAnchor = Math.Abs(y - anchorY) / Math.Max(1.0, h);
                    double skewPull = (1.0 - Math.Pow(t, 0.8)) * (1.0 - Math.Clamp(distFromAnchor, 0.0, 1.0)) * u * (w * 0.08);

                    double x = baseX - archSag - skewPull;
                    positions.Add(new Point3D(x, y, 0));
                }
            }
        }
        else // DockEdge.Right
        {
            // Popup sits LEFT of dock. Right (u=1) is anchored at dock icon (w, anchorY)
            double leftProgress = Math.Pow(t, 0.72);
            double leftX = (1.0 - leftProgress) * w;
            double rightX = w;

            double leftH = iconSize + (h - iconSize) * Math.Pow(t, 0.45);
            double rightH = iconSize + (h - iconSize) * Math.Pow(t, 3.2);

            for (int r = 0; r <= Rows; r++)
            {
                double v = (double)r / Rows;
                for (int c = 0; c <= Columns; c++)
                {
                    double u = (double)c / Columns;
                    double baseX = leftX + u * (rightX - leftX);

                    double curve = Math.Pow(u, 2.2);
                    double sliceH = leftH * (1.0 - curve) + rightH * curve;
                    double sliceCenter = (h / 2.0) * (1.0 - curve) + (anchorY + (h / 2.0 - anchorY) * Math.Pow(t, 2.5)) * curve;

                    double y = sliceCenter + (v - 0.5) * sliceH;

                    double vCenter = Math.Sin(v * Math.PI);
                    double archSag = vCenter * (1.0 - t) * Math.Sin(u * Math.PI) * (w * 0.12);

                    double distFromAnchor = Math.Abs(y - anchorY) / Math.Max(1.0, h);
                    double skewPull = (1.0 - Math.Pow(t, 0.8)) * (1.0 - Math.Clamp(distFromAnchor, 0.0, 1.0)) * (1.0 - u) * (w * 0.08);

                    double x = baseX + archSag + skewPull;
                    positions.Add(new Point3D(x, y, 0));
                }
            }
        }

        _mesh.Positions = positions;
    }

    private static double EaseOutCubic(double x) => 1.0 - Math.Pow(1.0 - x, 3.0);
    private static double EaseInCubic(double x) => Math.Pow(x, 3.0);
}
