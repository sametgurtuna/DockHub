using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;
using CustomDock.Controls;

namespace CustomDock.Widgets;

/// <summary>
/// Dikey (sol/sağ) dock'ta widget'ın özet görünümü: üstte ikon/halka, altta kısa değer.
/// Widget'ın tamamı kutucuğa tıklanınca açılan panelde gösterilir.
/// </summary>
public sealed class CompactTile : Grid
{
    private readonly Border _visualHost;
    private readonly TextBlock _text;
    private readonly TextBlock _inner;
    private Path? _glyph;
    private RingGauge? _ring;
    private TextBlock? _label;

    public CompactTile()
    {
        Width = 40;
        Height = 40;
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Center;
        Background = Brushes.Transparent;
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _visualHost = new Border
        {
            Width = 24,
            Height = 24,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Children.Add(_visualHost);

        _inner = new TextBlock
        {
            FontSize = 8.5,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
        };
        _inner.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
        _inner.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        Children.Add(_inner);

        _text = new TextBlock
        {
            FontSize = 9.5,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextTrimming = TextTrimming.None,
            Margin = new Thickness(0, 0, 0, 1),
            Visibility = Visibility.Collapsed,
        };
        _text.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
        _text.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        Typography.SetNumeralAlignment(_text, FontNumeralAlignment.Tabular);
        SetRow(_text, 1);
        Children.Add(_text);
    }

    /// <summary>Kutucuğun alt satırındaki kısa metin (null → gizli, görsel ortalanır).</summary>
    public string? Text
    {
        get => _text.Text;
        set
        {
            _text.Text = value ?? "";
            bool visible = !string.IsNullOrEmpty(value);
            _text.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            _text.FontSize = value?.Length > 5 ? 8.5 : 9.5;
            if (_visualHost.Child is null || !ReferenceEquals(_visualHost.Child, _label))
                _visualHost.Width = _visualHost.Height = visible ? 22 : 26;
            SetRowSpan(_visualHost, visible ? 1 : 2);
            SetRowSpan(_inner, visible ? 1 : 2);
        }
    }

    public Brush TextBrush
    {
        set => _text.Foreground = value;
    }

    public void SetTextBrushKey(string key) => _text.SetResourceReference(TextBlock.ForegroundProperty, key);

    /// <summary>Tanımlayıcıdaki çizgi ikonunu gösterir.</summary>
    public Path ShowGlyph(Geometry icon, string brushKey)
    {
        _glyph ??= new Path
        {
            Stretch = Stretch.Uniform,
            StrokeThickness = 1.8,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Margin = new Thickness(1),
        };
        _glyph.Data = icon;
        _glyph.SetResourceReference(Shape.StrokeProperty, brushKey);
        SetVisual(_glyph);
        return _glyph;
    }

    /// <summary>İlerleme halkası gösterir; <paramref name="inner"/> halkanın içindeki kısa metindir.</summary>
    public RingGauge ShowRing(double value, double maximum, string fillKey, string trackKey, string? inner = null)
    {
        _ring ??= new RingGauge { Thickness = 2.6 };
        _ring.Maximum = maximum;
        _ring.Value = value;
        _ring.SetResourceReference(RingGauge.FillProperty, fillKey);
        _ring.SetResourceReference(RingGauge.TrackProperty, trackKey);
        SetVisual(_ring);
        SetInner(inner);
        return _ring;
    }

    public void SetInner(string? inner)
    {
        _inner.Text = inner ?? "";
        _inner.Visibility = string.IsNullOrEmpty(inner) ? Visibility.Collapsed : Visibility.Visible;
        _inner.FontSize = inner?.Length > 2 ? 7.5 : 8.5;
    }

    /// <summary>Görsel alanında büyük metin (ör. iki satırlık saat veya takvim günü).</summary>
    public TextBlock ShowLabel(string text, double fontSize = 13, string brushKey = "TextPrimaryBrush")
    {
        if (_label is null)
        {
            _label = new TextBlock
            {
                FontWeight = FontWeights.SemiBold,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            };
            _label.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
            Typography.SetNumeralAlignment(_label, FontNumeralAlignment.Tabular);
        }
        _label.Text = text;
        _label.FontSize = fontSize;
        _label.LineHeight = Math.Round(fontSize * 1.05);
        _label.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        SetVisual(_label);
        return _label;
    }

    /// <summary>Özel bir görsel (analog saat, hava ikonu, albüm kapağı...) gösterir.</summary>
    public void SetVisual(UIElement? visual)
    {
        if (!ReferenceEquals(visual, _ring)) SetInner(null);
        if (!ReferenceEquals(_visualHost.Child, visual))
            _visualHost.Child = visual;
        bool label = visual is not null && ReferenceEquals(visual, _label);
        _visualHost.ClipToBounds = false;
        if (label)
        {
            _visualHost.Width = double.NaN;
            _visualHost.Height = double.NaN;
        }
        else
        {
            _visualHost.Width = _visualHost.Height = _text.Visibility == Visibility.Visible ? 22 : 26;
        }
    }
}
