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

public enum DockItemKind { App, Widget, Separator, Group }

/// <summary>%AppData%\DockHub\config.json content.</summary>
public sealed class AppConfig : ObservableObject
{
    public const int CurrentVersion = 2;

    private TaskbarMode _taskbarMode = TaskbarMode.Replace;
    private DockEdge _edge = DockEdge.Bottom;
    private string? _monitorDevice;
    private bool _showOnAllDisplays;
    private bool _runningAppsOnOwnDisplay = true;
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

    private bool _showNetworkIcon = true;
    private bool _showVolumeIcon = true;
    private bool _showBatteryIcon = true;

    /// <summary>DockHub's own network / volume / battery icons next to the tray (Windows 11 keeps its own inside Explorer).</summary>
    public bool ShowNetworkIcon { get => _showNetworkIcon; set => Set(ref _showNetworkIcon, value); }

    public bool ShowVolumeIcon { get => _showVolumeIcon; set => Set(ref _showVolumeIcon, value); }

    /// <summary>Only shown on devices with a battery.</summary>
    public bool ShowBatteryIcon { get => _showBatteryIcon; set => Set(ref _showBatteryIcon, value); }

    /// <summary>IDs of tray icons that are always shown in the dock (null = import from Windows settings).</summary>
    public List<string>? PinnedTrayIcons { get; set; }

    /// <summary>Previously seen tray icons (new ones are pinned once based on Windows settings).</summary>
    public List<string> KnownTrayIcons { get; set; } = new();

    // ---------------- Appearance / Layout

    public DockEdge Edge { get => _edge; set => Set(ref _edge, value); }

    /// <summary>Monitor device name (e.g. \\.\DISPLAY2). null = primary monitor.</summary>
    public string? MonitorDevice { get => _monitorDevice; set => Set(ref _monitorDevice, value); }

    /// <summary>Shows a dock on every connected display. The main display (<see cref="MonitorDevice"/>) keeps widgets and tray icons.</summary>
    public bool ShowOnAllDisplays { get => _showOnAllDisplays; set => Set(ref _showOnAllDisplays, value); }

    /// <summary>With docks on all displays: unpinned running apps appear only on the dock of the display their window is on.</summary>
    public bool RunningAppsOnOwnDisplay { get => _runningAppsOnOwnDisplay; set => Set(ref _runningAppsOnOwnDisplay, value); }

    public bool AutoHide { get => _autoHide; set => Set(ref _autoHide, value); }

    public ThemePreference Theme { get => _theme; set => Set(ref _theme, value); }

    public BackdropKind Backdrop { get => _backdrop; set => Set(ref _backdrop, value); }

    public double TintOpacity { get => _tintOpacity; set => Set(ref _tintOpacity, Math.Clamp(value, 0, 1)); }

    /// <summary>Size of the main dock (and of other displays without their own size).</summary>
    public DockSize Size { get => _size; set => Set(ref _size, value); }

    /// <summary>Per-display size overrides for docks on other displays, keyed by device name (e.g. \.\DISPLAY1).</summary>
    public Dictionary<string, DockSize> DisplaySizes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Own size of a display's dock, or null when it follows <see cref="Size"/>.</summary>
    public DockSize? DisplaySizeOf(string device)
    {
        foreach (var (key, size) in DisplaySizes)
            if (string.Equals(key, device, StringComparison.OrdinalIgnoreCase)) return size;
        return null;
    }

    /// <summary>Sets (or with null, clears) a display's own dock size.</summary>
    public void SetDisplaySize(string device, DockSize? size)
    {
        if (DisplaySizeOf(device) == size) return;
        foreach (var key in DisplaySizes.Keys.Where(k => string.Equals(k, device, StringComparison.OrdinalIgnoreCase)).ToList())
            DisplaySizes.Remove(key);
        if (size is { } value) DisplaySizes[device] = value;
        OnPropertyChanged(nameof(DisplaySizes));
    }

    public DockLayout Layout { get => _layout; set => Set(ref _layout, value); }

    public DockWidthMode WidthMode { get => _widthMode; set => Set(ref _widthMode, value); }

    public DockAlignment Alignment { get => _alignment; set => Set(ref _alignment, value); }

    public double EdgeMargin { get => _edgeMargin; set => Set(ref _edgeMargin, Math.Clamp(value, 0, 32)); }

    public bool HoverEffect { get => _hoverEffect; set => Set(ref _hoverEffect, value); }

    // ---------------- Keyboard

    private bool _winNumberHotkeys = true;

    /// <summary>Win+1..9 / Win+0 open and switch dock apps (replace mode only).</summary>
    public bool WinNumberHotkeys { get => _winNumberHotkeys; set => Set(ref _winNumberHotkeys, value); }

    /// <summary>Global shortcuts by action id (<see cref="HotkeyActions"/>). Missing = default, empty string = off.</summary>
    public Dictionary<string, string> Hotkeys { get; set; } = new();

    public HotkeyGesture? GetHotkey(string actionId)
    {
        string? text = Hotkeys.TryGetValue(actionId, out var custom) ? custom : HotkeyActions.Find(actionId)?.DefaultGesture;
        return HotkeyGesture.Parse(text);
    }

    public void SetHotkey(string actionId, HotkeyGesture? gesture)
    {
        Hotkeys[actionId] = gesture?.ToString() ?? "";
        OnPropertyChanged(nameof(Hotkeys));
    }

    // ---------------- Diagnostics

    /// <summary>Writes verbose icon/window diagnostics to log.txt. Only editable in config.json.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool DebugLogging { get; set; }

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
    private bool _pinnedEnd;

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

    /// <summary>Widgets only: pinned to the fixed right edge of the dock (after the tray/clock) instead of scrolling with the other items.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool PinnedEnd { get => _pinnedEnd; set => Set(ref _pinnedEnd, value); }

    // ---- Group

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? GroupName { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? GroupAccent { get; set; }

    /// <summary>Child items inside a group folder.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<DockItem>? Children { get; set; }

    public static string NewId() => Guid.NewGuid().ToString("N")[..10];

    public static DockItem App(string path, string? name = null) => new() { Kind = DockItemKind.App, Path = path, Name = name };

    public static DockItem ForWidget(string widget, string? variant = null) => new() { Kind = DockItemKind.Widget, Widget = widget, Variant = variant };

    public static DockItem Separator() => new() { Kind = DockItemKind.Separator };

    public static DockItem Group(string name, List<DockItem>? children = null)
        => new() { Kind = DockItemKind.Group, GroupName = name, GroupAccent = "AccentBlueBrush", Children = children ?? new() };
}

public sealed class LegacyWidgetEntry
{
    public string Id { get; set; } = "";
    public bool Enabled { get; set; }
}
