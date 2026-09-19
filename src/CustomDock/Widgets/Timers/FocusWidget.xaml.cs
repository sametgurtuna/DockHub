using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CustomDock.Core;
using CustomDock.Dock;

namespace CustomDock.Widgets;

public sealed class FocusSettings : ObservableObject
{
    private int _focusMinutes = 25;
    private int _breakMinutes = 5;
    private bool _autoStartNext = true;
    private int _sessionsBeforeLongBreak = 4;
    private int _longBreakMinutes = 15;

    public int FocusMinutes { get => _focusMinutes; set => Set(ref _focusMinutes, Math.Clamp(value, 1, 180)); }

    public int BreakMinutes { get => _breakMinutes; set => Set(ref _breakMinutes, Math.Clamp(value, 1, 60)); }

    /// <summary>Automatically starts break when focus ends and vice versa.</summary>
    public bool AutoStartNext { get => _autoStartNext; set => Set(ref _autoStartNext, value); }

    /// <summary>Take a long break instead of a short break after this many focus sessions.</summary>
    public int SessionsBeforeLongBreak { get => _sessionsBeforeLongBreak; set => Set(ref _sessionsBeforeLongBreak, Math.Clamp(value, 2, 8)); }

    public int LongBreakMinutes { get => _longBreakMinutes; set => Set(ref _longBreakMinutes, Math.Clamp(value, 1, 60)); }

    public static IReadOnlyList<Option<int>> FocusOptions { get; } =
        Options.Of((15, "15 minutes"), (25, "25 minutes"), (30, "30 minutes"), (45, "45 minutes"), (50, "50 minutes"), (60, "60 minutes"), (90, "90 minutes"));

    public static IReadOnlyList<Option<int>> BreakOptions { get; } =
        Options.Of((3, "3 minutes"), (5, "5 minutes"), (10, "10 minutes"), (15, "15 minutes"), (20, "20 minutes"));

    public static IReadOnlyList<Option<int>> LongBreakOptions { get; } =
        Options.Of((10, "10 minutes"), (15, "15 minutes"), (20, "20 minutes"), (30, "30 minutes"));

    public static IReadOnlyList<Option<int>> SessionCountOptions { get; } =
        Options.Of((2, "2 sessions"), (3, "3 sessions"), (4, "4 sessions"), (6, "6 sessions"), (8, "8 sessions"));

    /// <summary>Focus durations shown for quick selection in the flyout.</summary>
    public static IReadOnlyList<int> QuickFocusMinutes { get; } = new[] { 15, 25, 45, 60 };
}

/// <summary>
/// Pomodoro-style focus timer. Clicking the card opens the control panel;
/// start/pause/reset and duration selection are inside the panel. Provides a long break after a set number of focus sessions.
/// </summary>
public partial class FocusWidget : WidgetBase
{
    private readonly Dictionary<int, Button> _presetButtons = new();
    private FocusSettings _settings = new();
    private bool _isBreak;
    private bool _running;
    private int _session = 1;
    private DateTime _endsAt;
    private TimeSpan _remaining;

    public FocusWidget()
    {
        InitializeComponent();
        BuildPresets();
    }

    private bool IsLongBreak => _isBreak && _session % Math.Max(1, _settings.SessionsBeforeLongBreak) == 0;

    private TimeSpan PhaseLength => TimeSpan.FromMinutes(
        _isBreak ? (IsLongBreak ? _settings.LongBreakMinutes : _settings.BreakMinutes) : _settings.FocusMinutes);

    protected override void OnAttached()
    {
        _settings = GetSettings<FocusSettings>();
        _settings.PropertyChanged += OnSettingsChanged;
        _remaining = PhaseLength;
        Render();
    }

