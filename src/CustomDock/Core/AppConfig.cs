using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace CustomDock.Core;

public enum DockEdge { Bottom, Top, Left, Right }

public enum TaskbarMode
{
    /// <summary>DockHub replaces the Windows taskbar completely (taskbar is hidden).</summary>
    Replace,
    /// <summary>Windows taskbar also remains visible.</summary>
    ShowBoth,
}

public enum ThemePreference { Dark, Light, System }

public enum BackdropKind
{
    /// <summary>Blurred glass (runs on inactive windows too, fluid).</summary>
    Blur,
    /// <summary>Windows Acrylic texture (grain + tint).</summary>
    Acrylic,
    /// <summary>No blur, solid color.</summary>
    Solid,
}

public enum DockSize { Small, Medium, Large }

public enum DockLayout
{
    /// <summary>Floating bar with margins and rounded corners.</summary>
    Floating,
    /// <summary>Classic taskbar attached to screen edge.</summary>
    Attached,
}

public enum DockWidthMode
{
    /// <summary>Spans full screen width.</summary>
    Full,
    /// <summary>Fits content width, centered.</summary>
    Fit,
}

public enum DockAlignment { Start, Center }

public enum DockItemKind { App, Widget, Separator }

/// <summary>%AppData%\DockHub\config.json content.</summary>
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

    // ---------------- General / Taskbar

    public TaskbarMode TaskbarMode { get => _taskbarMode; set => Set(ref _taskbarMode, value); }

    public bool HideOnFullscreen { get => _hideOnFullscreen; set => Set(ref _hideOnFullscreen, value); }

    public bool StartWithWindows { get => _startWithWindows; set => Set(ref _startWithWindows, value); }

    private bool _explorerPinMenu = true;

    /// <summary>Adds "Pin to DockHub" to Explorer .exe/.lnk right-click context menu.</summary>
    public bool ExplorerPinMenu { get => _explorerPinMenu; set => Set(ref _explorerPinMenu, value); }

    public bool ShowStartButton { get => _showStartButton; set => Set(ref _showStartButton, value); }

    public bool ShowSearchButton { get => _showSearchButton; set => Set(ref _showSearchButton, value); }

    public bool ShowTaskViewButton { get => _showTaskViewButton; set => Set(ref _showTaskViewButton, value); }

    /// <summary>Shows unpinned running apps at the end of the dock.</summary>
    public bool ShowRunningApps { get => _showRunningApps; set => Set(ref _showRunningApps, value); }

    public bool ShowTray { get => _showTray; set => Set(ref _showTray, value); }

    public bool ShowClock { get => _showClock; set => Set(ref _showClock, value); }

    public bool ClockShowDate { get => _clockShowDate; set => Set(ref _clockShowDate, value); }

    public bool ClockShowSeconds { get => _clockShowSeconds; set => Set(ref _clockShowSeconds, value); }

    public bool ShowDesktopButton { get => _showDesktopButton; set => Set(ref _showDesktopButton, value); }

    /// <summary>IDs of tray icons that are always shown in the dock (null = import from Windows settings).</summary>
    public List<string>? PinnedTrayIcons { get; set; }

    /// <summary>Previously seen tray icons (new ones are pinned once based on Windows settings).</summary>
    public List<string> KnownTrayIcons { get; set; } = new();

    // ---------------- Appearance / Layout

    public DockEdge Edge { get => _edge; set => Set(ref _edge, value); }

    /// <summary>Monitor device name (e.g. \\.\DISPLAY2). null = primary monitor.</summary>
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

    // ---------------- Items

    /// <summary>Dock items (app, widget, separator), from left to right.</summary>
    public List<DockItem> Items { get; set; } = new();

    // ---------------- v1 compatibility (read-only, not written back after migration)

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<LegacyWidgetEntry>? Widgets { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, JsonObject>? WidgetSettings { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ReserveSpace { get; set; }

    [JsonIgnore]
    public bool IsFirstRun { get; set; }

    /// <summary>Fired when the items list changes (add, remove, reorder).</summary>
    public event EventHandler? ItemsChanged;

    public void NotifyItemsChanged() => ItemsChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>Represents a single dock item.</summary>
public sealed class DockItem : ObservableObject
{
    private string? _variant;
    private string? _name;

    public string Id { get; set; } = NewId();

    public DockItemKind Kind { get; set; }

    // ---- App

    /// <summary>.exe, .lnk, folder, URI, or shell:AppsFolder\AUMID.</summary>
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
