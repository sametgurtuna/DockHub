using System.Windows.Controls;
using System.Windows.Input;
using CustomDock.Core;
using CustomDock.Services;

namespace CustomDock.Dock;

/// <summary>
/// Volume glyph next to the tray. Click: quick settings, wheel: ±2 % (Shift: ±10 %), middle click: mute,
/// right click: output device and sound settings.
/// </summary>
public sealed class VolumeStatusIconView : StatusIconViewBase
{
    private static AudioService Audio => AppServices.Audio;

    protected override void Attach()
    {
        Audio.VolumeChanged += RefreshSoon;
        Audio.DevicesChanged += RefreshSoon;
    }

    protected override void Detach()
    {
        Audio.VolumeChanged -= RefreshSoon;
        Audio.DevicesChanged -= RefreshSoon;
    }

    protected override void Refresh()
    {
        bool muted = Audio.IsMuted;
        int percent = Audio.VolumePercent;
        bool hasDevice = Audio.DefaultDevice is not null;

        Glyph.Text = !hasDevice || muted ? ""
            : percent == 0 ? ""
            : percent <= 33 ? ""
            : percent <= 66 ? ""
            : "";
        Glyph.Opacity = hasDevice ? 1.0 : 0.45;
        ToolTip = !hasDevice ? L.T("No audio output device")
            : $"{Audio.DefaultDevice!.Name}: {(muted ? L.T("muted") : $"{percent}%")}";
    }

    protected override void OnClick()
    {
        try { AppServices.Shell?.ShowQuickSettings(); }
        catch (Exception ex) { Log.Error(ex, "Failed to open quick settings from volume icon"); }
    }

    protected override void OnMiddleClick() => Audio.ToggleMute();

    protected override bool OnWheel(int delta)
    {
        if (Audio.DefaultDevice is null) return false;
        float step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 0.10f : 0.02f;
        Audio.StepVolume(delta > 0 ? step : -step);
        Refresh();
        return true;
    }

    protected override void BuildMenu(ItemCollection items)
    {
        var devices = Audio.Devices;
        if (devices.Count > 0)
        {
            items.Add(DockMenu.Header("Output device"));
            foreach (var device in devices)
            {
                var id = device.Id;
                items.Add(DockMenu.Check(device.Name, device.IsDefault, () => Audio.SetDefaultDevice(id)));
            }
            items.Add(DockMenu.Separator());
        }
        items.Add(DockMenu.Check("Mute", Audio.IsMuted, Audio.ToggleMute));
        items.Add(DockMenu.Item("Volume mixer", "", () => NetworkStatusIconView.OpenSettings("ms-settings:apps-volume")));
        items.Add(DockMenu.Item("Sound settings", "", () => NetworkStatusIconView.OpenSettings("ms-settings:sound")));
    }
}
