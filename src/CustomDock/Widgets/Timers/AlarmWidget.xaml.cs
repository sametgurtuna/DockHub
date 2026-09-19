using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using CustomDock.Core;
using CustomDock.Dock;
using CustomDock.Services;

namespace CustomDock.Widgets;

public sealed class AlarmSettings : ObservableObject
{
    private bool _enabled;
    private string _time = "07:30";
    private bool _repeatDaily;
    private string _label = "";

    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }

    /// <summary>Time in "HH:mm" format.</summary>
    public string Time { get => _time; set => Set(ref _time, value); }

    public bool RepeatDaily { get => _repeatDaily; set => Set(ref _repeatDaily, value); }

    public string Label { get => _label; set => Set(ref _label, value); }

    public TimeSpan? TimeOfDay => TimeInput.TryParse(Time, out var t) ? t : null;

    public DateTime? NextOccurrence(DateTime now)
    {
        if (!Enabled || TimeOfDay is not { } t) return null;
        var today = now.Date + t;
        return today > now ? today : today.AddDays(1);
    }
}

/// <summary>Clock alarm; shows notification when due.</summary>
public partial class AlarmWidget : WidgetBase
{
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Normal);
    private AlarmSettings _settings = new();

    public AlarmWidget()
    {
        InitializeComponent();
        _timer.Tick += (_, _) => OnTimer();
    }

    protected override void OnAttached()
    {
        _settings = GetSettings<AlarmSettings>();
        _settings.PropertyChanged += OnSettingsChanged;
        AppServices.Clock.MinuteTick += OnMinuteTick;
        Schedule();
        Render();
    }

    protected override void OnDetached()
    {
        _settings.PropertyChanged -= OnSettingsChanged;
        AppServices.Clock.MinuteTick -= OnMinuteTick;
        _timer.Stop();
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        Schedule();
        Render();
    }

    private void OnMinuteTick(object? sender, DateTime e) => Render();

    private void Schedule()
    {
        _timer.Stop();
        if (IsPreview || _settings.NextOccurrence(DateTime.Now) is not { } next) return;
        var wait = next - DateTime.Now;
        // Re-evaluate at most every 10 minutes against sleep / time changes.
        _timer.Interval = wait > TimeSpan.FromMinutes(10) ? TimeSpan.FromMinutes(10) : wait < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : wait;
        _timer.Start();
    }

    private void OnTimer()
    {
        var now = DateTime.Now;
        if (_settings.TimeOfDay is { } t && _settings.Enabled)
        {
            var due = now.Date + t;
            if (now >= due && now - due < TimeSpan.FromMinutes(2))
            {
                AppServices.Notifications.ShowAlarm(
                    string.IsNullOrWhiteSpace(_settings.Label) ? "Alarm" : _settings.Label,
                    _settings.Time,
                    "alarm-" + Item.Id);
                if (!_settings.RepeatDaily)
                    _settings.Enabled = false;
                // Avoid re-triggering in the same minute
                _timer.Stop();
                _timer.Interval = TimeSpan.FromMinutes(2);
                _timer.Start();
                return;
            }
        }
        Schedule();
    }

    private void Render()
    {
        var next = _settings.NextOccurrence(DateTime.Now);
        TitleText.Text = next is null ? "Alarm" : _settings.Time;
        if (next is null)
        {
            SubText.Text = "Set time";
        }
        else
        {
            string day = next.Value.Date == DateTime.Today ? "Today" : "Tomorrow";
            string label = string.IsNullOrWhiteSpace(_settings.Label) ? (_settings.RepeatDaily ? "Every day" : day) : _settings.Label;
            SubText.Text = _settings.RepeatDaily && !string.IsNullOrWhiteSpace(_settings.Label) ? $"{label} · daily" : label;
        }
        Bell.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, next is null ? "TextSecondaryBrush" : "AccentOrangeBrush");
        ToolTip = next is null ? "Click to set alarm" : $"Next: {next.Value:dddd HH:mm}";
        RefreshCompact();
    }

    protected override void UpdateCompact(CompactTile tile)
    {
        bool set = _settings.NextOccurrence(DateTime.Now) is not null;
        tile.ShowGlyph(Descriptor.Icon, set ? "AccentOrangeBrush" : "TextSecondaryBrush");
        tile.Text = set ? _settings.Time : null;
    }

    public override bool OnCompactClick()
    {
        OpenEditor();
        return true;
    }

    private void OnClick(object sender, MouseButtonEventArgs e) => OpenEditor();

    private void OpenEditor()
    {
        TimeBox.Text = _settings.Time;
        LabelBox.Text = _settings.Label;
        RepeatBox.IsChecked = _settings.RepeatDaily;
        ErrorText.Text = "";
        DisableButton.Visibility = _settings.Enabled ? Visibility.Visible : Visibility.Collapsed;
        OpenPopup(EditorPopup);
    }

    private async void OnPopupOpened(object? sender, EventArgs e)
    {
        Host.ActivateForInput();
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Input);
        TimeBox.Focus();
        TimeBox.SelectAll();
    }

    private void OnEditorKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Save();
        else if (e.Key == Key.Escape) EditorPopup.IsOpen = false;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e) => Save();

    private void Save()
    {
        if (!TimeInput.TryParse(TimeBox.Text, out var time))
        {
            ErrorText.Text = "Enter time in HH:mm format.";
            return;
        }
        _settings.Time = $"{time.Hours:00}:{time.Minutes:00}";
        _settings.Label = LabelBox.Text.Trim();
        _settings.RepeatDaily = RepeatBox.IsChecked == true;
        _settings.Enabled = true;
        EditorPopup.IsOpen = false;
    }

    private void OnDisableClick(object sender, RoutedEventArgs e)
    {
        _settings.Enabled = false;
        EditorPopup.IsOpen = false;
    }

    public override void AddContextMenuItems(ItemCollection items)
    {
        items.Add(DockMenu.Item("Edit alarm…", "\uE70F", OpenEditor));
        items.Add(DockMenu.Check("Alarm enabled", _settings.Enabled, () => _settings.Enabled = !_settings.Enabled));
        items.Add(DockMenu.Check("Repeat daily", _settings.RepeatDaily, () => _settings.RepeatDaily = !_settings.RepeatDaily));
    }
}

public static class TimeInput
{
    /// <summary>Accepts inputs like "14:30", "14.30", "1430", "9".</summary>
    public static bool TryParse(string? input, out TimeSpan time)
    {
        time = default;
        var s = (input ?? "").Trim().Replace('.', ':');
        if (s.Length is 3 or 4 && s.All(char.IsDigit))
            s = s[..^2] + ":" + s[^2..];
        else if (s.Length is 1 or 2 && s.All(char.IsDigit))
            s += ":00";

        if (!TimeSpan.TryParseExact(s, new[] { @"h\:mm", @"hh\:mm" }, CultureInfo.InvariantCulture, out var parsed))
            return false;
        if (parsed < TimeSpan.Zero || parsed >= TimeSpan.FromDays(1))
            return false;
        time = parsed;
        return true;
    }
}
