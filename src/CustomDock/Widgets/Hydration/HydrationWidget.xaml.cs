using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CustomDock.Core;
using CustomDock.Dock;
using CustomDock.Services;

namespace CustomDock.Widgets;

/// <summary>Option lists used in setting templates.</summary>
public static class WidgetOptions
{
    public static IReadOnlyList<Option<int>> HydrationIntervals { get; } = Options.Of(
        (15, "15 minutes"), (30, "30 minutes"), (45, "45 minutes"), (60, "1 hour"), (90, "1.5 hours"), (120, "2 hours"), (180, "3 hours"));

    public static IReadOnlyList<Option<int>> GlassSizes { get; } = Options.Of(
        (150, "150 ml"), (200, "200 ml"), (250, "250 ml"), (300, "300 ml"), (330, "330 ml"), (500, "500 ml"));

    public static IReadOnlyList<Option<int>> StartHours { get; } = Options.Hours(0, 23);

    public static IReadOnlyList<Option<int>> EndHours { get; } = Options.Hours(1, 24);

    public static IReadOnlyList<Option<int>> SystemIntervals { get; } =
        Options.Of((1, "1 second"), (2, "2 seconds"), (3, "3 seconds"), (5, "5 seconds"), (10, "10 seconds"));
}

/// <summary>Hydration: countdown to next reminder or daily goal ring. Click → +1 glass.</summary>
public partial class HydrationWidget : WidgetBase
{
    private HydrationSettings _settings = new();

    public HydrationWidget()
    {
        InitializeComponent();
    }

    private static HydrationService Hydration => AppServices.Hydration;

    protected override void OnAttached()
    {
        _settings = GetSettings<HydrationSettings>();
        _settings.PropertyChanged += OnSettingsChanged;
        Hydration.Changed += Render;
        if (!IsPreview) Hydration.Start(_settings);
    }

    protected override void OnDetached()
    {
        _settings.PropertyChanged -= OnSettingsChanged;
        Hydration.Changed -= Render;
        AppServices.Clock.SecondTick -= OnTick;
        AppServices.Clock.MinuteTick -= OnTick;
        if (!IsPreview) Hydration.Stop();
    }

    protected override void OnVariantChanged()
    {
        ShowLayout(Layout_timer, Layout_progress);
        AppServices.Clock.SecondTick -= OnTick;
        AppServices.Clock.MinuteTick -= OnTick;

        if (Variant == "timer")
        {
            SetResourceReference(CardBackgroundProperty, "HydrationCardBrush");
            AppServices.Clock.SecondTick += OnTick;
        }
        else
        {
            ClearValue(CardBackgroundProperty);
            AppServices.Clock.MinuteTick += OnTick; // midnight reset
        }
        Render();
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e) => Render();

    private void OnTick(object? sender, DateTime now)
    {
        if (now.Second == 0) Hydration.EnsureToday();
        Render();
    }

    private void Render()
    {
        int count = Hydration.Count;
        int goal = _settings.DailyGoal;
        double liters = count * _settings.GlassMl / 1000.0;

        var remaining = Hydration.NextReminder - DateTime.Now;
        TimerText.Text = remaining > TimeSpan.Zero ? TimerFormat.FormatRemaining(remaining) : "Drink up!";
        TimerCaption.Text = $"Water · {count}/{goal}";

        Ring.Maximum = goal;
        Ring.Value = Math.Min(count, goal);
        Ring.SetResourceReference(Controls.RingGauge.FillProperty, count >= goal ? "AccentGreenBrush" : "AccentCyanBrush");
        CountRun.Text = count.ToString();
        GoalRun.Text = $"/{goal}";
        ProgressCaption.Text = count >= goal ? "goal reached" : $"glasses · {liters:0.0#} L";

        string reminders = _settings.NotificationsEnabled
            ? $"Reminder every {_settings.IntervalMinutes}m ({_settings.StartHour:00}:00–{_settings.EndHour:00}:00)"
            : "Reminders off";
        ToolTip = $"Today {count}/{goal} glasses ({liters:0.0#} L)\n{reminders}\nClick: +1 glass · Right-click: options";
        RefreshCompact();
    }

    protected override void UpdateCompact(CompactTile tile)
    {
        int count = Hydration.Count;
        int goal = _settings.DailyGoal;
        if (Variant == "timer")
        {
            var remaining = Hydration.NextReminder - DateTime.Now;
            tile.ShowGlyph(Descriptor.Icon, "TextOnColorBrush");
            tile.SetTextBrushKey("TextOnColorBrush");
            tile.Text = remaining > TimeSpan.Zero ? TimerFormat.FormatRemaining(remaining) : "Drink!";
        }
        else
        {
            tile.ShowRing(Math.Min(count, goal), goal, count >= goal ? "AccentGreenBrush" : "AccentCyanBrush", "TrackBrush", count.ToString());
            tile.SetTextBrushKey("TextPrimaryBrush");
            tile.Text = $"{count}/{goal}";
        }
    }

    public override bool OnCompactClick()
    {
        Hydration.AddGlass();
        return true;
    }

    private void OnClick(object sender, MouseButtonEventArgs e)
    {
        if (IsPreview) return;
        Hydration.AddGlass();
        var pop = new DoubleAnimation(1.12, 1, TimeSpan.FromMilliseconds(320))
        {
            EasingFunction = new ElasticEase { Oscillations = 1, Springiness = 4, EasingMode = EasingMode.EaseOut },
        };
        var scale = Variant == "timer" ? TimerScale : RingScale;
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
    }

    public override void AddContextMenuItems(ItemCollection items)
    {
        items.Add(DockMenu.Item("Drank a glass", "\uE710", Hydration.AddGlass));
        items.Add(DockMenu.Item("Remove a glass", "\uE738", Hydration.RemoveGlass, Hydration.Count > 0));
        items.Add(DockMenu.Item("Reset today", "\uE72C", Hydration.Reset));
        items.Add(DockMenu.Check("Reminder notifications", _settings.NotificationsEnabled,
            () => _settings.NotificationsEnabled = !_settings.NotificationsEnabled));
    }
}
