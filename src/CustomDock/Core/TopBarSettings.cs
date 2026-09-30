namespace CustomDock.Core;

/// <summary>
/// The top bar: a second, thin dock on the main display that holds widgets (and a clock), like the menu bar on a Mac.
/// Start, search, the tray and the apps stay on the dock. Off until turned on.
/// </summary>
public sealed class TopBarSettings : ObservableObject
{
    private bool _enabled;
    private DockEdge _edge = DockEdge.Top;
    private DockSize _size = DockSize.Small;
    private DockLayout _layout = DockLayout.Attached;
    private BackdropKind? _backdrop;
    private bool _autoHide;
    private bool _showClock = true;

    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }

    /// <summary>The bar's screen edge; if the dock sits there, the bar takes the opposite one (see <see cref="EdgeFor"/>).</summary>
    public DockEdge Edge { get => _edge; set => Set(ref _edge, value); }

    public DockSize Size { get => _size; set => Set(ref _size, value); }

    public DockLayout Layout { get => _layout; set => Set(ref _layout, value); }

    /// <summary>The bar's own backdrop; null uses the dock's.</summary>
    public BackdropKind? Backdrop { get => _backdrop; set => Set(ref _backdrop, value); }

    public bool AutoHide { get => _autoHide; set => Set(ref _autoHide, value); }

    public bool ShowClock { get => _showClock; set => Set(ref _showClock, value); }

    /// <summary>Takes over every setting of <paramref name="other"/> (a profile's bar), keeping this object.</summary>
    public void CopyFrom(TopBarSettings other)
    {
        Edge = other.Edge;
        Size = other.Size;
        Layout = other.Layout;
        Backdrop = other.Backdrop;
        AutoHide = other.AutoHide;
        ShowClock = other.ShowClock;
        Enabled = other.Enabled;
    }

    /// <summary>The edge the bar uses: its own, unless the dock is there; then the opposite one.</summary>
    public static DockEdge EdgeFor(DockEdge barEdge, DockEdge dockEdge) => barEdge != dockEdge ? barEdge : Opposite(barEdge);

    public static DockEdge Opposite(DockEdge edge) => edge switch
    {
        DockEdge.Top => DockEdge.Bottom,
        DockEdge.Bottom => DockEdge.Top,
        DockEdge.Left => DockEdge.Right,
        _ => DockEdge.Left,
    };
}
