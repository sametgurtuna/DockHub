using System.ComponentModel;
using CustomDock.Core;

namespace CustomDock.Dock;

/// <summary>What a dock window is: the main dock, the dock of another display, or the top bar.</summary>
public enum DockRole { Main, Secondary, Bar }

/// <summary>
/// The layout settings one dock window follows: its screen edge, size, shape, width, alignment, hiding and glass. The
/// main dock and the docks of other displays read them from <see cref="AppConfig"/> (<see cref="ConfigDockSurface"/>);
/// the top bar has settings of its own (<see cref="BarDockSurface"/>). A dock reads these only through its surface.
/// </summary>
public abstract class DockSurface : IDisposable
{
    public abstract DockRole Role { get; }

    public abstract DockEdge Edge { get; set; }

    /// <summary>Size setting (a secondary display may still override it; see AppConfig.DisplaySizes).</summary>
    public abstract DockSize Size { get; set; }

    public abstract DockLayout Layout { get; set; }

    public abstract DockWidthMode WidthMode { get; set; }

    public abstract DockAlignment Alignment { get; set; }

    public abstract bool AutoHide { get; set; }

    public abstract double EdgeMargin { get; set; }

    public abstract bool SmartAutoHide { get; set; }

    public abstract BackdropKind Backdrop { get; set; }

    public abstract double TintOpacity { get; set; }

    public bool IsVertical => Edge is DockEdge.Left or DockEdge.Right;

    /// <summary>Raised with the name of the surface property that changed (the names above).</summary>
    public event Action<string>? Changed;

    protected void OnChanged(string property) => Changed?.Invoke(property);

    public virtual void Dispose()
    {
    }
}

/// <summary>The surface of the main dock and of the docks on other displays: the settings in <see cref="AppConfig"/>.</summary>
public sealed class ConfigDockSurface : DockSurface
{
    /// <summary>The <see cref="AppConfig"/> properties a surface stands for (same names on <see cref="DockSurface"/>).</summary>
    public static readonly IReadOnlySet<string> Properties = new HashSet<string>
    {
        nameof(AppConfig.Edge), nameof(AppConfig.Size), nameof(AppConfig.Layout), nameof(AppConfig.WidthMode),
        nameof(AppConfig.Alignment), nameof(AppConfig.AutoHide), nameof(AppConfig.EdgeMargin), nameof(AppConfig.SmartAutoHide),
        nameof(AppConfig.Backdrop), nameof(AppConfig.TintOpacity),
    };

    private readonly AppConfig _config;

    public ConfigDockSurface(AppConfig config, DockRole role)
    {
        if (role == DockRole.Bar) throw new ArgumentException("The top bar has its own surface.", nameof(role));
        _config = config;
        Role = role;
        _config.PropertyChanged += OnConfigChanged;
    }

    public override DockRole Role { get; }

    public override DockEdge Edge { get => _config.Edge; set => _config.Edge = value; }

    public override DockSize Size { get => _config.Size; set => _config.Size = value; }

    public override DockLayout Layout { get => _config.Layout; set => _config.Layout = value; }

    public override DockWidthMode WidthMode { get => _config.WidthMode; set => _config.WidthMode = value; }

    public override DockAlignment Alignment { get => _config.Alignment; set => _config.Alignment = value; }

    public override bool AutoHide { get => _config.AutoHide; set => _config.AutoHide = value; }

    public override double EdgeMargin { get => _config.EdgeMargin; set => _config.EdgeMargin = value; }

    public override bool SmartAutoHide { get => _config.SmartAutoHide; set => _config.SmartAutoHide = value; }

    public override BackdropKind Backdrop { get => _config.Backdrop; set => _config.Backdrop = value; }

    public override double TintOpacity { get => _config.TintOpacity; set => _config.TintOpacity = value; }

    private void OnConfigChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is { } name && Properties.Contains(name)) OnChanged(name);
    }

    public override void Dispose() => _config.PropertyChanged -= OnConfigChanged;
}

/// <summary>
/// The surface of the top bar: its own edge (never the dock's; see <see cref="TopBarSettings.EdgeFor"/>), size, shape,
/// hiding and, if set, backdrop from <see cref="AppConfig.TopBar"/>; margin, smart hiding, tint and, by default, the
/// backdrop from the dock. It always spans its edge with the widgets from the start.
/// </summary>
public sealed class BarDockSurface : DockSurface
{
    /// <summary>Settings of the dock the bar shares.</summary>
    private static readonly HashSet<string> SharedProperties = new()
    {
        nameof(AppConfig.Edge), nameof(AppConfig.EdgeMargin), nameof(AppConfig.SmartAutoHide), nameof(AppConfig.Backdrop),
        nameof(AppConfig.TintOpacity),
    };

    private readonly AppConfig _config;
    private TopBarSettings _bar;
    private bool _disposed;

    public BarDockSurface(AppConfig config)
    {
        _config = config;
        _bar = config.TopBar;
        _bar.PropertyChanged += OnBarChanged;
        _config.PropertyChanged += OnConfigChanged;
    }

    public override DockRole Role => DockRole.Bar;

    public override DockEdge Edge { get => TopBarSettings.EdgeFor(_bar.Edge, _config.Edge); set => _bar.Edge = value; }

    public override DockSize Size { get => _bar.Size; set => _bar.Size = value; }

    public override DockLayout Layout { get => _bar.Layout; set => _bar.Layout = value; }

    public override DockWidthMode WidthMode { get => DockWidthMode.Full; set { } }

    public override DockAlignment Alignment { get => DockAlignment.Start; set { } }

    public override bool AutoHide { get => _bar.AutoHide; set => _bar.AutoHide = value; }

    public override double EdgeMargin { get => _config.EdgeMargin; set => _config.EdgeMargin = value; }

    public override bool SmartAutoHide { get => _config.SmartAutoHide; set => _config.SmartAutoHide = value; }

    public override BackdropKind Backdrop { get => _bar.Backdrop ?? _config.Backdrop; set => _bar.Backdrop = value; }

    public override double TintOpacity { get => _config.TintOpacity; set => _config.TintOpacity = value; }

    /// <summary>Whether the bar shows a clock at its end.</summary>
    public bool ShowClock => _bar.ShowClock;

    private void OnBarChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Turning the bar on or off is App's (it opens or closes the bar's window).
        if (!_disposed && e.PropertyName is { } name && name != nameof(TopBarSettings.Enabled)) OnChanged(name);
    }

    private void OnConfigChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Closed by an earlier handler of the same change (App closes the bar a profile turns off).
        if (_disposed) return;
        if (e.PropertyName == nameof(AppConfig.TopBar))
        {
            // Replaced (a profile or an import): follow the new object.
            _bar.PropertyChanged -= OnBarChanged;
            _bar = _config.TopBar;
            _bar.PropertyChanged += OnBarChanged;
            OnChanged(nameof(Edge));
            return;
        }
        if (e.PropertyName is not { } name || !SharedProperties.Contains(name)) return;
        if (name == nameof(AppConfig.Backdrop) && _bar.Backdrop is not null) return;
        OnChanged(name);
    }

    public override void Dispose()
    {
        _disposed = true;
        _bar.PropertyChanged -= OnBarChanged;
        _config.PropertyChanged -= OnConfigChanged;
    }
}
