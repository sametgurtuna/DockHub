using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using CustomDock.Core;

namespace CustomDock.Widgets;

/// <summary>Host environment where widgets are hosted (dock or settings gallery).</summary>
public interface IWidgetHost
{
    DockEdge Edge { get; }

    bool IsVertical { get; }

    Window Window { get; }

    /// <summary>Is this a gallery preview? (previews do not write persistent data or send notifications)</summary>
    bool IsPreview { get; }

    /// <summary>Suppresses auto-hide while a popup or menu is open.</summary>
    void BeginInteraction();

    void EndInteraction();

    /// <summary>Activates the dock window for text input.</summary>
    void ActivateForInput();
}

/// <summary>
/// Base class for all widgets.
/// Lifecycle: instantiate → <see cref="OnAttached"/> (subscribe to services) → <see cref="OnVariantChanged"/> →
/// <see cref="OnDetached"/> (unsubscribe, persist data).
/// </summary>
public abstract class WidgetBase : UserControl
{
    public static readonly DependencyProperty CardBackgroundProperty = DependencyProperty.Register(
        nameof(CardBackground), typeof(Brush), typeof(WidgetBase),
        new PropertyMetadata(null, (d, _) => ((WidgetBase)d).CardAppearanceChanged?.Invoke()));

    public static readonly DependencyProperty CardPaddingProperty = DependencyProperty.Register(
        nameof(CardPadding), typeof(Thickness), typeof(WidgetBase),
        new PropertyMetadata(new Thickness(11, 0, 11, 0), (d, _) => ((WidgetBase)d).CardAppearanceChanged?.Invoke()));

    private IWidgetHost? _host;
    private CompactTile? _compact;

    protected WidgetBase()
    {
        Focusable = false;
        VerticalAlignment = VerticalAlignment.Center;
        SnapsToDevicePixels = true;
    }

    public WidgetDescriptor Descriptor { get; internal set; } = null!;

    public DockItem Item { get; internal set; } = null!;

    public string Variant
    {
        get
        {
            var variant = Item.Variant;
            return Descriptor.Variants.Any(v => v.Id == variant) ? variant! : Descriptor.DefaultVariant;
        }
    }

    /// <summary>null if card uses default glass background.</summary>
    public Brush? CardBackground { get => (Brush?)GetValue(CardBackgroundProperty); set => SetValue(CardBackgroundProperty, value); }

    public Thickness CardPadding { get => (Thickness)GetValue(CardPaddingProperty); set => SetValue(CardPaddingProperty, value); }

    public event Action? CardAppearanceChanged;

    protected IWidgetHost Host => _host ?? throw new InvalidOperationException("Widget is not attached yet.");

    public bool IsAttached { get; private set; }

    protected bool IsPreview => _host?.IsPreview ?? false;

    /// <summary>Name of widget-specific persistent state key.</summary>
    protected string StateKey => $"{Descriptor.Id}-{Item.Id}";

    internal void Attach(IWidgetHost host)
    {
        if (IsAttached) return;
        _host = host;
        IsAttached = true;
        Item.PropertyChanged += OnItemPropertyChanged;
        try
        {
            OnAttached();
            OnVariantChanged();
            OnLayoutChanged();
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Failed to start widget: {Descriptor.Id}");
        }
    }

