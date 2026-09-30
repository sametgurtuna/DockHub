using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;
using CustomDock.Core;
using CustomDock.Dock;
using CustomDock.Services;

namespace CustomDock.Widgets;

/// <summary>
/// Windows' power mode (best power efficiency, balanced, best performance) in one click, and whether the PC runs on
/// battery with battery saver on. Where Windows doesn't let the mode change (another power plan), it shows the state
/// and opens Settings.
/// </summary>
public sealed class PowerModeWidget : WidgetBase
{
    public const string Icon = "M13,2 L4,14 H11 L10,22 L20,9 H13 Z";

    private static readonly PowerMode[] Modes = { PowerMode.BestEfficiency, PowerMode.Balanced, PowerMode.BestPerformance };

    private readonly StackPanel _iconLayout;
    private readonly StackPanel _buttonsLayout;
    private readonly Border _disc;
    private readonly Path _bolt;
    private readonly List<(PowerMode Mode, Border Button, TextBlock Label)> _buttons = new();
    private readonly Popup _popup;
    private readonly StackPanel _panel;

    public PowerModeWidget()
    {
        Background = Brushes.Transparent;
        _bolt = new Path { Data = Geometry.Parse(Icon), StrokeThickness = 1.6, Stretch = Stretch.Uniform, Width = 15, Height = 15, StrokeLineJoin = PenLineJoin.Round };
        _disc = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(17), Child = _bolt };
        _iconLayout = new StackPanel { Name = "Layout_icon", Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { _disc } };
        _iconLayout.Cursor = System.Windows.Input.Cursors.Hand;
        _iconLayout.MouseLeftButtonUp += (_, e) =>
        {
            if (DockDragHelper.JustDragged) return;
            e.Handled = true;
            OpenPanel();
        };

