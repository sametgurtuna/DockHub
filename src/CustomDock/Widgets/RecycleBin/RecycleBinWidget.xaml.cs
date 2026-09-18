using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CustomDock.Core;
using CustomDock.Dock;
using CustomDock.Services;

namespace CustomDock.Widgets;

public partial class RecycleBinWidget : WidgetBase
{
    // Gerçekçi çöp kutusu ikonları (macOS tarzı kapaklı sepet)
    // Boş: Kapaklı çöp kutusu gövdesi, kapak ve kulp, alt kısım ayrıntılı
    private const string EmptyIconPath =
        "M9.5,4 H14.5 M7,6 H17 L16.2,20 A1,1 0 0 1 15.2,21 H8.8 A1,1 0 0 1 7.8,20 Z " + // Gövde
        "M10,9 V18 M14,9 V18 M12,9 V18 " + // Çizgiler
        "M6,6 H18 M10,4 V6 M14,4 V6"; // Kapak ve kulp
    private const string FullIconPath =
        "M9.5,4 H14.5 M7,6 H17 L16.2,20 A1,1 0 0 1 15.2,21 H8.8 A1,1 0 0 1 7.8,20 Z " + // Gövde
        "M10,10 V18 M14,10 V18 M12,10 V18 " + // Çizgiler
        "M6,6 H18 M10,4 V6 M14,4 V6 " + // Kapak ve kulp
        "M9,3 C9.3,1.5 10.5,1.5 11,2.5 M12,1.5 C12.5,1 14,1 14.5,3 M8.5,3.5 L10,5 M15,3 L13.5,5"; // Buruşuk kağıtlar

    private static readonly Geometry EmptyGeometry = Geometry.Parse(EmptyIconPath);
    private static readonly Geometry FullGeometry = Geometry.Parse(FullIconPath);

    static RecycleBinWidget()
    {
        EmptyGeometry.Freeze();
        FullGeometry.Freeze();
    }

    public RecycleBinWidget()
    {
        InitializeComponent();
    }

    protected override void OnAttached()
    {
        AppServices.RecycleBin.Updated += OnRecycleBinUpdated;
        Render();
    }

    protected override void OnDetached()
    {
        AppServices.RecycleBin.Updated -= OnRecycleBinUpdated;
    }

    protected override void OnVariantChanged()
    {
        ShowLayout(Layout_icon, Layout_details);
        Render();
    }

    private void OnRecycleBinUpdated(object? sender, EventArgs e) => Render();

    private void Render()
    {
        var info = AppServices.RecycleBin.Current;
        bool empty = info.IsEmpty;
        var geometry = empty ? EmptyGeometry : FullGeometry;
        string brushKey = empty ? "TextSecondaryBrush" : "AccentBlueBrush";

        IconOnlyPath.Data = geometry;
        IconOnlyPath.SetResourceReference(System.Windows.Shapes.Path.FillProperty, brushKey);

        DetailsIconPath.Data = geometry;
        DetailsIconPath.SetResourceReference(System.Windows.Shapes.Path.FillProperty, brushKey);
        DetailsSubText.Text = empty ? "Boş" : $"{info.ItemCount} öğe · {info.FormattedSize}";

        ToolTip = empty
            ? "Çöp Kutusu (Boş)"
            : $"Çöp Kutusu: {info.ItemCount} öğe, {info.FormattedSize}";

        RefreshCompact();
    }

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        AppServices.RecycleBin.Refresh();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (DockDragHelper.JustDragged) return;
        AppServices.RecycleBin.Open();
        e.Handled = true;
    }

    protected override void OnDragOver(DragEventArgs e)
    {
        base.OnDragOver(e);
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }
    }

    protected override void OnDrop(DragEventArgs e)
    {
        base.OnDrop(e);
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
        {
            AppServices.RecycleBin.SendToRecycleBin(files);
            e.Handled = true;
        }
    }

    protected override void UpdateCompact(CompactTile tile)
    {
        var info = AppServices.RecycleBin.Current;
        bool empty = info.IsEmpty;
        tile.ShowGlyph(empty ? EmptyGeometry : FullGeometry, empty ? "TextSecondaryBrush" : "AccentBlueBrush");
        tile.Text = empty ? null : info.ItemCount.ToString();
    }

    public override bool OnCompactClick()
    {
        AppServices.RecycleBin.Open();
        return true;
    }

    public override void AddContextMenuItems(ItemCollection items)
    {
        AppServices.RecycleBin.Refresh();
        var info = AppServices.RecycleBin.Current;
        items.Add(DockMenu.Item("Çöp Kutusunu Aç", "\uE838", () => AppServices.RecycleBin.Open()));
        items.Add(DockMenu.Item("Çöp Kutusunu Boşalt", "\uE74D", () => AppServices.RecycleBin.Empty(),
            enabled: !info.IsEmpty));
    }
}