    protected override void OnDetached()
    {
        _settings.PropertyChanged -= OnSettingsChanged;
        AppServices.Clock.SecondTick -= OnTick;
        EditorPopup.IsOpen = false;
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_running) _remaining = PhaseLength;
        Render();
    }

    // ------------------------------------------------------------------ Panel

    private void OnClick(object sender, MouseButtonEventArgs e) => OpenEditor();

    public override bool OnCompactClick()
    {
        OpenEditor();
        return true;
    }

    private void OpenEditor()
    {
        if (IsPreview) return;
        Render();
        OpenPopup(EditorPopup, Root);
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => EditorPopup.IsOpen = false;

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        EditorPopup.IsOpen = false;
        WidgetItemView.RequestSettings(Item);
    }

    private void OnToggleClick(object sender, RoutedEventArgs e) => Toggle();

    private void OnResetClick(object sender, RoutedEventArgs e) => Reset();

    // ------------------------------------------------------------------ Timer

    private void Toggle()
    {
        if (_running)
        {
            _remaining = _endsAt - DateTime.Now;
            _running = false;
            AppServices.Clock.SecondTick -= OnTick;
        }
        else
        {
            if (_remaining <= TimeSpan.Zero) _remaining = PhaseLength;
            _endsAt = DateTime.Now + _remaining;
            _running = true;
            AppServices.Clock.SecondTick += OnTick;
        }
        Render();
    }

    private void Reset()
    {
        _running = false;
        _isBreak = false;
        _session = 1;
        AppServices.Clock.SecondTick -= OnTick;
        _remaining = PhaseLength;
        Render();
    }

    private void Skip()
    {
        bool wasRunning = _running;
        _running = false;
        AppServices.Clock.SecondTick -= OnTick;
        AdvancePhase();
        _remaining = PhaseLength;
        if (wasRunning) Toggle();
        Render();
    }

    /// <summary>Advance focus → break, break → next focus session (also advances session counter).</summary>
    private void AdvancePhase()
    {
        if (_isBreak)
        {
            _session = _session % Math.Max(1, _settings.SessionsBeforeLongBreak) + 1;
            _isBreak = false;
        }
        else
        {
            _isBreak = true;
        }
    }

    private void OnTick(object? sender, DateTime now)
    {
        if (_running && now >= _endsAt)
        {
            bool finishedBreak = _isBreak;
            bool wasLongBreak = IsLongBreak;
            Notify(finishedBreak ? "Break ended" : "Focus session ended",
                finishedBreak
                    ? "Ready for a new focus session."
                    : wasLongBreak ? "Great job! Time for a long break." : $"Great job! Take a {_settings.BreakMinutes}-minute break.",
                tag: "focus-" + Item.Id);
            _running = false;
            AppServices.Clock.SecondTick -= OnTick;
            AdvancePhase();
            _remaining = PhaseLength;
            if (_settings.AutoStartNext) Toggle();
        }
        Render();
    }

    private void Render()
    {
        var remaining = _running ? _endsAt - DateTime.Now : _remaining;
        string time = TimerFormat.FormatRemaining(remaining);
        TimeText.Text = time;
        PopupTime.Text = time;

        Ring.Value = PhaseLength.TotalSeconds <= 0 ? 0 : Math.Clamp(remaining.TotalSeconds / PhaseLength.TotalSeconds, 0, 1);
        string fillKey = _isBreak ? "AccentGreenBrush" : "AccentOrangeBrush";
        string trackKey = _isBreak ? "GreenTrackBrush" : "OrangeTrackBrush";
        Ring.SetResourceReference(Controls.RingGauge.FillProperty, fillKey);
        Ring.SetResourceReference(Controls.RingGauge.TrackProperty, trackKey);

        string phase = _isBreak ? (IsLongBreak ? "Long break" : "Break") : "Focus";
        PhaseText.Text = _running ? phase : remaining < PhaseLength ? $"{phase} · paused" : phase;
        PopupPhase.Text = $"{phase} · session {_session} of {_settings.SessionsBeforeLongBreak}";

        ToggleButton.Content = _running ? "Pause" : "Start";

        foreach (var (minutes, button) in _presetButtons)
        {
            bool selected = _settings.FocusMinutes == minutes;
            button.SetResourceReference(Button.BackgroundProperty, selected ? "AccentBrush" : "SubtleFillBrush");
            button.SetResourceReference(Button.ForegroundProperty, selected ? "OnAccentBrush" : "TextPrimaryBrush");
        }

        ToolTip = (_running ? "Click to pause" : "Click to start") + "\nRight-click: reset, skip, durations";
        RefreshCompact();
    }

    private void BuildPresets()
    {
        foreach (var minutes in FocusSettings.QuickFocusMinutes)
        {
            var button = new Button
            {
                Style = (Style)FindResource("PillButton"),
                Content = $"{minutes} min",
                Margin = new Thickness(3, 0, 3, 0),
                MinWidth = 54,
            };
            button.Click += (_, _) => _settings.FocusMinutes = minutes;
            _presetButtons[minutes] = button;
            PresetList.Children.Add(button);
        }
    }

    protected override void UpdateCompact(CompactTile tile)
    {
        var remaining = _running ? _endsAt - DateTime.Now : _remaining;
        double fraction = PhaseLength.TotalSeconds <= 0 ? 0 : Math.Clamp(remaining.TotalSeconds / PhaseLength.TotalSeconds, 0, 1);
        tile.ShowRing(fraction, 1, _isBreak ? "AccentGreenBrush" : "AccentOrangeBrush", _isBreak ? "GreenTrackBrush" : "OrangeTrackBrush");
        tile.Text = TimerFormat.FormatRemaining(remaining);
    }

    public override void AddContextMenuItems(ItemCollection items)
    {
        items.Add(DockMenu.Item("Open timer…", "\uE70F", OpenEditor));
        items.Add(DockMenu.Item(_running ? "Pause" : "Start", _running ? "\uE769" : "\uE768", Toggle));
        items.Add(DockMenu.Item(_isBreak ? "Skip break" : "Skip focus", "\uE893", Skip));
        items.Add(DockMenu.Item("Reset", "\uE72C", Reset));
        items.Add(DockMenu.Submenu("Focus duration", "\uE916",
            FocusSettings.FocusOptions.Select(o => DockMenu.Check(o.Label, _settings.FocusMinutes == o.Value, () => _settings.FocusMinutes = o.Value))));
        items.Add(DockMenu.Submenu("Break duration", "\uEC72",
            FocusSettings.BreakOptions.Select(o => DockMenu.Check(o.Label, _settings.BreakMinutes == o.Value, () => _settings.BreakMinutes = o.Value))));
    }
}
