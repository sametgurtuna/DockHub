using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using CustomDock.Core;
using CustomDock.Services;

namespace CustomDock.Dock;

/// <summary>
/// Microphone next to the tray. Click or middle click: mute or unmute; right click: recording device and privacy
/// settings. Shown while an app records (like Windows), always, or never.
/// </summary>
public sealed class MicrophoneStatusIconView : StatusIconViewBase
{
    private readonly Line _slash;
    private MicrophoneIconMode _mode = MicrophoneIconMode.Off;
    private bool _subscribed;

    public MicrophoneStatusIconView()
    {
        Visibility = Visibility.Collapsed;
        Glyph.Text = "";
        // A slash drawn over the glyph marks mute (independent of which icon font is installed).
        _slash = new Line
        {
            X1 = 0, Y1 = 0, X2 = 13, Y2 = 13,
            StrokeThickness = 1.6,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
        };
        _slash.SetResourceReference(Shape.StrokeProperty, "TextPrimaryBrush");
        ((Grid)Child).Children.Add(_slash);
    }

    private static MicrophoneService Mic => AppServices.Microphone;

    /// <summary>Set by the dock (Off on docks without a tray).</summary>
    public MicrophoneIconMode Mode
    {
        get => _mode;
        set
        {
            _mode = value;
            if (IsLoaded) SyncSubscription();
            Update();
        }
    }

    private void SyncSubscription()
    {
        if (_mode != MicrophoneIconMode.Off && !_subscribed)
        {
            Mic.EnsureStarted();
            Mic.Changed += Update;
            _subscribed = true;
        }
        else if (_mode == MicrophoneIconMode.Off && _subscribed)
        {
            Mic.Changed -= Update;
            _subscribed = false;
        }
    }

    protected override void Attach() => SyncSubscription();

    protected override void Detach()
    {
        if (!_subscribed) return;
        Mic.Changed -= Update;
        _subscribed = false;
    }

    protected override void Refresh()
    {
        bool hasDevice = _subscribed && Mic.DefaultDevice is not null;
        bool show = _mode switch
        {
            MicrophoneIconMode.Always => hasDevice,
            MicrophoneIconMode.WhenInUse => hasDevice && Mic.IsInUse,
            _ => false,
        };
        Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show) return;

        bool muted = Mic.IsMuted;
        _slash.Visibility = muted ? Visibility.Visible : Visibility.Collapsed;
        Glyph.Opacity = muted ? 0.7 : 1.0;
        string state = muted ? L.T("muted") : L.T("on");
        string tip = $"{Mic.DefaultDevice!.Name}: {state}";
        if (Mic.IsInUse) tip += "\n" + L.T("In use by {0}", string.Join(", ", Mic.AppsInUse));
        ToolTip = tip + "\n" + L.T("Click to mute or unmute");
    }

    protected override void OnClick() => Mic.ToggleMute();

    protected override void OnMiddleClick() => Mic.ToggleMute();

    protected override void BuildMenu(ItemCollection items)
    {
        var devices = Mic.Devices;
        if (devices.Count > 0)
        {
            items.Add(DockMenu.Header("Recording device"));
            foreach (var device in devices)
            {
                var id = device.Id;
                items.Add(DockMenu.Check(device.Name, device.IsDefault, () => Mic.SetDefaultDevice(id)));
            }
            items.Add(DockMenu.Separator());
        }
        items.Add(DockMenu.Check("Mute microphone", Mic.IsMuted, Mic.ToggleMute));
        items.Add(DockMenu.Item("Sound settings", "", () => NetworkStatusIconView.OpenSettings("ms-settings:sound")));
        items.Add(DockMenu.Item("Microphone privacy settings", "", () => NetworkStatusIconView.OpenSettings("ms-settings:privacy-microphone")));
    }
}
