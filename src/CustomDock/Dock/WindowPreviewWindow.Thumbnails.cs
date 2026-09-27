using System.Windows;
using System.Windows.Interop;
using CustomDock.Native;
using static CustomDock.Native.NativeMethods;

namespace CustomDock.Dock;

public sealed partial class WindowPreviewWindow
{
    private void RegisterThumbnails()
    {
        if (_hwnd == IntPtr.Zero)
            _hwnd = new WindowInteropHelper(this).EnsureHandle();

        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget is null) return;

        double scale = source.CompositionTarget.TransformToDevice.M11;

        foreach (var (host, window) in _previewItems)
        {
            if (window.Handle == IntPtr.Zero) continue;

            try
            {
                var hostScreen = host.PointToScreen(new Point(0, 0));
                var pt = new POINT { X = (int)hostScreen.X, Y = (int)hostScreen.Y };
                ScreenToClient(_hwnd, ref pt);

                int hostW = (int)Math.Round(host.ActualWidth * scale);
                int hostH = (int)Math.Round(host.ActualHeight * scale);

                if (hostW <= 0 || hostH <= 0) continue;

                int hr = DwmRegisterThumbnail(_hwnd, window.Handle, out IntPtr hThumb);
                if (hr == 0 && hThumb != IntPtr.Zero)
                {
                    _thumbnails.Add(hThumb);

                    // Oran koruma (Aspect ratio fitting)
                    int destW = hostW;
                    int destH = hostH;

                    if (DwmQueryThumbnailSourceSize(hThumb, out PSIZE srcSize) == 0 && srcSize.x > 0 && srcSize.y > 0)
                    {
                        double aspect = (double)srcSize.x / srcSize.y;
                        if (aspect > (double)hostW / hostH)
                        {
                            destW = hostW;
                            destH = (int)Math.Round(hostW / aspect);
                        }
                        else
                        {
                            destH = hostH;
                            destW = (int)Math.Round(hostH * aspect);
                        }
                    }

                    int offsetX = pt.X + (hostW - destW) / 2;
                    int offsetY = pt.Y + (hostH - destH) / 2;

                    var props = new DWM_THUMBNAIL_PROPERTIES
                    {
                        dwFlags = DWM_TNP_RECTDESTINATION | DWM_TNP_VISIBLE | DWM_TNP_OPACITY,
                        rcDestination = new RECT(offsetX, offsetY, offsetX + destW, offsetY + destH),
                        fVisible = true,
                        opacity = 255,
                    };

                    DwmUpdateThumbnailProperties(hThumb, ref props);
                }
            }
            catch
            {
                // Continue if single window thumbnail cannot be acquired
            }
        }
    }

    private void UnregisterAllThumbnails()
    {
        foreach (var thumb in _thumbnails)
        {
            try
            {
                DwmUnregisterThumbnail(thumb);
            }
            catch
            {
                // Yoksay
            }
        }
        _thumbnails.Clear();
    }
}
