using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace CustomDock.Dock;

/// <summary>
/// Glyph-based system status icon next to the tray (network, volume, battery). Windows 11 draws these inside
/// Explorer's XAML taskbar, so DockHub renders its own equivalents from system state.
/// </summary>
public abstract class StatusIconViewBase : Border
{
    private readonly Border _hover;

    protected StatusIconViewBase()
    {
        Width = 28;
        Height = 46;
        Background = Brushes.Transparent;
        Focusable = false;

        _hover = new Border { CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 9, 0, 9) };
        Glyph = new TextBlock
        {
            FontSize = 15,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Glyph.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        Glyph.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");

        var grid = new Grid();
        grid.Children.Add(_hover);
        grid.Children.Add(Glyph);
        Child = grid;

        MouseEnter += (_, _) => _hover.SetResourceReference(BackgroundProperty, "DockHoverBrush");
        MouseLeave += (_, _) => _hover.Background = null;
        MouseLeftButtonUp += (_, e) => { e.Handled = true; OnClick(); };
        MouseUp += (_, e) =>
        {
            if (e.ChangedButton != MouseButton.Middle) return;
            e.Handled = true;
            OnMiddleClick();
        };
        MouseWheel += (_, e) =>
        {
            if (OnWheel(e.Delta)) e.Handled = true;
        };

        ContextMenu = new ContextMenu();
        ContextMenuOpening += (_, e) =>
        {
            ContextMenu.Items.Clear();
            BuildMenu(ContextMenu.Items);
            if (ContextMenu.Items.Count == 0) e.Handled = true;
        };

        Loaded += (_, _) => { Attach(); Update(); };
        Unloaded += (_, _) => Detach();
    }

    protected TextBlock Glyph { get; }

    /// <summary>Subscribe to the system state this icon shows.</summary>
    protected abstract void Attach();

    protected abstract void Detach();

    /// <summary>Update glyph, tooltip and colors.</summary>
    protected abstract void Refresh();

    protected virtual void OnClick()
    {
    }

    protected virtual void OnMiddleClick()
    {
    }

    /// <summary>Mouse wheel; return true when handled.</summary>
    protected virtual bool OnWheel(int delta) => false;

    protected virtual void BuildMenu(ItemCollection items)
    {
    }

    /// <summary>Refresh on the UI thread (system notifications may arrive on other threads).</summary>
    protected void RefreshSoon(object? sender = null, EventArgs? e = null) => Dispatcher.BeginInvoke(Update);

    /// <summary>Refreshes the icon and gives screen readers its current state.</summary>
    protected void Update()
    {
        Refresh();
        System.Windows.Automation.AutomationProperties.SetName(this, ToolTip as string ?? "");
    }
}
