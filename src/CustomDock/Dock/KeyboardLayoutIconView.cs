using System.Windows;
using System.Windows.Controls;
using CustomDock.Core;
using CustomDock.Services;

namespace CustomDock.Dock;

/// <summary>
/// Input language next to the tray ("TUR", "ENG"), like the Windows taskbar. Click or scroll: next layout,
/// right click: all layouts and language settings. Only shown while more than one layout is installed.
/// </summary>
public sealed class KeyboardLayoutIconView : StatusIconViewBase
{
    private bool _allowed;
    private bool _subscribed;

    public KeyboardLayoutIconView()
    {
        Width = double.NaN;
        MinWidth = 30;
        Visibility = Visibility.Collapsed;
        Glyph.SetResourceReference(TextBlock.FontFamilyProperty, "UiFont");
        Glyph.FontSize = 11;
        Glyph.FontWeight = FontWeights.SemiBold;
        Glyph.Margin = new Thickness(6, 0, 6, 0);
    }

    private static KeyboardLayoutService Layouts => AppServices.KeyboardLayouts;

    /// <summary>Set by the dock: the indicator is turned on and this dock hosts the tray.</summary>
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
            Layouts.Subscribe();
            Layouts.Changed += Update;
            _subscribed = true;
        }
        else if (!_allowed && _subscribed)
        {
            Layouts.Changed -= Update;
            Layouts.Unsubscribe();
            _subscribed = false;
        }
    }

    protected override void Attach() => SyncSubscription();

    protected override void Detach()
    {
        if (!_subscribed) return;
        Layouts.Changed -= Update;
        Layouts.Unsubscribe();
        _subscribed = false;
    }

    protected override void Refresh()
    {
        var current = _subscribed ? Layouts.Current : null;
        bool show = _allowed && _subscribed && Layouts.Layouts.Count > 1 && current is not null;
        Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show) return;
        Glyph.Text = current!.Abbreviation;
        ToolTip = L.T("Input language: {0}", current.DisplayName) + "\n" + L.T("Click to switch, right-click for all languages");
    }

    protected override void OnClick() => Layouts.ActivateNext();

    protected override bool OnWheel(int delta)
    {
        Layouts.ActivateNext(backwards: delta > 0);
        return true;
    }

    protected override void BuildMenu(ItemCollection items)
    {
        items.Add(DockMenu.Header("Input language"));
        var current = Layouts.Current;
        foreach (var layout in Layouts.Layouts)
        {
            var handle = layout.Handle;
            items.Add(DockMenu.Check(layout.DisplayName, layout.Handle == current?.Handle, () => Layouts.Activate(handle)));
        }
        items.Add(DockMenu.Separator());
        items.Add(DockMenu.Item("Language settings", "", () => NetworkStatusIconView.OpenSettings("ms-settings:regionlanguage")));
        items.Add(DockMenu.Item("Typing settings", "", () => NetworkStatusIconView.OpenSettings("ms-settings:typing")));
    }
}
