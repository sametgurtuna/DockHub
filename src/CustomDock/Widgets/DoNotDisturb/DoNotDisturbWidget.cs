using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using CustomDock.Core;
using CustomDock.Dock;

namespace CustomDock.Widgets;

/// <summary>
/// Whether Do Not Disturb (or Focus) is on, and how many notifications wait. Windows has no public way for an app to
/// switch Do Not Disturb, so a click opens the notification center, whose bell switches it.
/// </summary>
public sealed class DoNotDisturbWidget : WidgetBase
{
    public const string Icon = "M20,14.5 A8.5,8.5 0 1 1 9.5,4 A7,7 0 0 0 20,14.5 Z";
    private const string MoonGlyph = "";

    private readonly StackPanel _iconLayout;
    private readonly StackPanel _statusLayout;
    private readonly List<(Border Disc, TextBlock Glyph)> _discs = new();
    private readonly TextBlock _state;

    public DoNotDisturbWidget()
    {
        Background = System.Windows.Media.Brushes.Transparent;
        _iconLayout = new StackPanel { Name = "Layout_icon", Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _iconLayout.Children.Add(Disc(34));

        _state = WidgetUi.Text("CaptionText");
        _statusLayout = new StackPanel
        {
            Name = "Layout_status",
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                Disc(30),
                new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8, 0, 0, 0),
                    Children = { WidgetUi.Text("TitleText", L.T("Do not disturb"), 12), _state },
                },
            },
        };
        Content = new Grid { Background = System.Windows.Media.Brushes.Transparent, Children = { _iconLayout, _statusLayout } };
        Cursor = System.Windows.Input.Cursors.Hand;
        MouseLeftButtonUp += (_, e) =>
        {
            if (DockDragHelper.JustDragged || IsPreview) return;
            e.Handled = true;
            AppServices.Shell.ShowNotificationCenter();
        };
    }

    private Border Disc(double size)
    {
        var (disc, glyph) = WidgetUi.IconDisc(MoonGlyph, "TextPrimaryBrush", size);
        _discs.Add((disc, glyph));
        return disc;
    }

    private static Services.NotificationCenterService Center => AppServices.NotificationCenter;

    private bool On => IsPreview || Center.DoNotDisturb;

    private int Count => IsPreview ? 3 : Center.Count;

    protected override void OnAttached()
    {
        if (IsPreview) return;
        Center.EnsureStarted();
        Center.Changed += Render;
    }

    protected override void OnDetached()
    {
        if (IsPreview) return;
        Center.Changed -= Render;
    }

    protected override void OnVariantChanged()
    {
        ShowLayout(_iconLayout, _statusLayout);
        Render();
    }

    private string Notifications => Count switch
    {
        0 => L.T("No notifications"),
        1 => L.T("1 notification"),
        _ => L.T("{0} notifications", Count),
    };

    private void Render()
    {
        bool on = On;
        foreach (var (disc, glyph) in _discs)
        {
            disc.SetResourceReference(Border.BackgroundProperty, on ? "AccentBrush" : "SubtleFillBrush");
            glyph.SetResourceReference(TextBlock.ForegroundProperty, on ? "OnAccentBrush" : "TextSecondaryBrush");
        }
        _state.Text = (on ? L.T("On") : L.T("Off")) + " · " + Notifications;
        ToolTip = string.Join("\n",
            L.T("Do not disturb: {0}", on ? L.T("On") : L.T("Off")),
            Notifications,
            L.T("Click to open the notification center, where the bell turns it on or off."));
        RefreshCompact();
    }

    protected override void UpdateCompact(CompactTile tile)
    {
        tile.ShowGlyph(Descriptor.Icon, On ? "AccentBrush" : "TextTertiaryBrush");
        tile.Text = Count > 0 ? (Count > 9 ? "9+" : Count.ToString(System.Globalization.CultureInfo.CurrentCulture)) : null;
    }

    public override bool OnCompactClick()
    {
        if (!IsPreview) AppServices.Shell.ShowNotificationCenter();
        return true;
    }

    public override void AddContextMenuItems(ItemCollection items)
    {
        items.Add(DockMenu.Item(L.T("Notification center"), "", () => AppServices.Shell.ShowNotificationCenter()));
        items.Add(DockMenu.Item(L.T("Notification settings"), "", () =>
        {
            try { Process.Start(new ProcessStartInfo("ms-settings:notifications") { UseShellExecute = true }); }
            catch (Exception ex) { Log.Error(ex, "Could not open the notification settings"); }
        }));
    }
}
