using CustomDock.Services;
using CustomDock.Shell;

namespace CustomDock.Core;

/// <summary>Uygulama genelinde paylaşılan servisler. UI iş parçacığında erişilmelidir.</summary>
public static class AppServices
{
    public static ConfigService ConfigService { get; } = new();

    public static AppConfig Config => ConfigService.Config;

    /// <summary>Kabuk servisleri (görev listesi, sistem tepsisi, görev çubuğu). Başlangıçta oluşturulur.</summary>
    public static ShellHost Shell { get; set; } = null!;

    public static ClockService Clock { get; } = new();

    public static SystemMonitorService SystemMonitor { get; } = new();

    public static NetworkMonitorService Network { get; } = new();

    public static MediaService Media { get; } = new();

    public static WeatherService Weather { get; } = new();

    public static NotificationService Notifications { get; } = new();

    public static ReminderService Reminders { get; } = new();

    public static HydrationService Hydration { get; } = new();

    public static AIUsageService AIUsage { get; } = new();

    public static AudioService Audio { get; } = new();

    public static RecycleBinService RecycleBin { get; } = new();

    public static DeviceBatteryService DeviceBattery { get; } = new();
}
