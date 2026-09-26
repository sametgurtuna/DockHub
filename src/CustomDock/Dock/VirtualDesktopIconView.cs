using System.Windows;
using System.Windows.Controls;
using CustomDock.Core;
using CustomDock.Services;

namespace CustomDock.Dock;

/// <summary>
/// Number of the current virtual desktop, next to Task view. Click: Task view; scroll: previous or next desktop;
/// right click: every desktop, new and close. Only shown while more than one desktop exists.
/// </summary>
public sealed class VirtualDesktopIconView : StatusIconViewBase
{
    private readonly Border _frame;
    private bool _allowed;
    private bool _subscribed;

    public VirtualDesktopIconView()
    {
        Width = 36;
        Visibility = Visibility.Collapsed;
        Glyph.SetResourceReference(TextBlock.FontFamilyProperty, "UiFont");
        Glyph.FontSize = 11.5;
        Glyph.FontWeight = FontWeights.SemiBold;
        // A small outlined square around the number reads as "desktop".
        _frame = new Border
        {
            Width = 20,
            Height = 17,
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1.3),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };
        _frame.SetResourceReference(Border.BorderBrushProperty, "TextSecondaryBrush");
        ((Grid)Child).Children.Insert(1, _frame);
    }

    private static VirtualDesktopService Desktops => AppServices.VirtualDesktops;

    public bool Allowed
    {
        get => _allowed;
        set
        {
            _allowed = value;
            if (IsLoaded) SyncSubscription();
            Update();
        }
    }

    private void SyncSubscription()
    {
        if (_allowed && !_subscribed)
        {
            Desktops.EnsureStarted();
            Desktops.Changed += Update;
            _subscribed = true;
        }
        else if (!_allowed && _subscribed)
        {
            Desktops.Changed -= Update;
            _subscribed = false;
        }
    }

    protected override void Attach() => SyncSubscription();

    protected override void Detach()
    {
        if (!_subscribed) return;
        Desktops.Changed -= Update;
        _subscribed = false;
    }

    protected override void Refresh()
    {
        var current = _subscribed ? Desktops.Current : null;
        bool show = _allowed && current is not null && Desktops.Desktops.Count > 1;
        Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show) return;
        Glyph.Text = current!.Number.ToString(System.Globalization.CultureInfo.CurrentCulture);
        ToolTip = L.T("{0} ({1} of {2})", current.Name, current.Number, Desktops.Desktops.Count) + "\n" + L.T("Click for Task view, scroll to switch desktops");
    }

    protected override void OnClick() => AppServices.Shell?.ShowTaskView();

    protected override bool OnWheel(int delta)
    {
        if (delta > 0) Desktops.Previous();
        else Desktops.Next();
        return true;
    }

    protected override void BuildMenu(ItemCollection items)
    {
        items.Add(DockMenu.Header("Desktops"));
        foreach (var desktop in Desktops.Desktops)
        {
            var target = desktop;
            items.Add(DockMenu.Check(desktop.Name, desktop.Id == Desktops.Current?.Id, () => Desktops.SwitchTo(target)));
        }
        items.Add(DockMenu.Separator());
        items.Add(DockMenu.Item("New desktop", "", Desktops.CreateDesktop));
        items.Add(DockMenu.Item("Close this desktop", "", Desktops.CloseCurrent));
        items.Add(DockMenu.Separator());
        var config = AppServices.Config;
        items.Add(DockMenu.Check("Show apps from all desktops", config.RunningAppsAllDesktops,
            () => config.RunningAppsAllDesktops = !config.RunningAppsAllDesktops));
    }
}
