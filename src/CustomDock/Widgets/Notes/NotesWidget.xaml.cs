using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using CustomDock.Controls;
using CustomDock.Core;
using CustomDock.Dock;

namespace CustomDock.Widgets;

/// <remarks>Pink is legacy; converted to Red upon loading.</remarks>
public enum NoteColor { Yellow, Green, Blue, Pink, Purple, Orange, Red }

public enum NoteWidth { Narrow, Normal, Wide }

public sealed class NotesSettings : ObservableObject
{
    private double _fontSize = 20;
    private NoteWidth _width = NoteWidth.Normal;
    private NoteColor _color = NoteColor.Yellow;

    /// <summary>Font size on note sheet.</summary>
    public double FontSize { get => _fontSize; set => Set(ref _fontSize, value < 14 ? 20 : Math.Min(value, 28)); } // <14: old card font size

    /// <summary>Card width on the dock.</summary>
    public NoteWidth Width { get => _width; set => Set(ref _width, value); }

    public NoteColor Color { get => _color; set => Set(ref _color, value == NoteColor.Pink ? NoteColor.Red : value); }

    public static IReadOnlyList<Option<NoteWidth>> WidthOptions { get; } = new List<Option<NoteWidth>>
    {
        new(NoteWidth.Narrow, "Narrow"),
        new(NoteWidth.Normal, "Normal"),
        new(NoteWidth.Wide, "Wide"),
    };

    public static IReadOnlyList<Option<NoteColor>> ColorOptions { get; } = new List<Option<NoteColor>>
    {
        new(NoteColor.Yellow, "Yellow"),
        new(NoteColor.Orange, "Orange"),
        new(NoteColor.Red, "Red"),
        new(NoteColor.Purple, "Purple"),
        new(NoteColor.Blue, "Blue"),
        new(NoteColor.Green, "Green"),
    };

    public static IReadOnlyList<Option<double>> FontSizeOptions { get; } = new List<Option<double>>
    {
        new(16, "Small"),
        new(20, "Medium"),
        new(26, "Large"),
    };

    /// <summary>Vibrant colors in the palette (paper background is Note…Brush theme resource).</summary>
    public static System.Windows.Media.Color SwatchColor(NoteColor color) => color switch
    {
        NoteColor.Orange => System.Windows.Media.Color.FromRgb(0xFF, 0x92, 0x30),
        NoteColor.Red or NoteColor.Pink => System.Windows.Media.Color.FromRgb(0xFF, 0x3B, 0x5C),
        NoteColor.Purple => System.Windows.Media.Color.FromRgb(0xC9, 0x4F, 0xE0),
        NoteColor.Blue => System.Windows.Media.Color.FromRgb(0x0A, 0x84, 0xFF),
        NoteColor.Green => System.Windows.Media.Color.FromRgb(0x34, 0xC7, 0x59),
        _ => System.Windows.Media.Color.FromRgb(0xFF, 0xCC, 0x00),
    };
}

public sealed class NoteData
{
    public string Text { get; set; } = "";
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Sticky note: preview on the dock; opens larger note sheet on click.
/// Colors and font sizes can be customized from the "Customize" page. Text autosaves.
/// </summary>
public partial class NotesWidget : WidgetBase
{
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(800) };
    private readonly Dictionary<NoteColor, TextBlock> _swatchChecks = new();
    private readonly Dictionary<double, Button> _fontButtons = new();
    private NotesSettings _settings = new();
    private bool _loading;
    private bool _dirty;
    private DateTime _closedAt;

    public NotesWidget()
    {
        InitializeComponent();
        _saveTimer.Tick += (_, _) => Save();
        BuildCustomizePage();
    }

    protected override void OnAttached()
    {
        _settings = GetSettings<NotesSettings>();
        _settings.PropertyChanged += OnSettingsChanged;

        _loading = true;
        NoteBox.Text = IsPreview ? "Release update.\nGo for a walk." : JsonStore.LoadData<NoteData>(StateKey).Text;
        _loading = false;
        NoteBox.IsReadOnly = IsPreview;
        Root.Cursor = IsPreview ? null : Cursors.Hand;

        ApplySettings();
        UpdatePreview();
    }

    protected override void OnDetached()
    {
        _settings.PropertyChanged -= OnSettingsChanged;
        EditorPopup.IsOpen = false;
        Save();
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e) => ApplySettings();

    private void ApplySettings()
    {
        Root.Width = _settings.Width switch
        {
            NoteWidth.Narrow => 130,
            NoteWidth.Wide => 250,
            _ => 170,
        };

        string paper = $"Note{_settings.Color}Brush";
        SetResourceReference(CardBackgroundProperty, paper);
        Sheet.SetResourceReference(Border.BackgroundProperty, paper);

        NoteBox.FontSize = _settings.FontSize;
        TextBlock.SetLineHeight(NoteBox, Math.Round(_settings.FontSize * 1.3));

        foreach (var (color, check) in _swatchChecks)
            check.Visibility = color == _settings.Color ? Visibility.Visible : Visibility.Collapsed;
        foreach (var (size, button) in _fontButtons)
        {
            bool selected = Math.Abs(size - _settings.FontSize) < 0.1;
            button.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(selected ? (byte)0x33 : (byte)0x14, 0, 0, 0));
            button.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
        }

