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

    /// <summary>Odak bitince molayı (ve tersini) kendiliğinden başlatır.</summary>
    public bool AutoStartNext { get => _autoStartNext; set => Set(ref _autoStartNext, value); }

    /// <summary>Bu kadar odak oturumundan sonra kısa mola yerine uzun mola verilir.</summary>
    public int SessionsBeforeLongBreak { get => _sessionsBeforeLongBreak; set => Set(ref _sessionsBeforeLongBreak, Math.Clamp(value, 2, 8)); }

    public int LongBreakMinutes { get => _longBreakMinutes; set => Set(ref _longBreakMinutes, Math.Clamp(value, 1, 60)); }

    public static IReadOnlyList<Option<int>> FocusOptions { get; } =
        Options.Of((15, "15 dakika"), (25, "25 dakika"), (30, "30 dakika"), (45, "45 dakika"), (50, "50 dakika"), (60, "60 dakika"), (90, "90 dakika"));

    public static IReadOnlyList<Option<int>> BreakOptions { get; } =
        Options.Of((3, "3 dakika"), (5, "5 dakika"), (10, "10 dakika"), (15, "15 dakika"), (20, "20 dakika"));

    public static IReadOnlyList<Option<int>> LongBreakOptions { get; } =
        Options.Of((10, "10 dakika"), (15, "15 dakika"), (20, "20 dakika"), (30, "30 dakika"));

    public static IReadOnlyList<Option<int>> SessionCountOptions { get; } =
        Options.Of((2, "2 oturum"), (3, "3 oturum"), (4, "4 oturum"), (6, "6 oturum"), (8, "8 oturum"));

    /// <summary>Panelde hızlı seçim için gösterilen odak süreleri.</summary>
    public static IReadOnlyList<int> QuickFocusMinutes { get; } = new[] { 15, 25, 45, 60 };
}

/// <summary>
/// Pomodoro tarzı odak zamanlayıcı. Karta tıklayınca kontrol paneli açılır (doğrudan başlatıp durdurmaz);
/// başlat/duraklat/sıfırla ve süre seçimi panelin içindedir. Belirli sayıda odak oturumundan sonra uzun mola verir.
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

    // ------------------------------------------------------------------ Zamanlayıcı

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

    /// <summary>Odak → mola, mola → bir sonraki odak oturumu geçişi (oturum sayacını da ilerletir).</summary>
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
            Notify(finishedBreak ? "Mola bitti" : "Odak süresi bitti",
                finishedBreak
                    ? "Yeni bir odak oturumuna hazırsın."
                    : wasLongBreak ? "Harika iş çıkardın! Şimdi uzun bir mola ver." : $"Harika! {_settings.BreakMinutes} dakika mola ver.",
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

        string phase = _isBreak ? (IsLongBreak ? "Uzun mola" : "Mola") : "Odak";
        PhaseText.Text = _running ? phase : remaining < PhaseLength ? $"{phase} · duraklatıldı" : phase;
        PopupPhase.Text = $"{phase} · {_settings.SessionsBeforeLongBreak} oturumdan {_session}.si";

        ToggleButton.Content = _running ? "Duraklat" : "Başlat";

        foreach (var (minutes, button) in _presetButtons)
        {
            bool selected = _settings.FocusMinutes == minutes;
            button.SetResourceReference(Button.BackgroundProperty, selected ? "AccentBrush" : "SubtleFillBrush");
            button.SetResourceReference(Button.ForegroundProperty, selected ? "OnAccentBrush" : "TextPrimaryBrush");
        }

        ToolTip = (_running ? "Duraklatmak için tıklayın" : "Başlatmak için tıklayın") + "\nSağ tık: sıfırla, atla, süreler";
        RefreshCompact();
    }

    private void BuildPresets()
    {
        foreach (var minutes in FocusSettings.QuickFocusMinutes)
        {
            var button = new Button
            {
                Style = (Style)FindResource("PillButton"),
                Content = $"{minutes} dk",
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
        items.Add(DockMenu.Item("Zamanlayıcıyı aç…", "", OpenEditor));
        items.Add(DockMenu.Item(_running ? "Duraklat" : "Başlat", _running ? "" : "", Toggle));
        items.Add(DockMenu.Item(_isBreak ? "Molayı atla" : "Odağı atla", "", Skip));
        items.Add(DockMenu.Item("Sıfırla", "", Reset));
        items.Add(DockMenu.Submenu("Odak süresi", "",
            FocusSettings.FocusOptions.Select(o => DockMenu.Check(o.Label, _settings.FocusMinutes == o.Value, () => _settings.FocusMinutes = o.Value))));
        items.Add(DockMenu.Submenu("Mola süresi", "",
            FocusSettings.BreakOptions.Select(o => DockMenu.Check(o.Label, _settings.BreakMinutes == o.Value, () => _settings.BreakMinutes = o.Value))));
    }
}
