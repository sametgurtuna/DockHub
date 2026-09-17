using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace CustomDock.Core;

public enum DockEdge { Bottom, Top, Left, Right }

public enum TaskbarMode
{
    /// <summary>DockHub, Windows görev çubuğunun yerini tamamen alır (görev çubuğu gizlenir).</summary>
    Replace,
    /// <summary>Windows görev çubuğu da görünür kalır.</summary>
    ShowBoth,
}

public enum ThemePreference { Dark, Light, System }

public enum BackdropKind
{
    /// <summary>Bulanık cam (pasif pencerede de çalışır, akıcıdır).</summary>
    Blur,
    /// <summary>Windows Acrylic dokusu (gren + ton).</summary>
    Acrylic,
    /// <summary>Bulanıklık yok, düz renk.</summary>
    Solid,
}

public enum DockSize { Small, Medium, Large }

public enum DockLayout
{
    /// <summary>Kenarlardan boşluklu, yuvarlak köşeli yüzen çubuk.</summary>
    Floating,
    /// <summary>Ekran kenarına yapışık, klasik görev çubuğu.</summary>
    Attached,
}

public enum DockWidthMode
{
    /// <summary>Ekran genişliği boyunca.</summary>
    Full,
    /// <summary>İçerik kadar, ortalanmış.</summary>
    Fit,
}

public enum DockAlignment { Start, Center }

public enum DockItemKind { App, Widget, Separator }

/// <summary>%AppData%\DockHub\config.json içeriği.</summary>
public sealed class AppConfig : ObservableObject
{
    public const int CurrentVersion = 2;

    private TaskbarMode _taskbarMode = TaskbarMode.Replace;
    private DockEdge _edge = DockEdge.Bottom;
    private string? _monitorDevice;
    private bool _autoHide;
    private bool _hideOnFullscreen = true;
    private bool _startWithWindows = true;
    private ThemePreference _theme = ThemePreference.Dark;
    private BackdropKind _backdrop = BackdropKind.Blur;
    private double _tintOpacity = 0.55;
    private DockSize _size = DockSize.Small;
    private DockLayout _layout = DockLayout.Floating;
    private DockWidthMode _widthMode = DockWidthMode.Full;
    private DockAlignment _alignment = DockAlignment.Center;
    private double _edgeMargin = 6;
    private bool _hoverEffect = true;
    private bool _showStartButton = true;
    private bool _showSearchButton = true;
    private bool _showTaskViewButton;
    private bool _showRunningApps = true;
    private bool _showTray = true;
    private bool _showClock = true;
    private bool _clockShowDate = true;
    private bool _clockShowSeconds;
    private bool _showDesktopButton = true;

    public int Version { get; set; } = CurrentVersion;

    // ---------------- Genel / görev çubuğu

    public TaskbarMode TaskbarMode { get => _taskbarMode; set => Set(ref _taskbarMode, value); }

    public bool HideOnFullscreen { get => _hideOnFullscreen; set => Set(ref _hideOnFullscreen, value); }

    public bool StartWithWindows { get => _startWithWindows; set => Set(ref _startWithWindows, value); }

    private bool _explorerPinMenu = true;

    /// <summary>Explorer'da .exe/.lnk sağ tık menüsüne "DockHub'a sabitle" ekler.</summary>
    public bool ExplorerPinMenu { get => _explorerPinMenu; set => Set(ref _explorerPinMenu, value); }

    public bool ShowStartButton { get => _showStartButton; set => Set(ref _showStartButton, value); }

    public bool ShowSearchButton { get => _showSearchButton; set => Set(ref _showSearchButton, value); }

    public bool ShowTaskViewButton { get => _showTaskViewButton; set => Set(ref _showTaskViewButton, value); }

    /// <summary>Sabitlenmemiş çalışan uygulamaları dock'un sonunda gösterir.</summary>
    public bool ShowRunningApps { get => _showRunningApps; set => Set(ref _showRunningApps, value); }

    public bool ShowTray { get => _showTray; set => Set(ref _showTray, value); }