        _buttonsLayout = new StackPanel { Name = "Layout_buttons", Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var mode in Modes)
        {
            var label = new TextBlock { Text = ShortName(mode), FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            var button = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 5, 10, 5),
                Margin = new Thickness(_buttonsLayout.Children.Count == 0 ? 0 : 4, 0, 0, 0),
                Child = label,
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = L.T(PowerModeOverlays.EnglishName(mode)),
            };
            var target = mode;
            button.MouseLeftButtonUp += (_, e) =>
            {
                if (DockDragHelper.JustDragged || IsPreview) return;
                e.Handled = true;
                Choose(target);
            };
            _buttons.Add((mode, button, label));
            _buttonsLayout.Children.Add(button);
        }

        (_popup, _, _panel) = WidgetUi.Flyout(FlyoutSize.Narrow, L.T("Power mode"), null);
        Content = new Grid { Background = Brushes.Transparent, Children = { _iconLayout, _buttonsLayout, _popup } };
    }

    private static string ShortName(PowerMode mode) => mode switch
    {
        PowerMode.BestEfficiency => L.T("Efficiency"),
        PowerMode.BestPerformance => L.T("Performance"),
        _ => L.T("Balanced"),
    };

    private static PowerModeService Power => AppServices.PowerMode;

    private PowerMode? Mode => IsPreview ? PowerMode.Balanced : Power.Mode;

    private bool Supported => IsPreview || Power.Supported;

    protected override void OnAttached()
    {
        if (IsPreview) return;
        Power.EnsureStarted();
        Power.Changed += Render;
    }

    protected override void OnDetached()
    {
        if (IsPreview) return;
        Power.Changed -= Render;
    }

    protected override void OnVariantChanged()
    {
        ShowLayout(_iconLayout, _buttonsLayout);
        Render();
    }

    private void Choose(PowerMode mode)
    {
        if (!Power.TrySet(mode)) OpenSettings();
    }

    private static void OpenSettings()
    {
        try { Process.Start(new ProcessStartInfo("ms-settings:powersleep") { UseShellExecute = true }); }
        catch (Exception ex) { Log.Error(ex, "Could not open the power settings"); }
    }

    private static string BrushOf(PowerMode? mode) => mode switch
    {
        PowerMode.BestEfficiency => "AccentGreenBrush",
        PowerMode.BestPerformance => "AccentOrangeBrush",
        PowerMode.Balanced => "AccentBlueBrush",
        _ => "TextTertiaryBrush",
    };

    private string State
    {
        get
        {
            string mode = !Supported ? L.T("Set by Windows or your PC maker")
                : Mode is { } known ? L.T(PowerModeOverlays.EnglishName(known)) : L.T("Custom");
            if (IsPreview || !Power.HasBattery) return mode;
            string source = Power.OnBattery ? L.T("On battery") : L.T("Plugged in");
            return Power.BatterySaver ? $"{mode} · {source} · {L.T("Battery saver on")}" : $"{mode} · {source}";
        }
    }

    private void Render()
    {
        var mode = Mode;
        _bolt.SetResourceReference(Shape.StrokeProperty, BrushOf(mode));
        _disc.SetResourceReference(Border.BackgroundProperty, "SubtleFillBrush");
        foreach (var (buttonMode, button, label) in _buttons)
        {
            bool current = buttonMode == mode;
            button.SetResourceReference(Border.BackgroundProperty, current ? "AccentBrush" : "SubtleFillBrush");
            label.SetResourceReference(TextBlock.ForegroundProperty, current ? "OnAccentBrush" : "TextPrimaryBrush");
            button.Opacity = Supported ? 1 : 0.5;
        }
        ToolTip = string.Join("\n", L.T("Power mode"), State, Supported ? L.T("Click to change it.") : L.T("Click to open the power settings."));
        if (_popup.IsOpen) BuildPanel();
        RefreshCompact();
    }

    private void OpenPanel()
    {
        if (!Supported && !IsPreview)
        {
            OpenSettings();
            return;
        }
        BuildPanel();
        OpenPopup(_popup);
    }

    /// <summary>The panel: the three modes (the current one ticked), the battery state and a link to Settings.</summary>
    private void BuildPanel()
    {
        _panel.Children.Clear();
        foreach (var mode in Modes)
        {
            var check = WidgetUi.Glyph(mode == Mode ? "" : "", 12, "AccentBrush");
            check.Width = 18;
            var row = new StackPanel { Orientation = Orientation.Horizontal, Children = { check, WidgetUi.Text("TitleText", L.T(PowerModeOverlays.EnglishName(mode)), 12.5) } };
            var target = mode;
            _panel.Children.Add(WidgetUi.HoverRow(row, () =>
            {
                if (IsPreview) return;
                Choose(target);
            }));
        }
        var state = WidgetUi.Text("CaptionText", State);
        state.Margin = new Thickness(8, 8, 8, 4);
        state.TextWrapping = TextWrapping.Wrap;
        _panel.Children.Add(state);
        _panel.Children.Add(WidgetUi.HoverRow(WidgetUi.Text("CaptionText", L.T("Power settings…")), OpenSettings));
    }

    protected override void UpdateCompact(CompactTile tile)
    {
        tile.ShowGlyph(Descriptor.Icon, BrushOf(Mode));
        tile.Text = null;
    }

    public override bool OnCompactClick()
    {
        if (IsPreview) return true;
        // The next mode round the three, like pressing the tile repeatedly in Quick Settings.
        if (!Supported || Mode is not { } mode)
        {
            OpenSettings();
            return true;
        }
        Choose(Modes[(Array.IndexOf(Modes, mode) + 1) % Modes.Length]);
        return true;
    }

    public override void AddContextMenuItems(ItemCollection items)
    {
        foreach (var mode in Modes)
        {
            var target = mode;
            items.Add(DockMenu.Check(L.T(PowerModeOverlays.EnglishName(mode)), mode == Mode, () => Choose(target)));
        }
        items.Add(DockMenu.Item(L.T("Power settings…"), "", OpenSettings));
    }
}