        RefreshCompact();
    }

    private void UpdatePreview()
    {
        var text = NoteBox.Text.Trim();
        PreviewText.Text = text;
        PlaceholderText.Visibility = text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ------------------------------------------------------------------ Customize page

    private void BuildCustomizePage()
    {
        var swatches = new List<UIElement>();
        foreach (var option in NotesSettings.ColorOptions)
        {
            var color = option.Value;
            var check = new TextBlock
            {
                Text = "\uE73E",
                FontSize = 13,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false,
            };
            check.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
            _swatchChecks[color] = check;

            var dot = new Ellipse
            {
                Fill = new SolidColorBrush(NotesSettings.SwatchColor(color)),
                Stroke = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x30, 0, 0, 0)),
                StrokeThickness = 1,
            };
            var scale = new ScaleTransform();
            var face = new Grid { Width = 32, Height = 32, RenderTransform = scale, RenderTransformOrigin = new Point(0.5, 0.5) };
            face.Children.Add(dot);
            face.Children.Add(check);

            var swatch = new Border
            {
                Child = face,
                Margin = new Thickness(0, 0, 10, 0),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                ToolTip = option.Label,
            };
            swatch.MouseEnter += (_, _) => Motion.Scale(scale, 1.12, 140);
            swatch.MouseLeave += (_, _) => Motion.Scale(scale, 1, 200);
            swatch.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                _settings.Color = color;
            };
            swatches.Add(swatch);
        }
        SwatchList.ItemsSource = swatches;

        foreach (var option in NotesSettings.FontSizeOptions)
        {
            double size = option.Value;
            var button = new Button
            {
                Style = (Style)Resources["PaperPillButton"],
                Content = option.Label,
                Margin = new Thickness(0, 0, 8, 0),
                MinWidth = 70,
            };
            button.Click += (_, _) => _settings.FontSize = size;
            _fontButtons[size] = button;
            FontSizeList.Children.Add(button);
        }
    }

    private void ShowPage(bool note)
    {
        var show = note ? (FrameworkElement)NotePage : CustomizePage;
        var hide = note ? (FrameworkElement)CustomizePage : NotePage;
        if (show.Visibility == Visibility.Visible) return;
        hide.Visibility = Visibility.Collapsed;
        show.Visibility = Visibility.Visible;
        Motion.PopIn(show, new Vector(note ? -14 : 14, 0), 180);
    }

    private void OnCustomizeClick(object sender, RoutedEventArgs e) => ShowPage(note: false);

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        ShowPage(note: true);
        FocusEditor();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => EditorPopup.IsOpen = false;

    // ------------------------------------------------------------------ Note sheet

    private void OnCardClick(object sender, MouseButtonEventArgs e)
    {
        if (IsPreview || DockDragHelper.JustDragged) return;
        // StaysOpen=false closes when card is clicked; don't let the same click reopen it.
        if (DateTime.UtcNow - _closedAt < TimeSpan.FromMilliseconds(250)) return;
        OpenEditor();
    }

    public override bool OnCompactClick()
    {
        OpenEditor();
        return true;
    }

    private void OpenEditor()
    {
        if (IsPreview || EditorPopup.IsOpen) return;
        NotePage.Visibility = Visibility.Visible;
        CustomizePage.Visibility = Visibility.Collapsed;
        OpenPopup(EditorPopup, Root);
    }

    private void OnPopupOpened(object? sender, EventArgs e)
    {
        Host.ActivateForInput();
        FocusEditor();
    }

    private async void FocusEditor()
    {
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Input);
        NoteBox.Focus();
        Keyboard.Focus(NoteBox);
        NoteBox.CaretIndex = NoteBox.Text.Length;
    }

    private void OnPopupClosed(object? sender, EventArgs e)
    {
        _closedAt = DateTime.UtcNow;
        Save();
        Keyboard.ClearFocus();
    }

    private void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading) return;
        UpdatePreview();
        if (IsPreview) return;
        _dirty = true;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void OnEditorKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        EditorPopup.IsOpen = false;
        e.Handled = true;
    }

    private void Save()
    {
        _saveTimer.Stop();
        if (!_dirty) return;
        _dirty = false;
        JsonStore.SaveData(StateKey, new NoteData { Text = NoteBox.Text, UpdatedAt = DateTime.Now });
    }

    protected override void UpdateCompact(CompactTile tile)
    {
        tile.ShowGlyph(Descriptor.Icon, "NoteTextBrush");
        tile.Text = null;
    }

    public override void AddContextMenuItems(ItemCollection items)
    {
        items.Add(DockMenu.Item("Open note", "\uE70F", OpenEditor, !IsPreview));
        items.Add(DockMenu.Submenu("Color", "\uE790",
            NotesSettings.ColorOptions.Select(o => DockMenu.Check(o.Label, _settings.Color == o.Value, () => _settings.Color = o.Value))));
        items.Add(DockMenu.Item("Clear note", "\uE894", () => NoteBox.Clear(), NoteBox.Text.Length > 0));
    }
}
