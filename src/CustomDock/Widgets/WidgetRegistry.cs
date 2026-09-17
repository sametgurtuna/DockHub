using System.Windows;
using System.Windows.Media;
using CustomDock.Core;
using CustomDock.Services;

namespace CustomDock.Widgets;

public sealed record WidgetVariant(string Id, string Name);

/// <summary>Bir widget türünün meta verisi, varyantları ve fabrika metotları.</summary>
public sealed class WidgetDescriptor
{
    private Geometry? _icon;

    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string Category { get; init; }

    public required string Description { get; init; }

    /// <summary>24×24 kutuda çizgi olarak çizilen ikon.</summary>
    public required string IconPath { get; init; }

    public string AccentKey { get; init; } = "AccentBlueBrush";

    public required IReadOnlyList<WidgetVariant> Variants { get; init; }

    public string DefaultVariant => Variants[0].Id;

    public required Func<WidgetBase> Factory { get; init; }

    /// <summary>Öğe başına ayar sınıfı (yoksa null).</summary>
    public Type? SettingsType { get; init; }

    /// <summary>Özel ayar görünümü. null ise Settings/WidgetSettingsTemplates.xaml içindeki DataTemplate kullanılır.</summary>
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
    public const string Clocks = "Saatler";
    public const string Reminders = "Hatırlatıcılar";
    public const string Notes = "Yapışkan notlar";
    public const string Media = "Medya";
    public const string System = "Sistem";
    public const string Weather = "Hava durumu";

    public static readonly string[] Ordered = { Clocks, Reminders, Notes, Media, System, Weather };
}

/// <summary>
/// Kullanılabilir tüm widget'lar. Yeni widget eklemek için:
/// 1) WidgetBase'den türeyen bir UserControl yazın,
/// 2) (isteğe bağlı) ObservableObject'ten türeyen bir ayar sınıfı ve DataTemplate ekleyin,
/// 3) Aşağıdaki listeye bir WidgetDescriptor ekleyin.
/// </summary>
public static class WidgetRegistry
{
    private const string ClockIcon = "M12,3 A9,9 0 1 1 11.99,3 Z M12,7 V12 L15,14";

