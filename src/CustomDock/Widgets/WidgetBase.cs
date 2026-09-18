using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using CustomDock.Core;

namespace CustomDock.Widgets;

/// <summary>Widget'ların barındırıldığı ortam (dock veya ayarlar galerisi).</summary>
public interface IWidgetHost
{
    DockEdge Edge { get; }

    bool IsVertical { get; }

    Window Window { get; }

    /// <summary>Galeri önizlemesi mi? (önizlemeler kalıcı veri yazmaz ve bildirim göndermez)</summary>
    bool IsPreview { get; }

    /// <summary>Popup/menü açıkken otomatik gizlemeyi durdurur.</summary>
    void BeginInteraction();

    void EndInteraction();

    /// <summary>Metin girişi için dock penceresini etkinleştirir.</summary>
    void ActivateForInput();
}

/// <summary>
/// Tüm widget'ların temel sınıfı.
/// Yaşam döngüsü: oluştur → <see cref="OnAttached"/> (servislere abone ol) → <see cref="OnVariantChanged"/> →
/// <see cref="OnDetached"/> (aboneliklerden çık, veriyi kaydet).
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

    /// <summary>null ise kart varsayılan cam rengini kullanır.</summary>
    public Brush? CardBackground { get => (Brush?)GetValue(CardBackgroundProperty); set => SetValue(CardBackgroundProperty, value); }

    public Thickness CardPadding { get => (Thickness)GetValue(CardPaddingProperty); set => SetValue(CardPaddingProperty, value); }

    public event Action? CardAppearanceChanged;

    protected IWidgetHost Host => _host ?? throw new InvalidOperationException("Widget henüz bağlanmadı.");

    public bool IsAttached { get; private set; }

    protected bool IsPreview => _host?.IsPreview ?? false;

    /// <summary>Widget'a özel kalıcı veri dosyasının adı.</summary>
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
            Log.Error(ex, $"Widget başlatılamadı: {Descriptor.Id}");
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
            Log.Error(ex, $"Widget kapatılamadı: {Descriptor.Id}");
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

    /// <summary>Varyant değiştiğinde (ve ilk bağlanmada) çağrılır.</summary>
    protected virtual void OnVariantChanged()
    {
    }

    /// <summary>Dock kenarı / yönü değiştiğinde çağrılır.</summary>
    public virtual void OnLayoutChanged()
    {
    }

    // ------------------------------------------------------------------ Dikey dock (kompakt kutucuk)

    /// <summary>Dikey dock'ta gösterilen özet kutucuk (ilk erişimde oluşturulur).</summary>
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

    /// <summary>Widget dikey dock'ta kutucuk olarak mı gösteriliyor?</summary>
    public bool IsCompact { get; internal set; }

    /// <summary>Kompakt moddayken iç popup'ların (düzenleyiciler) hizalanacağı öğe; açılmadan önce paneli kapatır.</summary>
    internal FrameworkElement? CompactAnchor { get; set; }

    internal Action? BeforeCompactPopup { get; set; }

    /// <summary>Widget kendi görünümünü güncelledikten sonra çağırır; kutucuk varsa onu da günceller.</summary>
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
            Log.Error(ex, $"Kompakt görünüm güncellenemedi: {Descriptor.Id}");
        }
    }

    /// <summary>Kutucuğu widget'ın güncel durumuyla doldurur. Varsayılan: yalnızca ikon.</summary>
    protected virtual void UpdateCompact(CompactTile tile)
    {
    }

    /// <summary>
    /// Kutucuğa tıklandığında çağrılır. Birincil eylemi doğrudan yapan widget'lar true döndürür;
    /// false dönerse widget'ın tamamı bir panelde açılır.
    /// </summary>
    public virtual bool OnCompactClick() => false;

    /// <summary>Sağ tık menüsünün başına widget'a özel komutlar ekler.</summary>
    public virtual void AddContextMenuItems(ItemCollection items)
    {
    }

    protected T GetSettings<T>() where T : ObservableObject, new()
        => AppServices.ConfigService.GetItemSettings<T>(Item);

    /// <summary>Adı verilen varyant düzenini gösterir, diğerlerini gizler (ör. "Layout_full").</summary>
    protected void ShowLayout(params FrameworkElement[] layouts)
    {
        foreach (var layout in layouts)
            layout.Visibility = layout.Name == "Layout_" + Variant ? Visibility.Visible : Visibility.Collapsed;
    }

    private DateTime _lastPopupClosedAt = DateTime.MinValue;

    /// <summary>Popup'ı açar veya açıksa kapatır (toggle).</summary>
    protected void TogglePopup(Popup popup, UIElement? target = null)
    {
        if (popup.IsOpen)
        {
            popup.IsOpen = false;
            return;
        }
        OpenPopup(popup, target);
    }

    /// <summary>Popup'ı dock kenarına göre doğru yönde açar ve açık kaldığı sürece otomatik gizlemeyi engeller.</summary>
    protected void OpenPopup(Popup popup, UIElement? target = null)
    {
        if (popup.IsOpen)
        {
            popup.IsOpen = false;
            return;
        }

        // Tıklama ile dışarı tıklama kancası yeni kapatmışsa tekrar açılmasını engelle (toggle hissi)
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
        popup.AllowsTransparency = true;
        popup.PopupAnimation = PopupAnimation.None;
        if (popup.Child is FrameworkElement child)
            Controls.Motion.PopIn(child, Dock.PopupPlacement.EnterOffset(edge));

        Host.BeginInteraction();
        void OnClosed(object? sender, EventArgs e)
        {
            popup.Closed -= OnClosed;
            _lastPopupClosedAt = DateTime.UtcNow;
            Host.EndInteraction();
        }
        popup.Closed += OnClosed;
        Dock.GlobalPopupDismissHook.RegisterPopup(popup);
        popup.IsOpen = true;
    }

    /// <summary>Bildirim gösterir (önizlemede gösterilmez).</summary>
    protected void Notify(string title, string body, string? tag = null, params Services.ToastAction[] actions)
    {
        if (IsPreview) return;
        AppServices.Notifications.Show(title, body, tag, actions);
    }
}