    public bool ShowClock { get => _showClock; set => Set(ref _showClock, value); }

    public bool ClockShowDate { get => _clockShowDate; set => Set(ref _clockShowDate, value); }

    public bool ClockShowSeconds { get => _clockShowSeconds; set => Set(ref _clockShowSeconds, value); }

    public bool ShowDesktopButton { get => _showDesktopButton; set => Set(ref _showDesktopButton, value); }

    /// <summary>Her zaman dock'ta görünen tepsi ikonlarının kimlikleri (null = Windows ayarlarından içe aktar).</summary>
    public List<string>? PinnedTrayIcons { get; set; }

    /// <summary>Daha önce görülen tepsi ikonları (yenileri Windows ayarlarına göre bir kez sabitlenir).</summary>
    public List<string> KnownTrayIcons { get; set; } = new();

    // ---------------- Görünüm / yerleşim

    public DockEdge Edge { get => _edge; set => Set(ref _edge, value); }

    /// <summary>Monitör aygıt adı (ör. \\.\DISPLAY2). null = birincil ekran.</summary>
    public string? MonitorDevice { get => _monitorDevice; set => Set(ref _monitorDevice, value); }

    public bool AutoHide { get => _autoHide; set => Set(ref _autoHide, value); }

    public ThemePreference Theme { get => _theme; set => Set(ref _theme, value); }

    public BackdropKind Backdrop { get => _backdrop; set => Set(ref _backdrop, value); }

    public double TintOpacity { get => _tintOpacity; set => Set(ref _tintOpacity, Math.Clamp(value, 0, 1)); }

    public DockSize Size { get => _size; set => Set(ref _size, value); }

    public DockLayout Layout { get => _layout; set => Set(ref _layout, value); }

    public DockWidthMode WidthMode { get => _widthMode; set => Set(ref _widthMode, value); }

    public DockAlignment Alignment { get => _alignment; set => Set(ref _alignment, value); }

    public double EdgeMargin { get => _edgeMargin; set => Set(ref _edgeMargin, Math.Clamp(value, 0, 32)); }

    public bool HoverEffect { get => _hoverEffect; set => Set(ref _hoverEffect, value); }

    // ---------------- Öğeler

    /// <summary>Dock'taki öğeler (uygulama, widget, ayraç), soldan sağa.</summary>
    public List<DockItem> Items { get; set; } = new();

    // ---------------- v1 uyumluluğu (yalnızca okunur, taşıma sonrası yazılmaz)

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<LegacyWidgetEntry>? Widgets { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, JsonObject>? WidgetSettings { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ReserveSpace { get; set; }

    [JsonIgnore]
    public bool IsFirstRun { get; set; }

    /// <summary>Öğe listesi değiştiğinde (ekleme, silme, sıralama) tetiklenir.</summary>
    public event EventHandler? ItemsChanged;

    public void NotifyItemsChanged() => ItemsChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>Dock'taki tek bir öğe.</summary>
public sealed class DockItem : ObservableObject
{
    private string? _variant;
    private string? _name;

    public string Id { get; set; } = NewId();

    public DockItemKind Kind { get; set; }

    // ---- Uygulama

    /// <summary>.exe, .lnk, klasör, URI veya shell:AppsFolder\AUMID.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Path { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Arguments { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get => _name; set => Set(ref _name, value); }

    // ---- Widget

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Widget { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Variant { get => _variant; set => Set(ref _variant, value); }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonObject? Settings { get; set; }

    public static string NewId() => Guid.NewGuid().ToString("N")[..10];

    public static DockItem App(string path, string? name = null) => new() { Kind = DockItemKind.App, Path = path, Name = name };

    public static DockItem ForWidget(string widget, string? variant = null) => new() { Kind = DockItemKind.Widget, Widget = widget, Variant = variant };

    public static DockItem Separator() => new() { Kind = DockItemKind.Separator };
}

public sealed class LegacyWidgetEntry
{
    public string Id { get; set; } = "";
    public bool Enabled { get; set; }
}
