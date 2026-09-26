using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CustomDock.Core;
using CustomDock.Dock;
using CustomDock.Services;

namespace CustomDock.Widgets;

/// <summary>Clipboard history: the last copied texts and images, click to copy again.</summary>
public partial class ClipboardWidget : WidgetBase
{
    public const string Icon = "M9,4 H15 V6.5 H9 Z M9,5 H6.5 A1.5,1.5 0 0 0 5,6.5 V19.5 A1.5,1.5 0 0 0 6.5,21 H17.5 A1.5,1.5 0 0 0 19,19.5 V6.5 A1.5,1.5 0 0 0 17.5,5 H15 M8.5,11 H15.5 M8.5,14.5 H15.5 M8.5,18 H12.5";
    private static readonly Geometry IconGeometry = Freeze(Geometry.Parse(Icon));

    /// <summary>Raised by the "Clipboard history" shortcut; the first clipboard widget opens its list.</summary>
    public static event Action? OpenRequested;

    private static readonly List<ClipboardWidget> s_attached = new();

    private static ClipboardHistoryService History => AppServices.Clipboard;

    public ClipboardWidget()
    {
        InitializeComponent();
        IconPath.Data = IconGeometry;
        LatestIconPath.Data = IconGeometry;
    }

    private static Geometry Freeze(Geometry geometry)
    {
        geometry.Freeze();
        return geometry;
    }

    public static void RequestOpen() => OpenRequested?.Invoke();

    protected override void OnAttached()
    {
        if (!IsPreview)
        {
            History.Subscribe();
            History.Changed += Render;
            s_attached.Add(this);
            OpenRequested += OnOpenRequested;
        }
        Render();
    }

    protected override void OnDetached()
    {
        if (IsPreview) return;
        History.Changed -= Render;
        History.Unsubscribe();
        s_attached.Remove(this);
        OpenRequested -= OnOpenRequested;
    }

    private void OnOpenRequested()
    {
        if (s_attached.FirstOrDefault() != this) return;
        (Host.Window as DockWindow)?.Reveal();
        OpenPopup(HistoryPopup);
    }

    protected override void OnVariantChanged()
    {
        ShowLayout(Layout_icon, Layout_latest);
        Render();
    }

    private void Render()
    {
        var entries = IsPreview
            ? new List<ClipboardEntry> { new() { Text = "Meeting moved to 3pm" }, new() { Text = "https://github.com/sametgurtuna/DockHub" } }
            : History.Entries;
        var latest = entries.FirstOrDefault();
        LatestText.Text = latest?.Preview ?? L.T("Nothing copied yet");
        CountText.Text = entries.Count.ToString();
        CountBadge.Visibility = entries.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ToolTip = latest is null ? L.T("Clipboard history") : L.T("Last copied: {0}", latest.Preview);
        SetIdle(entries.Count == 0);
        if (HistoryPopup.IsOpen) BuildList();
        RefreshCompact();
    }

    private void BuildList()
    {
        EntryList.Children.Clear();
        if (History.Entries.Count == 0)
        {
            EntryList.Children.Add(new TextBlock
            {
                Text = L.T("Copy some text or an image and it shows up here."),
                Margin = new Thickness(6, 10, 6, 10),
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
            });
            return;
        }

        foreach (var entry in History.Entries)
        {
            var e = entry;
            UIElement content = e.Image is { } image
                ? new Image { Source = image, MaxHeight = 70, HorizontalAlignment = HorizontalAlignment.Left, Stretch = Stretch.Uniform }
                : new TextBlock { Text = e.Preview, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = (Brush)FindResource("TextPrimaryBrush") };

            var pin = new TextBlock
            {
                Text = "",
                FontSize = 11,
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = e.IsPinned ? Visibility.Visible : Visibility.Collapsed,
            };
            pin.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
            pin.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");

            var row = new DockPanel();
            DockPanel.SetDock(pin, System.Windows.Controls.Dock.Right);
            row.Children.Add(pin);
            row.Children.Add(content);

            var item = new Border
            {
                Padding = new Thickness(8, 7, 8, 7),
                CornerRadius = new CornerRadius(7),
                Background = Brushes.Transparent,
                Child = row,
                Cursor = Cursors.Hand,
                ToolTip = e.Text is { Length: > 90 } full ? full[..Math.Min(full.Length, 600)] : null,
            };
            item.MouseEnter += (_, _) => item.SetResourceReference(Border.BackgroundProperty, "DockHoverBrush");
            item.MouseLeave += (_, _) => item.Background = Brushes.Transparent;
            item.MouseLeftButtonUp += (_, args) =>
            {
                args.Handled = true;
                History.Restore(e);
                ClosePopup(HistoryPopup);
            };
            var menu = new ContextMenu();
            menu.Items.Add(DockMenu.Item(L.T("Copy"), "", () => History.Restore(e)));
            menu.Items.Add(DockMenu.Check(L.T("Pin"), e.IsPinned, () => History.TogglePin(e)));
            menu.Items.Add(DockMenu.Item(L.T("Remove"), "", () => History.Remove(e)));
            item.ContextMenu = menu;
            EntryList.Children.Add(item);
        }
    }

    private void OnClearClick(object sender, RoutedEventArgs e) => History.Clear();

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (DockDragHelper.JustDragged || IsPreview || e.Handled) return;
        BuildList();
        OpenPopup(HistoryPopup);
        e.Handled = true;
    }

    public override bool OnCompactClick()
    {
        BuildList();
        OpenPopup(HistoryPopup);
        return true;
    }

    protected override void UpdateCompact(CompactTile tile)
    {
        tile.ShowGlyph(IconGeometry, "AccentCyanBrush");
        tile.Text = History.Entries.Count > 0 ? History.Entries.Count.ToString() : null;
    }

    public override void AddContextMenuItems(ItemCollection items)
    {
        items.Add(DockMenu.Item(L.T("Show history"), "", () => { BuildList(); OpenPopup(HistoryPopup); }));
        items.Add(DockMenu.Item(L.T("Clear history"), "", History.Clear, History.Entries.Count > 0));
    }
}
