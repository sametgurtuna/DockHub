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
    /// <summary>No blur: the tint over the plain desktop. Works everywhere, unlike DWM blur, which some graphics drivers draw black.</summary>
    Transparent,
}

public enum DockSize { Small, Medium, Large }

public enum MotionPreference { System, Full, Reduced, Off }

public enum RunningIndicatorStyle { Line, Dots, Off }

/// <summary>Widgets on the dock: each on its own card, or straight on the dock with a thin line between them.</summary>
public enum WidgetStyle { Cards, Seamless }

/// <summary>An app's windows on one button (Always), or each window on a button of its own with its title (Never).</summary>
public enum CombineButtons { Always, Never }

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

public enum MicrophoneIconMode
{
    /// <summary>Only while an app is using the microphone, like Windows.</summary>
    WhenInUse,
    Always,
    Off,
}

public enum SearchButtonAction
{
    /// <summary>Opens Windows Search (Win+S).</summary>
    WindowsSearch,
    /// <summary>Opens DockHub's quick launcher.</summary>
    Launcher,
}

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

    private CombineButtons _combineButtons = CombineButtons.Always;

    /// <summary>Like Windows' "Combine taskbar buttons": Always (one button per app, as before) or Never (one per window).</summary>
    public CombineButtons CombineButtons { get => _combineButtons; set => Set(ref _combineButtons, value); }

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

    private bool _showKeyboardLayout = true;
    private MicrophoneIconMode _microphoneIcon = MicrophoneIconMode.WhenInUse;
    private bool _showNotificationIndicator = true;
    private bool _showDesktopIndicator = true;
    private bool _runningAppsAllDesktops;
    private bool _previewPeek = true;
    private SearchButtonAction _searchButtonAction = SearchButtonAction.WindowsSearch;

    /// <summary>Input language indicator next to the tray (only while more than one keyboard layout is installed).</summary>
    public bool ShowKeyboardLayout { get => _showKeyboardLayout; set => Set(ref _showKeyboardLayout, value); }

    /// <summary>Microphone icon next to the tray: click to mute or unmute.</summary>
    public MicrophoneIconMode MicrophoneIcon { get => _microphoneIcon; set => Set(ref _microphoneIcon, value); }

    /// <summary>Notification count and Do Not Disturb state next to the clock.</summary>
    public bool ShowNotificationIndicator { get => _showNotificationIndicator; set => Set(ref _showNotificationIndicator, value); }

    /// <summary>Virtual desktop number next to Task view (only while more than one desktop exists).</summary>
    public bool ShowDesktopIndicator { get => _showDesktopIndicator; set => Set(ref _showDesktopIndicator, value); }

    /// <summary>Running apps from every virtual desktop, not just the current one.</summary>
    public bool RunningAppsAllDesktops { get => _runningAppsAllDesktops; set => Set(ref _runningAppsAllDesktops, value); }

    /// <summary>Hovering a window preview shows that window and fades the others (Aero Peek).</summary>
    public bool PreviewPeek { get => _previewPeek; set => Set(ref _previewPeek, value); }

    public SearchButtonAction SearchButtonAction { get => _searchButtonAction; set => Set(ref _searchButtonAction, value); }

    private bool _launcherFileSearch = true;

    /// <summary>The quick launcher also finds files and folders in the user's folder (Windows Search index).</summary>
    public bool LauncherFileSearch { get => _launcherFileSearch; set => Set(ref _launcherFileSearch, value); }

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

    private RunningIndicatorStyle _runningIndicator = RunningIndicatorStyle.Line;

    /// <summary>How open apps are marked: a line that widens with more windows, one dot per window, or nothing.</summary>
    public RunningIndicatorStyle RunningIndicator { get => _runningIndicator; set => Set(ref _runningIndicator, value); }

    private bool _alignWidgetWidths = true;

    /// <summary>Widget cards snap their width to a grid of half the dock height, so the dock keeps an even rhythm.</summary>
    public bool AlignWidgetWidths { get => _alignWidgetWidths; set => Set(ref _alignWidgetWidths, value); }

    private WidgetStyle _widgetStyle = WidgetStyle.Cards;

    /// <summary>Cards (each widget on its own card, as before) or Seamless (no card; a thin line between widgets).</summary>
    public WidgetStyle WidgetStyle { get => _widgetStyle; set => Set(ref _widgetStyle, value); }

    private TopBarSettings _topBar = new();

    /// <summary>The top bar (off by default; settings from before it existed load as off).</summary>
    public TopBarSettings TopBar { get => _topBar; set => Set(ref _topBar, value ?? new TopBarSettings()); }

    private double _textScale;

    /// <summary>Text size of settings, panels and menus. 0 follows Windows' "Text size" setting.</summary>
    public double TextScale { get => _textScale; set => Set(ref _textScale, value <= 0 ? 0 : Math.Clamp(value, 1, 2)); }

    /// <summary>Layouts the user saved from their own setup (Settings › Appearance › Layout presets).</summary>
    public List<CustomLayoutPreset> CustomPresets { get; set; } = new();

    /// <summary>
    /// Settings of widgets a layout preset took off the dock, by widget type (weather city, units...). A later preset
    /// that brings the type back starts from them instead of the defaults.
    /// </summary>
    public Dictionary<string, JsonObject> RemovedWidgetSettings { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    private bool _smartAutoHide;

    /// <summary>With auto-hide: the dock only hides while the active window overlaps it.</summary>
    public bool SmartAutoHide { get => _smartAutoHide; set => Set(ref _smartAutoHide, value); }

    // ---------------- Accessibility

    private MotionPreference _motion = MotionPreference.System;

    /// <summary>Animation level; System follows Windows' "Animation effects" setting.</summary>
    public MotionPreference Motion { get => _motion; set => Set(ref _motion, value); }

    // ---------------- Language

    private UiLanguage _language = UiLanguage.System;

    /// <summary>Interface language; System follows the Windows display language. Applies after a restart.</summary>
    public UiLanguage Language { get => _language; set => Set(ref _language, value); }

    // ---------------- Updates

    private bool _checkForUpdates = true;
    private bool _includePrereleases;

    /// <summary>Checks GitHub Releases once a day for a newer DockHub.</summary>
    public bool CheckForUpdates { get => _checkForUpdates; set => Set(ref _checkForUpdates, value); }

    public bool IncludePrereleases { get => _includePrereleases; set => Set(ref _includePrereleases, value); }

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

    /// <summary>All shortcuts at once (settings from another PC).</summary>
    public void ReplaceHotkeys(Dictionary<string, string> hotkeys)
    {
        Hotkeys = new Dictionary<string, string>(hotkeys);
        OnPropertyChanged(nameof(Hotkeys));
    }

    private string? _syncFolder;

    /// <summary>Folder the settings are shared through with other PCs (OneDrive...); null: settings sync is off.</summary>
    public string? SyncFolder { get => _syncFolder; set => Set(ref _syncFolder, string.IsNullOrWhiteSpace(value) ? null : value); }

    /// <summary>This PC in the sync folder (so it never takes back what it wrote itself).</summary>
    public string? SyncDeviceId { get; set; }

    /// <summary>When the settings this PC last wrote to, or took from, the sync folder were written (UTC).</summary>
    public DateTime? SyncAppliedAt { get; set; }

    /// <summary>The first-run welcome screen was completed or closed.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool WelcomeShown { get; set; }

    // ---------------- Diagnostics

    /// <summary>Writes verbose icon/window diagnostics to log.txt. Only editable in config.json.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool DebugLogging { get; set; }

    // ---------------- Profiles

    /// <summary>Saved dock setups (empty until the user creates one).</summary>
    public List<DockProfile> Profiles { get; set; } = new();

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ActiveProfileId { get; set; }

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

    private string? _display;

    /// <summary>
    /// Widgets only: the display (device name, "\\.\DISPLAY2") whose dock shows the widget when "Show on all displays"
    /// is on. Null, or a display without a dock, means the main dock.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Display { get => _display; set => Set(ref _display, value); }

    /// <summary>The <see cref="Surface"/> of a widget on the top bar.</summary>
    public const string BarSurface = "bar";

    private string? _surface;

    /// <summary>
    /// Widgets only: <see cref="BarSurface"/> when the widget lives on the top bar. While the bar is off it shows on the
    /// dock. Null: the dock.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Surface { get => _surface; set => Set(ref _surface, value); }

    private bool _collapseWhenIdle;

    /// <summary>Widgets only: show the small tile while the widget has nothing to show (see WidgetBase.IsIdle).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool CollapseWhenIdle { get => _collapseWhenIdle; set => Set(ref _collapseWhenIdle, value); }

    /// <summary>Widgets that start collapsed while idle when newly added.</summary>
    private static readonly HashSet<string> CollapseByDefault = new() { "media", "notes", "reminders" };

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

    public static DockItem ForWidget(string widget, string? variant = null)
        => new() { Kind = DockItemKind.Widget, Widget = widget, Variant = variant, CollapseWhenIdle = CollapseByDefault.Contains(widget) };

    public static DockItem Separator() => new() { Kind = DockItemKind.Separator };

    public static DockItem Group(string name, List<DockItem>? children = null)
        => new() { Kind = DockItemKind.Group, GroupName = name, GroupAccent = "AccentBlueBrush", Children = children ?? new() };
}

/// <summary>A layout the user saved: the look plus the widgets (type and layout) of the setup at that time.</summary>
public sealed class CustomLayoutPreset
{
    public string Id { get; set; } = DockItem.NewId();

    public string Name { get; set; } = "";

    public JsonObject? Appearance { get; set; }

    public List<CustomPresetWidget> Widgets { get; set; } = new();
}

public sealed class CustomPresetWidget
{
    public string Widget { get; set; } = "";

    public string? Variant { get; set; }
}

public sealed class LegacyWidgetEntry
{
    public string Id { get; set; } = "";
    public bool Enabled { get; set; }
}
