using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CustomDock.Controls;
using CustomDock.Core;
using CustomDock.Dock;
using CustomDock.Widgets;

namespace CustomDock.Settings;

/// <summary>Galeri önizlemeleri için sahte dock ortamı.</summary>
internal sealed class PreviewHost : IWidgetHost
{
    public PreviewHost(Window window) => Window = window;
    public DockEdge Edge => DockEdge.Bottom;
    public bool IsVertical => false;
    public Window Window { get; }
    public bool IsPreview => true;
    public void BeginInteraction() { }
    public void EndInteraction() { }
    public void ActivateForInput() { }
}

public partial class SettingsWindow
{
    private FrameworkElement CreatePreviewCard(WidgetBase widget)
    {
        var card = new WidgetCard { Content = widget, HoverEnabled = false, Margin = new Thickness(0) };
        void Apply()
        {
            if (widget.CardBackground is { } background) card.Background = background;
            else card.SetResourceReference(BackgroundProperty, "CardBrush");
            card.Padding = widget.CardPadding;
        }
        widget.CardAppearanceChanged += Apply;
        Apply();
        return card;
    }

    private void BuildGallery()
    {
        foreach (var category in WidgetCategories.Ordered)
        {
            var descriptors = WidgetRegistry.All.Where(d => d.Category == category).ToList();
            if (descriptors.Count == 0) continue;

            GalleryPanel.Children.Add(new TextBlock
            {
                Text = category,
                FontFamily = (FontFamily)FindResource("DisplayFont"),
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(2, 18, 0, 10),
            });

            var wrap = new WrapPanel();
            foreach (var descriptor in descriptors)
            {
                foreach (var variant in descriptor.Variants)
                    wrap.Children.Add(CreateGalleryTile(descriptor, variant));
            }
            GalleryPanel.Children.Add(wrap);
        }
    }

    private FrameworkElement CreateGalleryTile(WidgetDescriptor descriptor, WidgetVariant variant)
    {
        var previewItem = new DockItem { Id = "preview-" + descriptor.Id + "-" + variant.Id, Kind = DockItemKind.Widget, Widget = descriptor.Id, Variant = variant.Id };
        FrameworkElement preview;
        try
        {
            var widget = descriptor.Create(previewItem);
            preview = CreatePreviewCard(widget);
            widget.Attach(_previewHost);
            _previews.Add(widget);
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Galeri önizlemesi oluşturulamadı: {descriptor.Id}");
            preview = new TextBlock { Text = descriptor.Name };
        }

        var stage = new Border
        {
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(8),
            Child = preview,
            HorizontalAlignment = HorizontalAlignment.Left,
            IsHitTestVisible = false,
            ToolTip = descriptor.Description,
        };
        stage.SetResourceReference(Border.BackgroundProperty, "GalleryBackdropBrush");

        var name = new TextBlock
        {
            Text = descriptor.Variants.Count > 1 ? $"{descriptor.Name} · {variant.Name}" : descriptor.Name,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            FontSize = 12.5,
        };

        var add = new Button
        {
            Content = "\uE710",
            Style = (Style)FindResource("IconButton"),
            ToolTip = "Dock'a ekle",
            Width = 28,
            Height = 28,
        };
        add.Click += (_, _) =>
        {
            var item = DockItem.ForWidget(descriptor.Id, variant.Id);
            AppServices.ConfigService.AddItem(item);
            add.Content = "\uE73E";
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.2) };
            timer.Tick += (_, _) => { timer.Stop(); add.Content = "\uE710"; };
            timer.Start();
        };

        var footer = new DockPanel { Margin = new Thickness(4, 6, 0, 0) };
        DockPanel.SetDock(add, System.Windows.Controls.Dock.Right);
        footer.Children.Add(add);
        footer.Children.Add(name);

        var tile = new StackPanel { Margin = new Thickness(0, 0, 18, 18), MinWidth = 150 };
        tile.Children.Add(stage);
        tile.Children.Add(footer);
        // Alt satır önizleme genişliğini izlesin
        footer.SetBinding(WidthProperty, new System.Windows.Data.Binding(nameof(ActualWidth)) { Source = stage });
        return tile;
    }
}
