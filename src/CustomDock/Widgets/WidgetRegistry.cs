using System.Windows;
using System.Windows.Media;
using CustomDock.Core;
using CustomDock.Services;

namespace CustomDock.Widgets;

public sealed record WidgetVariant(string Id, string Name);

/// <summary>Metadata, variants, and factory methods for a widget type.</summary>
public sealed class WidgetDescriptor
{
    private Geometry? _icon;

    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string Category { get; init; }

    public required string Description { get; init; }

    /// <summary>Outline icon drawn in a 24×24 box.</summary>
    public required string IconPath { get; init; }

    public string AccentKey { get; init; } = "AccentBlueBrush";

    public required IReadOnlyList<WidgetVariant> Variants { get; init; }

    public string DefaultVariant => Variants[0].Id;

    public required Func<WidgetBase> Factory { get; init; }

    /// <summary>Per-item settings class (null if none).</summary>
    public Type? SettingsType { get; init; }

    /// <summary>Custom settings view. If null, the DataTemplate in Settings/WidgetSettingsTemplates.xaml is used.</summary>
    public Func<object, FrameworkElement>? SettingsViewFactory { get; init; }

    public Geometry Icon
    {
        get
        {
            if (_icon is null)
            {
                _icon = Geometry.Parse(IconPath);
                _icon.Freeze();
            }
            return _icon;
        }
    }

    public string VariantName(string? variant) => Variants.FirstOrDefault(v => v.Id == variant)?.Name ?? Variants[0].Name;

    public WidgetBase Create(DockItem item)
    {
        var widget = Factory();
        widget.Descriptor = this;
        widget.Item = item;
        return widget;
    }
}

public static class WidgetCategories
{
    public const string Clocks = "Clocks";
    public const string Reminders = "Reminders";
    public const string Notes = "Sticky notes";
    public const string Media = "Media";
    public const string System = "System";
    public const string Weather = "Weather";
    public const string AI = "AI";

    public static readonly string[] Ordered = { Clocks, Reminders, Notes, Media, System, Weather, AI };
}

/// <summary>
/// All available widgets. To add a new widget:
/// 1) Create a UserControl derived from WidgetBase,
/// 2) (optional) Add a settings class derived from ObservableObject and a DataTemplate,
/// 3) Add a WidgetDescriptor to the list below.
/// </summary>
public static class WidgetRegistry
{
    private const string ClockIcon = "M12,3 A9,9 0 1 1 11.99,3 Z M12,7 V12 L15,14";