    internal void Detach()
    {
        if (!IsAttached) return;
        IsAttached = false;
        Item.PropertyChanged -= OnItemPropertyChanged;
        try
        {
            OnDetached();
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Failed to stop widget: {Descriptor.Id}");
        }
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DockItem.Variant))
            OnVariantChanged();
    }

    protected virtual void OnAttached()
    {
    }

    protected virtual void OnDetached()
    {
    }

    /// <summary>Called when variant changes (and on initial attach).</summary>
    protected virtual void OnVariantChanged()
    {
    }

    /// <summary>Called when dock edge or orientation changes.</summary>
    public virtual void OnLayoutChanged()
    {
    }

    // ------------------------------------------------------------------ Vertical dock (compact tile)

    /// <summary>Summary tile shown in vertical dock (created on first access).</summary>
    public CompactTile Compact
    {
        get
        {
            if (_compact is null)
            {
                _compact = new CompactTile();
                _compact.ShowGlyph(Descriptor.Icon, Descriptor.AccentKey);
                RefreshCompact();
            }
            return _compact;
        }
    }

    /// <summary>Is the widget shown as a compact tile in vertical dock?</summary>
    public bool IsCompact { get; internal set; }

    /// <summary>Anchor element for inner popups in compact mode; closes panel before opening.</summary>
    internal FrameworkElement? CompactAnchor { get; set; }

    internal Action? BeforeCompactPopup { get; set; }

    /// <summary>Called after widget updates its appearance; updates tile if present.</summary>
    protected void RefreshCompact()
    {
        if (_compact is null) return;
        try
        {
            UpdateCompact(_compact);
            _compact.ToolTip = ToolTip;
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Failed to update compact view: {Descriptor.Id}");
        }
    }

    /// <summary>Fills tile with widget's current state. Default: icon only.</summary>
    protected virtual void UpdateCompact(CompactTile tile)
    {
    }

    /// <summary>
    /// Called when tile is clicked. Widgets that perform primary action directly return true;
    /// if false is returned, the full widget is opened in a flyout panel.
    /// </summary>
    public virtual bool OnCompactClick() => false;

    /// <summary>Adds widget-specific commands to the top of the context menu.</summary>
    public virtual void AddContextMenuItems(ItemCollection items)
    {
    }

    protected T GetSettings<T>() where T : ObservableObject, new()
        => AppServices.ConfigService.GetItemSettings<T>(Item);

    /// <summary>Shows the named variant layout and hides others (e.g. "Layout_full").</summary>
    protected void ShowLayout(params FrameworkElement[] layouts)
    {
        foreach (var layout in layouts)
            layout.Visibility = layout.Name == "Layout_" + Variant ? Visibility.Visible : Visibility.Collapsed;
    }

    private DateTime _lastPopupClosedAt = DateTime.MinValue;

    /// <summary>Closes popup with smooth macOS-style animation.</summary>
    protected void ClosePopup(Popup popup)
    {
        if (popup is null) return;
        Dock.PopupAnimationHelper.ClosePopup(popup, Host.Edge);
    }

    /// <summary>Opens popup or closes it if already open (toggle).</summary>
    protected void TogglePopup(Popup popup, UIElement? target = null)
    {
        if (popup.IsOpen || Dock.PopupAnimationHelper.IsClosing(popup))
        {
            ClosePopup(popup);
            return;
        }
        OpenPopup(popup, target);
    }

    /// <summary>Opens popup in correct direction according to dock edge and prevents auto-hide while open.</summary>
    protected void OpenPopup(Popup popup, UIElement? target = null)
    {
        if (popup.IsOpen || Dock.PopupAnimationHelper.IsClosing(popup))
        {
            ClosePopup(popup);
            return;
        }

        // Prevent reopening if dismiss hook just closed it on click (toggle feel)
        if (DateTime.UtcNow - _lastPopupClosedAt < TimeSpan.FromMilliseconds(250))
            return;

        if (IsCompact && CompactAnchor is not null)
        {
            BeforeCompactPopup?.Invoke();
            target = CompactAnchor;
        }
        var anchor = target as FrameworkElement ?? this;
        var edge = Host.Edge;
        Dock.PopupPlacement.PlacePopup(popup, anchor, edge, IsPreview ? 6 : 8);

        Host.BeginInteraction();
        void OnClosed(object? sender, EventArgs e)
        {
            popup.Closed -= OnClosed;
            _lastPopupClosedAt = DateTime.UtcNow;
            Host.EndInteraction();
        }
        popup.Closed += OnClosed;
        Dock.GlobalPopupDismissHook.RegisterPopup(popup);
        Dock.PopupAnimationHelper.AnimateOpen(popup, edge);
    }

    /// <summary>Shows notification (suppressed in preview mode).</summary>
    protected void Notify(string title, string body, string? tag = null, params Services.ToastAction[] actions)
    {
        if (IsPreview) return;
        AppServices.Notifications.Show(title, body, tag, actions);
    }
}
