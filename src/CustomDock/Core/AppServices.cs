using System.Windows;
using CustomDock.Services;
using CustomDock.Shell;

namespace CustomDock.Core;

/// <summary>
/// Application-wide shared services. Each one is created the first time something uses it (a widget that isn't on
/// the dock costs nothing), and always on the UI thread, where its timers and COM objects belong.
/// </summary>
public static class AppServices
{
    /// <summary>A service created on first use. Creation is marshalled to the UI thread, so it never races.</summary>
    private sealed class OnFirstUse<T>(Func<T> create) where T : class
    {
        private T? _value;

        public bool IsCreated => _value is not null;

        public T Value
        {
            get
            {
                if (_value is { } value) return value;
                if (Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
                    return dispatcher.Invoke(() => Value);
                return _value ??= create();
            }
        }

        public T? IfCreated => _value;
    }

    public static ConfigService ConfigService { get; } = new();

    public static AppConfig Config => ConfigService.Config;

    public static ProfileService Profiles { get; } = new(ConfigService);

    /// <summary>Shell services (task list, system tray, taskbar). Initialized on startup.</summary>
    public static ShellHost Shell { get; set; } = null!;

    private static readonly OnFirstUse<ClockService> s_clock = new(() => new ClockService());
    private static readonly OnFirstUse<SystemMonitorService> s_systemMonitor = new(() => new SystemMonitorService());
    private static readonly OnFirstUse<NetworkMonitorService> s_network = new(() => new NetworkMonitorService());
    private static readonly OnFirstUse<NetworkStatusService> s_networkStatus = new(() => new NetworkStatusService());
    private static readonly OnFirstUse<MediaService> s_media = new(() => new MediaService());
    private static readonly OnFirstUse<WeatherService> s_weather = new(() => new WeatherService());
    private static readonly OnFirstUse<NotificationService> s_notifications = new(() => new NotificationService());
    private static readonly OnFirstUse<ReminderService> s_reminders = new(() => new ReminderService());
    private static readonly OnFirstUse<HydrationService> s_hydration = new(() => new HydrationService());
    private static readonly OnFirstUse<AIUsageService> s_aiUsage = new(() => new AIUsageService());
    private static readonly OnFirstUse<CodexUsageService> s_codexUsage = new(() => new CodexUsageService());
    private static readonly OnFirstUse<GeminiUsageService> s_geminiUsage = new(() => new GeminiUsageService());
    private static readonly OnFirstUse<AudioService> s_audio = new(() => new AudioService());
    private static readonly OnFirstUse<RecycleBinService> s_recycleBin = new(() => new RecycleBinService());
    private static readonly OnFirstUse<DeviceBatteryService> s_deviceBattery = new(() => new DeviceBatteryService());
    private static readonly OnFirstUse<UpdateService> s_updates = new(() => new UpdateService());
    private static readonly OnFirstUse<ClipboardHistoryService> s_clipboard = new(() => new ClipboardHistoryService());
    private static readonly OnFirstUse<KeyboardLayoutService> s_keyboardLayouts = new(() => new KeyboardLayoutService());
    private static readonly OnFirstUse<MicrophoneService> s_microphone = new(() => new MicrophoneService());
    private static readonly OnFirstUse<NotificationCenterService> s_notificationCenter = new(() => new NotificationCenterService());
    private static readonly OnFirstUse<VirtualDesktopService> s_virtualDesktops = new(() => new VirtualDesktopService());
    private static readonly OnFirstUse<GpuMonitorService> s_gpu = new(() => new GpuMonitorService());
    private static readonly OnFirstUse<BrightnessService> s_brightness = new(() => new BrightnessService());
    private static readonly OnFirstUse<RadioService> s_radios = new(() => new RadioService());
    private static readonly OnFirstUse<PingService> s_ping = new(() => new PingService());
    private static readonly OnFirstUse<PowerModeService> s_powerMode = new(() => new PowerModeService());

    public static ClockService Clock => s_clock.Value;

    public static SystemMonitorService SystemMonitor => s_systemMonitor.Value;

    public static NetworkMonitorService Network => s_network.Value;

    public static NetworkStatusService NetworkStatus => s_networkStatus.Value;

    public static MediaService Media => s_media.Value;

    public static WeatherService Weather => s_weather.Value;

    public static NotificationService Notifications => s_notifications.Value;

    public static ReminderService Reminders => s_reminders.Value;

    public static HydrationService Hydration => s_hydration.Value;

    public static AIUsageService AIUsage => s_aiUsage.Value;

    public static CodexUsageService CodexUsage => s_codexUsage.Value;

    public static GeminiUsageService GeminiUsage => s_geminiUsage.Value;

    public static AudioService Audio => s_audio.Value;

    public static RecycleBinService RecycleBin => s_recycleBin.Value;

    public static DeviceBatteryService DeviceBattery => s_deviceBattery.Value;

    public static UpdateService Updates => s_updates.Value;

    public static ClipboardHistoryService Clipboard => s_clipboard.Value;

    public static KeyboardLayoutService KeyboardLayouts => s_keyboardLayouts.Value;

    public static MicrophoneService Microphone => s_microphone.Value;

    public static NotificationCenterService NotificationCenter => s_notificationCenter.Value;

    public static VirtualDesktopService VirtualDesktops => s_virtualDesktops.Value;

    public static GpuMonitorService Gpu => s_gpu.Value;

    public static BrightnessService Brightness => s_brightness.Value;

    public static RadioService Radios => s_radios.Value;

    public static PingService Ping => s_ping.Value;

    public static PowerModeService PowerMode => s_powerMode.Value;

    /// <summary>Names of the services created so far (for diagnostics).</summary>
    public static IEnumerable<string> CreatedServices()
    {
        var all = new (string Name, bool Created)[]
        {
            ("Clock", s_clock.IsCreated), ("SystemMonitor", s_systemMonitor.IsCreated), ("Network", s_network.IsCreated),
            ("NetworkStatus", s_networkStatus.IsCreated), ("Media", s_media.IsCreated), ("Weather", s_weather.IsCreated),
            ("Notifications", s_notifications.IsCreated), ("Reminders", s_reminders.IsCreated), ("Hydration", s_hydration.IsCreated),
            ("AIUsage", s_aiUsage.IsCreated), ("CodexUsage", s_codexUsage.IsCreated), ("GeminiUsage", s_geminiUsage.IsCreated),
            ("Audio", s_audio.IsCreated), ("RecycleBin", s_recycleBin.IsCreated), ("DeviceBattery", s_deviceBattery.IsCreated),
            ("Updates", s_updates.IsCreated), ("Clipboard", s_clipboard.IsCreated), ("KeyboardLayouts", s_keyboardLayouts.IsCreated),
            ("Microphone", s_microphone.IsCreated), ("NotificationCenter", s_notificationCenter.IsCreated),
            ("VirtualDesktops", s_virtualDesktops.IsCreated), ("Gpu", s_gpu.IsCreated), ("Brightness", s_brightness.IsCreated),
            ("Radios", s_radios.IsCreated), ("Ping", s_ping.IsCreated), ("PowerMode", s_powerMode.IsCreated),
        };
        return all.Where(s => s.Created).Select(s => s.Name);
    }

    /// <summary>Disposes the services that hold system resources, without creating any that were never used.</summary>
    public static void DisposeServices()
    {
        s_reminders.IfCreated?.Dispose();
        s_audio.IfCreated?.Dispose();
        s_clock.IfCreated?.Dispose();
    }
}
