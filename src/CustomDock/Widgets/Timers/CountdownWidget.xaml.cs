using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Input;
using CustomDock.Core;
using CustomDock.Dock;

namespace CustomDock.Widgets;

public sealed class CountdownSettings : ObservableObject
{
    private int _seconds = 300;
    private string _label = "Countdown";

    public int Seconds { get => _seconds; set => Set(ref _seconds, Math.Clamp(value, 5, 24 * 3600)); }

    public string Label { get => _label; set => Set(ref _label, value); }

    public static IReadOnlyList<Option<int>> Presets { get; } = Options.Of(
        (60, "1 minute"), (180, "3 minutes"), (300, "5 minutes"), (600, "10 minutes"), (900, "15 minutes"),
        (1200, "20 minutes"), (1800, "30 minutes"), (2700, "45 minutes"), (3600, "1 hour"), (7200, "2 hours"));
}

/// <summary>Countdown timer: click to start/pause, notifies on completion.</summary>
public partial class CountdownWidget : WidgetBase
{
    private CountdownSettings _settings = new();
    private bool _running;
    private bool _finished;
    private DateTime _endsAt;
    private TimeSpan _remaining;

    public CountdownWidget()
    {
        InitializeComponent();
    }

    private TimeSpan Total => TimeSpan.FromSeconds(_settings.Seconds);

    protected override void OnAttached()
    {
        _settings = GetSettings<CountdownSettings>();
        _settings.PropertyChanged += OnSettingsChanged;
        _remaining = Total;
        Render();
    }

    protected override void OnDetached()
    {
        _settings.PropertyChanged -= OnSettingsChanged;
        AppServices.Clock.SecondTick -= OnTick;
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CountdownSettings.Seconds)) Reset();
        else Render();
    }

    private void OnClick(object sender, MouseButtonEventArgs e) => Toggle();

    private void Toggle()
    {
        if (_finished)
        {
            Reset();
            return;
        }

        if (_running)
        {
            _remaining = _endsAt - DateTime.Now;
            _running = false;
            AppServices.Clock.SecondTick -= OnTick;
        }
        else
        {
            _endsAt = DateTime.Now + _remaining;
            _running = true;
            AppServices.Clock.SecondTick += OnTick;
        }
        Render();
    }

    private void Reset()
    {
        _running = false;
        _finished = false;
        AppServices.Clock.SecondTick -= OnTick;
        _remaining = Total;
        Render();
    }

    private void OnTick(object? sender, DateTime now)
    {
        if (_running && now >= _endsAt)
        {
            _running = false;
            _finished = true;
            _remaining = TimeSpan.Zero;
            AppServices.Clock.SecondTick -= OnTick;
            Notify("Time's up ⏱", $"{_settings.Label} ({TimerFormat.Format(Total)}) completed.", tag: "countdown-" + Item.Id);
        }
        Render();
    }

    private void Render()
    {
        var remaining = _running ? _endsAt - DateTime.Now : _remaining;
        TimeText.Text = TimerFormat.FormatRemaining(remaining);
        LabelText.Text = _finished ? "Time's up" : _running || remaining == Total ? _settings.Label : "Paused";
        string brush = _finished ? "AccentOrangeBrush" : _running ? "AccentYellowBrush" : "TextPrimaryBrush";
        Icon.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, brush);
        TimeText.SetResourceReference(TextBlock.ForegroundProperty, _finished ? "AccentOrangeBrush" : "TextPrimaryBrush");
        ToolTip = _finished ? "Click to reset" : (_running ? "Click to pause" : "Click to start") + "\nRight-click: select duration";
        RefreshCompact();
    }

    protected override void UpdateCompact(CompactTile tile)
    {
        var remaining = _running ? _endsAt - DateTime.Now : _remaining;
        tile.ShowGlyph(Descriptor.Icon, _finished ? "AccentOrangeBrush" : _running ? "AccentYellowBrush" : "TextSecondaryBrush");
        tile.SetTextBrushKey(_finished ? "AccentOrangeBrush" : "TextPrimaryBrush");
        tile.Text = TimerFormat.FormatRemaining(remaining);
    }

    public override bool OnCompactClick()
    {
        Toggle();
        return true;
    }

    public override void AddContextMenuItems(ItemCollection items)
    {
        items.Add(DockMenu.Item(_running ? "Pause" : "Start", _running ? "\uE769" : "\uE768", Toggle));
        items.Add(DockMenu.Item("Reset", "\uE72C", Reset));
        items.Add(DockMenu.Submenu("Duration", "\uE916",
            CountdownSettings.Presets.Select(o => DockMenu.Check(o.Label, _settings.Seconds == o.Value, () => _settings.Seconds = o.Value))));
    }
}