    public static IReadOnlyList<WidgetDescriptor> All { get; } = new List<WidgetDescriptor>
    {
        // ------------------------------------------------ Saatler
        new()
        {
            Id = "clock", Name = "Saat", Category = WidgetCategories.Clocks,
            Description = "Saat ve tarih. Analog, dijital veya takvim görünümü; takvim görünümü sıradaki hatırlatıcıyı da gösterir.",
            IconPath = ClockIcon, AccentKey = "AccentOrangeBrush",
            Variants = new[] { new WidgetVariant("analog", "Analog"), new WidgetVariant("digital", "Dijital"), new WidgetVariant("calendar", "Takvim") },
            Factory = () => new ClockWidget(), SettingsType = typeof(ClockSettings),
        },
        new()
        {
            Id = "world-clock", Name = "Dünya saati", Category = WidgetCategories.Clocks,
            Description = "Farklı şehirlerin saatleri.",
            IconPath = "M12,3 A9,9 0 1 1 11.99,3 Z M3,12 H21 M12,3 C15,6 15,18 12,21 C9,18 9,6 12,3 Z", AccentKey = "AccentBlueBrush",
            Variants = new[] { new WidgetVariant("single", "Tek şehir"), new WidgetVariant("multi", "Çoklu şehir") },
            Factory = () => new WorldClockWidget(), SettingsType = typeof(WorldClockSettings),
            SettingsViewFactory = s => new WorldClockSettingsView((WorldClockSettings)s),
        },
        new()
        {
            Id = "stopwatch", Name = "Kronometre", Category = WidgetCategories.Clocks,
            Description = "Tıklayarak başlatın/durdurun, sağ tıkla sıfırlayın.",
            IconPath = "M12,5 A8,8 0 1 1 11.99,5 Z M12,9 V13 M10,2 H14 M19,6 L20.5,4.5", AccentKey = "AccentOrangeBrush",
            Variants = new[] { new WidgetVariant("default", "Kronometre") },
            Factory = () => new StopwatchWidget(),
        },
        new()
        {
            Id = "focus", Name = "Odak zamanlayıcı", Category = WidgetCategories.Clocks,
            Description = "Pomodoro tarzı odak ve mola zamanlayıcısı; süre dolunca bildirim gönderir.",
            IconPath = "M12,3 A9,9 0 1 1 11.99,3 Z M12,6 A6,6 0 0 1 18,12", AccentKey = "AccentOrangeBrush",
            Variants = new[] { new WidgetVariant("default", "Odak zamanlayıcı") },
            Factory = () => new FocusWidget(), SettingsType = typeof(FocusSettings),
        },
        new()
        {
            Id = "countdown", Name = "Geri sayım", Category = WidgetCategories.Clocks,
            Description = "Hazır sürelerle geri sayım; bitince bildirim gönderir.",
            IconPath = "M12,5 A8,8 0 1 1 11.99,5 Z M12,13 L15,10 M10,2 H14", AccentKey = "AccentYellowBrush",
            Variants = new[] { new WidgetVariant("default", "Geri sayım") },
            Factory = () => new CountdownWidget(), SettingsType = typeof(CountdownSettings),
        },
        new()
        {
            Id = "alarm", Name = "Alarm", Category = WidgetCategories.Clocks,
            Description = "Belirli bir saatte (isteğe bağlı her gün) bildirim.",
            IconPath = "M12,6 A7,7 0 1 1 11.99,6 Z M12,9 V13 L14,14 M4,5 L7,2.5 M20,5 L17,2.5", AccentKey = "AccentRedBrush",
            Variants = new[] { new WidgetVariant("default", "Alarm") },
            Factory = () => new AlarmWidget(), SettingsType = typeof(AlarmSettings),
        },
        new()
        {
            Id = "time-progress", Name = "Zaman ilerlemesi", Category = WidgetCategories.Clocks,
            Description = "Günün, haftanın, ayın veya yılın ne kadarının geçtiği.",
            IconPath = "M3,8 H21 V16 H3 Z M6,8 V16 M9,8 V16 M12,8 V16", AccentKey = "AccentPurpleBrush",
            Variants = new[] { new WidgetVariant("bar", "Çubuk"), new WidgetVariant("ring", "Halka") },
            Factory = () => new TimeProgressWidget(), SettingsType = typeof(TimeProgressSettings),
        },

        // ------------------------------------------------ Hatırlatıcılar
        new()
        {
            Id = "hydration", Name = "Su içme", Category = WidgetCategories.Reminders,
            Description = "Bir sonraki su hatırlatmasına geri sayım ve günlük hedef. Tıklayınca bir bardak eklenir.",
            IconPath = "M12,3 C12,3 5.5,10 5.5,14.5 A6.5,6.5 0 0 0 18.5,14.5 C18.5,10 12,3 12,3 Z", AccentKey = "AccentCyanBrush",
            Variants = new[] { new WidgetVariant("timer", "Zamanlayıcı"), new WidgetVariant("progress", "Günlük hedef") },
            Factory = () => new HydrationWidget(), SettingsType = typeof(HydrationSettings),
        },
        new()
        {
            Id = "reminders", Name = "Hatırlatıcılar", Category = WidgetCategories.Reminders,
            Description = "Metin ve saat girerek hatırlatıcı ekleyin; zamanı gelince Windows bildirimi gösterilir.",
            IconPath = "M8,6 H20 M8,12 H20 M8,18 H20 M4,6 H4.5 M4,12 H4.5 M4,18 H4.5", AccentKey = "AccentBlueBrush",
            Variants = new[] { new WidgetVariant("list", "Liste"), new WidgetVariant("next", "Sıradaki"), new WidgetVariant("count", "Sayı") },
            Factory = () => new RemindersWidget(),
        },

        // ------------------------------------------------ Notlar
        new()
        {
            Id = "notes", Name = "Yapışkan not", Category = WidgetCategories.Notes,
            Description = "Renkli, kendiliğinden kaydedilen not kağıdı.",
            IconPath = "M5,4 H19 V14 L14,20 H5 Z M14,20 V14 H19", AccentKey = "AccentYellowBrush",
            Variants = new[] { new WidgetVariant("sticky", "Yapışkan not") },
            Factory = () => new NotesWidget(), SettingsType = typeof(NotesSettings),
        },

        // ------------------------------------------------ Medya
        new()
        {
            Id = "media", Name = "Şu an çalıyor", Category = WidgetCategories.Media,
            Description = "Spotify, YouTube Music, tarayıcı vb. oynatıcılardan şarkı bilgisi ve kontroller (Windows SMTC).",
            IconPath = "M9,17 V5 L20,3 V15 M9,17 A2.5,2.5 0 1 1 4,17 A2.5,2.5 0 1 1 9,17 Z M20,15 A2.5,2.5 0 1 1 15,15 A2.5,2.5 0 1 1 20,15 Z", AccentKey = "AccentPinkBrush",
            Variants = new[] { new WidgetVariant("full", "Tam"), new WidgetVariant("compact", "Kompakt"), new WidgetVariant("mini", "Mini") },
            Factory = () => new MediaWidget(), SettingsType = typeof(MediaSettings),
        },

        // ------------------------------------------------ Sistem
        new()
        {
            Id = "system", Name = "CPU ve bellek", Category = WidgetCategories.System,
            Description = "Canlı işlemci ve bellek kullanımı.",
            IconPath = "M3,12 H7 L10,4 L14,20 L17,12 H21", AccentKey = "AccentMagentaBrush",
            Variants = new[] { new WidgetVariant("numbers", "Sayılar"), new WidgetVariant("rings", "Halkalar"), new WidgetVariant("bars", "Çubuklar") },
            Factory = () => new SystemWidget(), SettingsType = typeof(SystemSettings),
        },
        new()
        {
            Id = "network", Name = "Ağ hızı", Category = WidgetCategories.System,
            Description = "Anlık indirme ve yükleme hızı.",
            IconPath = "M8,4 V20 M4,16 L8,20 L12,16 M16,20 V4 M12,8 L16,4 L20,8", AccentKey = "AccentBlueBrush",
            Variants = new[] { new WidgetVariant("numbers", "Yalnızca sayılar"), new WidgetVariant("chart", "Grafikli") },
            Factory = () => new NetworkWidget(),
        },
        new()
        {
            Id = "status", Name = "Durum", Category = WidgetCategories.System,
            Description = "Pil, disk, bellek ve işlemci doluluğu halkaları.",
            IconPath = "M12,4 A8,8 0 1 1 11.99,4 Z M12,8 A4,4 0 1 1 11.99,8 Z", AccentKey = "AccentGreenBrush",
            Variants = new[] { new WidgetVariant("rings", "Halkalar"), new WidgetVariant("percent", "Yüzde halkası"), new WidgetVariant("icons", "Yalnızca ikon") },
            Factory = () => new StatusWidget(), SettingsType = typeof(StatusSettings),
        },

        // ------------------------------------------------ Hava durumu
        new()
        {
            Id = "weather", Name = "Hava durumu", Category = WidgetCategories.Weather,
            Description = "Open-Meteo'dan güncel ve saatlik hava durumu (API anahtarı gerekmez).",
            IconPath = "M17.5,19 H8 A5,5 0 1 1 9.6,9.3 A6,6 0 0 1 20.8,11.6 A3.8,3.8 0 0 1 17.5,19 Z", AccentKey = "AccentCyanBrush",
            Variants = new[] { new WidgetVariant("current", "Güncel"), new WidgetVariant("conditions", "Durum"), new WidgetVariant("hourly", "Saatlik tahmin") },
            Factory = () => new WeatherWidget(), SettingsType = typeof(WeatherSettings),
            SettingsViewFactory = s => new WeatherSettingsView((WeatherSettings)s),
        },
    };

    public static WidgetDescriptor? Find(string? id)
        => All.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));
}