    public static IReadOnlyList<WidgetDescriptor> All { get; } = new List<WidgetDescriptor>
    {
        // ------------------------------------------------ Clocks
        new()
        {
            Id = "clock", Name = "Clock", Category = WidgetCategories.Clocks,
            Description = "Clock and date. Analog, digital, or calendar view; calendar view also displays the next reminder.",
            IconPath = ClockIcon, AccentKey = "AccentOrangeBrush",
            Variants = new[] { new WidgetVariant("analog", "Analog"), new WidgetVariant("digital", "Digital"), new WidgetVariant("calendar", "Calendar") },
            Factory = () => new ClockWidget(), SettingsType = typeof(ClockSettings),
        },
        new()
        {
            Id = "world-clock", Name = "World clock", Category = WidgetCategories.Clocks,
            Description = "Clocks for different cities.",
            IconPath = "M12,3 A9,9 0 1 1 11.99,3 Z M3,12 H21 M12,3 C15,6 15,18 12,21 C9,18 9,6 12,3 Z", AccentKey = "AccentBlueBrush",
            Variants = new[] { new WidgetVariant("single", "Single city"), new WidgetVariant("multi", "Multiple cities") },
            Factory = () => new WorldClockWidget(), SettingsType = typeof(WorldClockSettings),
            SettingsViewFactory = s => new WorldClockSettingsView((WorldClockSettings)s),
        },
        new()
        {
            Id = "stopwatch", Name = "Stopwatch", Category = WidgetCategories.Clocks,
            Description = "Click to start/stop, right-click to reset.",
            IconPath = "M12,5 A8,8 0 1 1 11.99,5 Z M12,9 V13 M10,2 H14 M19,6 L20.5,4.5", AccentKey = "AccentOrangeBrush",
            Variants = new[] { new WidgetVariant("default", "Stopwatch") },
            Factory = () => new StopwatchWidget(),
        },
        new()
        {
            Id = "focus", Name = "Focus timer", Category = WidgetCategories.Clocks,
            Description = "Pomodoro-style focus and break timer; sends notifications when time expires.",
            IconPath = "M12,3 A9,9 0 1 1 11.99,3 Z M12,6 A6,6 0 0 1 18,12", AccentKey = "AccentOrangeBrush",
            Variants = new[] { new WidgetVariant("default", "Focus timer") },
            Factory = () => new FocusWidget(), SettingsType = typeof(FocusSettings),
        },
        new()
        {
            Id = "countdown", Name = "Countdown", Category = WidgetCategories.Clocks,
            Description = "Countdown with presets; sends notification when finished.",
            IconPath = "M12,5 A8,8 0 1 1 11.99,5 Z M12,13 L15,10 M10,2 H14", AccentKey = "AccentYellowBrush",
            Variants = new[] { new WidgetVariant("default", "Countdown") },
            Factory = () => new CountdownWidget(), SettingsType = typeof(CountdownSettings),
        },
        new()
        {
            Id = "alarm", Name = "Alarm", Category = WidgetCategories.Clocks,
            Description = "Notification at a specific time (optional daily repeat).",
            IconPath = "M12,6 A7,7 0 1 1 11.99,6 Z M12,9 V13 L14,14 M4,5 L7,2.5 M20,5 L17,2.5", AccentKey = "AccentRedBrush",
            Variants = new[] { new WidgetVariant("default", "Alarm") },
            Factory = () => new AlarmWidget(), SettingsType = typeof(AlarmSettings),
        },
        new()
        {
            Id = "time-progress", Name = "Time progress", Category = WidgetCategories.Clocks,
            Description = "Elapsed progress of the day, week, month, or year.",
            IconPath = "M3,8 H21 V16 H3 Z M6,8 V16 M9,8 V16 M12,8 V16", AccentKey = "AccentPurpleBrush",
            Variants = new[] { new WidgetVariant("bar", "Bar"), new WidgetVariant("ring", "Ring") },
            Factory = () => new TimeProgressWidget(), SettingsType = typeof(TimeProgressSettings),
        },

        // ------------------------------------------------ Reminders
        new()
        {
            Id = "hydration", Name = "Hydration", Category = WidgetCategories.Reminders,
            Description = "Countdown to next water reminder and daily goal. Click to add a glass.",
            IconPath = "M12,3 C12,3 5.5,10 5.5,14.5 A6.5,6.5 0 0 0 18.5,14.5 C18.5,10 12,3 12,3 Z", AccentKey = "AccentCyanBrush",
            Variants = new[] { new WidgetVariant("timer", "Timer"), new WidgetVariant("progress", "Daily goal") },
            Factory = () => new HydrationWidget(), SettingsType = typeof(HydrationSettings),
        },
        new()
        {
            Id = "reminders", Name = "Reminders", Category = WidgetCategories.Reminders,
            Description = "Add reminders with text and time; shows a Windows notification when due.",
            IconPath = "M8,6 H20 M8,12 H20 M8,18 H20 M4,6 H4.5 M4,12 H4.5 M4,18 H4.5", AccentKey = "AccentBlueBrush",
            Variants = new[] { new WidgetVariant("list", "List"), new WidgetVariant("next", "Next"), new WidgetVariant("count", "Count") },
            Factory = () => new RemindersWidget(),
        },

        // ------------------------------------------------ Notes
        new()
        {
            Id = "notes", Name = "Sticky note", Category = WidgetCategories.Notes,
            Description = "Color-customizable, auto-saving sticky note.",
            IconPath = "M5,4 H19 V14 L14,20 H5 Z M14,20 V14 H19", AccentKey = "AccentYellowBrush",
            Variants = new[] { new WidgetVariant("sticky", "Sticky note") },
            Factory = () => new NotesWidget(), SettingsType = typeof(NotesSettings),
        },

        // ------------------------------------------------ Media
        new()
        {
            Id = "media", Name = "Now playing", Category = WidgetCategories.Media,
            Description = "Track info and controls from Spotify, YouTube Music, browsers, etc. (Windows SMTC).",
            IconPath = "M9,17 V5 L20,3 V15 M9,17 A2.5,2.5 0 1 1 4,17 A2.5,2.5 0 1 1 9,17 Z M20,15 A2.5,2.5 0 1 1 15,15 A2.5,2.5 0 1 1 20,15 Z", AccentKey = "AccentPinkBrush",
            Variants = new[] { new WidgetVariant("full", "Full"), new WidgetVariant("compact", "Compact"), new WidgetVariant("mini", "Mini") },
            Factory = () => new MediaWidget(), SettingsType = typeof(MediaSettings),
        },

        // ------------------------------------------------ System
        new()
        {
            Id = "system", Name = "CPU and memory", Category = WidgetCategories.System,
            Description = "Live CPU and memory usage.",
            IconPath = "M3,12 H7 L10,4 L14,20 L17,12 H21", AccentKey = "AccentMagentaBrush",
            Variants = new[] { new WidgetVariant("numbers", "Numbers"), new WidgetVariant("rings", "Rings"), new WidgetVariant("bars", "Bars") },
            Factory = () => new SystemWidget(), SettingsType = typeof(SystemSettings),
        },
        new()
        {
            Id = "network", Name = "Network speed", Category = WidgetCategories.System,
            Description = "Real-time download and upload speeds.",
            IconPath = "M8,4 V20 M4,16 L8,20 L12,16 M16,20 V4 M12,8 L16,4 L20,8", AccentKey = "AccentBlueBrush",
            Variants = new[] { new WidgetVariant("numbers", "Numbers only"), new WidgetVariant("chart", "Chart") },
            Factory = () => new NetworkWidget(),
        },
        new()
        {
            Id = "status", Name = "Status", Category = WidgetCategories.System,
            Description = "Battery, disk, memory, and CPU usage rings.",
            IconPath = "M12,4 A8,8 0 1 1 11.99,4 Z M12,8 A4,4 0 1 1 11.99,8 Z", AccentKey = "AccentGreenBrush",
            Variants = new[] { new WidgetVariant("rings", "Rings"), new WidgetVariant("percent", "Percentage ring"), new WidgetVariant("icons", "Icon only") },
            Factory = () => new StatusWidget(), SettingsType = typeof(StatusSettings),
        },

        // ------------------------------------------------ Weather
        new()
        {
            Id = "weather", Name = "Weather", Category = WidgetCategories.Weather,
            Description = "Current and hourly weather from Open-Meteo (no API key required).",
            IconPath = "M17.5,19 H8 A5,5 0 1 1 9.6,9.3 A6,6 0 0 1 20.8,11.6 A3.8,3.8 0 0 1 17.5,19 Z", AccentKey = "AccentCyanBrush",
            Variants = new[] { new WidgetVariant("current", "Current"), new WidgetVariant("conditions", "Conditions"), new WidgetVariant("hourly", "Hourly forecast") },
            Factory = () => new WeatherWidget(), SettingsType = typeof(WeatherSettings),
            SettingsViewFactory = s => new WeatherSettingsView((WeatherSettings)s),
        },

        // ------------------------------------------------ AI
        new()
        {
            Id = "ai-usage", Name = "AI usage", Category = WidgetCategories.AI,
            Description = "Claude Code subscription usage: 5-hour and weekly limits (updates in background every 5 minutes via 'claude -p /usage').",
            IconPath = "M12,3 L14.2,9.2 L20.8,9.2 L15.5,13.1 L17.5,19.3 L12,15.6 L6.5,19.3 L8.5,13.1 L3.2,9.2 L9.8,9.2 Z", AccentKey = "AccentOrangeBrush",
            Variants = new[] { new WidgetVariant("numbers", "Numbers"), new WidgetVariant("rings", "Rings"), new WidgetVariant("bars", "Bars") },
            Factory = () => new AIUsageWidget(),
        },

        // ------------------------------------------------ Audio and Hardware
        new()
        {
            Id = "audio", Name = "Audio device", Category = WidgetCategories.Media,
            Description = "Quick switch between headphones and speakers, volume adjustment with mouse wheel, and mute.",
            IconPath = "M12,3 A9,9 0 0 0 3,12 V18 A3,3 0 0 0 6,21 H7 A2,2 0 0 0 9,19 V15 A2,2 0 0 0 7,13 H5 V12 A7,7 0 0 1 19,12 V13 H17 A2,2 0 0 0 15,15 V19 A2,2 0 0 0 17,21 H18 A3,3 0 0 0 21,18 V12 A9,9 0 0 0 12,3 Z", AccentKey = "AccentCyanBrush",
            Variants = new[] { new WidgetVariant("compact", "Compact"), new WidgetVariant("slider", "Slider") },
            Factory = () => new AudioWidget(),
        },
        new()
        {
            Id = "recycle-bin", Name = "Recycle bin", Category = WidgetCategories.System,
            Description = "macOS-style recycle bin. Drag and drop files to delete, click to open, or right-click to empty.",
            IconPath = "M8,5 H16 M3,6 H21 M5,6 V19 A2,2 0 0 0 7,21 H17 A2,2 0 0 0 19,19 V6 M10,10 V17 M14,10 V17", AccentKey = "AccentBlueBrush",
            Variants = new[] { new WidgetVariant("icon", "Icon only"), new WidgetVariant("details", "Detailed") },
            Factory = () => new RecycleBinWidget(),
        },
        new()
        {
            Id = "battery-devices", Name = "Device batteries", Category = WidgetCategories.System,
            Description = "Battery levels for HyperX Cloud II Wireless USB dongle and connected Bluetooth headphones, mice, and keyboards.",
            IconPath = "M4,7 H18 A2,2 0 0 1 20,9 V15 A2,2 0 0 1 18,17 H4 A2,2 0 0 1 2,15 V9 A2,2 0 0 1 4,7 Z M20,11 H22 V13 H20 Z", AccentKey = "AccentGreenBrush",
            Variants = new[] { new WidgetVariant("single", "Single device"), new WidgetVariant("multi", "Multiple devices") },
            Factory = () => new BatteryDevicesWidget(),
        },
    };

    public static WidgetDescriptor? Find(string? id)
        => All.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));
}
