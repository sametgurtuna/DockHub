using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using CustomDock.Controls;
using CustomDock.Core;
using CustomDock.Dock;
using CustomDock.Services;

namespace CustomDock.Widgets;

public sealed class StockSettings : ObservableObject
{
    private string _symbols = "AAPL, MSFT";

    /// <summary>Symbols separated by commas, for example "AAPL, MSFT, ^SPX".</summary>
    public string Symbols { get => _symbols; set => Set(ref _symbols, value ?? ""); }

    public IReadOnlyList<string> SymbolList => StockService.SplitSymbols(Symbols);
}

/// <summary>Stock prices with the change since the previous close and a month's trend (Stooq, delayed, no API key).</summary>
public sealed class StocksWidget : WidgetBase
{
    public const string Icon = "M3,17 L9,11 L13,15 L21,7 M15,7 H21 V13";

    private static readonly DispatcherTimer SharedTimer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMinutes(15) };
    private static event Action? RefreshAll;

    private readonly StackPanel _singleLayout;
    private readonly TextBlock _symbol;
    private readonly TextBlock _price;
    private readonly TextBlock _change;
    private readonly Sparkline _trend;
    private readonly StackPanel _listLayout;
    private readonly Popup _popup;
    private readonly StackPanel _panel;
    private StockSettings _settings = new();
    private List<StockQuote> _quotes = new();
    private string? _error;

    static StocksWidget()
    {
        // Nothing shows the prices while every dock is hidden; the next tick after that fetches them.
        SharedTimer.Tick += (_, _) =>
        {
            if (DockVisibility.IsAnyDockVisible) RefreshAll?.Invoke();
        };
    }

    public StocksWidget()
    {
        Background = System.Windows.Media.Brushes.Transparent;
        _symbol = WidgetUi.Text("CaptionText");
        _price = WidgetUi.Text("TitleText", "", 15);
        _change = new TextBlock { FontSize = 11, Margin = new Thickness(6, 2, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        System.Windows.Documents.Typography.SetNumeralAlignment(_price, FontNumeralAlignment.Tabular);
        System.Windows.Documents.Typography.SetNumeralAlignment(_change, FontNumeralAlignment.Tabular);
        _trend = NewTrend(54, 24);
        _trend.Margin = new Thickness(10, 0, 0, 0);
        _singleLayout = new StackPanel
        {
            Name = "Layout_single",
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Children = { _symbol, new StackPanel { Orientation = Orientation.Horizontal, Children = { _price, _change } } },
                },
                _trend,
            },
        };
        _listLayout = new StackPanel { Name = "Layout_list", Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        (_popup, _, _panel) = WidgetUi.Flyout(FlyoutSize.Standard, L.T("Stocks"), null);
        Content = new Grid { Background = System.Windows.Media.Brushes.Transparent, Children = { _singleLayout, _listLayout, _popup } };
        Cursor = System.Windows.Input.Cursors.Hand;
        MouseLeftButtonUp += (_, e) =>
        {
            if (DockDragHelper.JustDragged) return;
            e.Handled = true;
            BuildPanel();
            OpenPopup(_popup);
        };
    }

    private static Sparkline NewTrend(double width, double height)
    {
        var trend = new Sparkline { Width = width, Height = height, VerticalAlignment = VerticalAlignment.Center, SecondaryStroke = System.Windows.Media.Brushes.Transparent };
        trend.SetResourceReference(Sparkline.PrimaryStrokeProperty, "AccentGreenBrush");
        return trend;
    }

    protected override void OnAttached()
    {
        _settings = GetSettings<StockSettings>();
        _settings.PropertyChanged += OnSettingsChanged;
        RefreshAll += OnRefreshRequested;
        SharedTimer.Start();
        _ = LoadAsync();
    }

    protected override void OnDetached()
    {
        _settings.PropertyChanged -= OnSettingsChanged;
        RefreshAll -= OnRefreshRequested;
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e) => _ = LoadAsync();

    private void OnRefreshRequested() => _ = LoadAsync();

    protected override void OnVariantChanged()
    {
        ShowLayout(_singleLayout, _listLayout);
        Render();
    }

    private async Task LoadAsync()
    {
        if (IsPreview)
        {
            _quotes = new()
            {
                new("AAPL", 227.52, 224.81, new[] { 219.8, 221.4, 220.9, 223.6, 224.81, 227.52 }, DateTime.Today),
                new("MSFT", 431.18, 432.9, new[] { 425.1, 428.6, 433.2, 434.0, 432.9, 431.18 }, DateTime.Today),
            };
            Render();
            return;
        }
        var symbols = _settings.SymbolList;
        if (symbols.Count == 0)
        {
            _quotes = new();
            _error = L.T("Add stock symbols in the widget settings.");
            Render();
            return;
        }
        try
        {
            _quotes = await StockService.GetAsync(symbols);
            _error = _quotes.Count == 0 ? L.T("No prices found for these symbols.") : null;
        }
        catch (Exception ex)
        {
            _error = L.T("Prices couldn't be loaded. Retrying in 15 minutes.");
            Log.Warn($"Stock prices failed: {ex.Message}");
        }
        Render();
    }

    private static string Format(double price) => price.ToString(price >= 1 ? "N2" : "N4", CultureInfo.CurrentCulture);

    private static string Change(StockQuote quote) => quote.ChangePercent is { } change
        ? $"{(change > 0.005 ? "▲" : change < -0.005 ? "▼" : "•")} {Math.Abs(change).ToString("0.00", CultureInfo.CurrentCulture)}%"
        : "";

    private static string ChangeBrush(StockQuote quote) => quote.ChangePercent switch
    {
        > 0.005 => "AccentGreenBrush",
        < -0.005 => "AccentRedBrush",
        _ => "TextSecondaryBrush",
    };

    private string Source => _quotes.Count == 0
        ? L.T("Delayed prices from Stooq")
        : L.T("Delayed prices from Stooq, {0}", _quotes.Max(q => q.Date).ToString("d", CultureInfo.CurrentCulture));

    private void Render()
    {
        var first = _quotes.FirstOrDefault();
        if (first is null)
        {
            _symbol.Text = L.T("Stocks");
            _price.Text = "—";
            _change.Text = "";
            _trend.SetData(Array.Empty<double>(), Array.Empty<double>());
        }
        else
        {
            _symbol.Text = first.Symbol;
            _price.Text = Format(first.Close);
            _change.Text = Change(first);
            _change.SetResourceReference(TextBlock.ForegroundProperty, ChangeBrush(first));
            _trend.SetResourceReference(Sparkline.PrimaryStrokeProperty, ChangeBrush(first) == "AccentRedBrush" ? "AccentRedBrush" : "AccentGreenBrush");
            _trend.SetData(first.History, Array.Empty<double>());
        }

        _listLayout.Children.Clear();
        foreach (var quote in _quotes.Take(4))
        {
            var change = new TextBlock { Text = Change(quote), FontSize = 10.5 };
            change.SetResourceReference(TextBlock.ForegroundProperty, ChangeBrush(quote));
            var price = WidgetUi.Text("TitleText", Format(quote.Close), 13);
            _listLayout.Children.Add(new StackPanel
            {
                Margin = new Thickness(0, 0, 14, 0),
                Children = { WidgetUi.Text("CaptionText", quote.Symbol), price, change },
            });
        }
        if (_quotes.Count == 0) _listLayout.Children.Add(new StackPanel { Children = { WidgetUi.Text("CaptionText", L.T("Stocks")), WidgetUi.Text("TitleText", "—", 13) } });

        var lines = _quotes.Select(q => $"{q.Symbol}  {Format(q.Close)}  {Change(q)}").ToList();
        lines.Add(Source);
        if (_error is not null) lines.Add(_error);
        ToolTip = string.Join("\n", lines);
        Opacity = _error is not null && first is null ? 0.6 : 1;
        if (_popup.IsOpen) BuildPanel();
        RefreshCompact();
    }

    /// <summary>The panel: every symbol with its price, change and trend, and where the prices come from.</summary>
    private void BuildPanel()
    {
        _panel.Children.Clear();
        if (_quotes.Count == 0)
        {
            _panel.Children.Add(WidgetUi.EmptyState("", _error ?? L.T("No prices yet"), L.T("Symbols like AAPL or MSFT (US), or with a market: SAP.DE, ^SPX."),
                L.T("Widget settings"), () =>
                {
                    ClosePopup(_popup);
                    App.Instance.ShowSettings("items", Item.Id);
                }));
            return;
        }
        foreach (var quote in _quotes)
        {
            var grid = new Grid { Margin = new Thickness(4, 5, 4, 5) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var name = WidgetUi.Text("TitleText", quote.Symbol, 13);
            name.VerticalAlignment = VerticalAlignment.Center;
            var values = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(8, 0, 10, 0) };
            var price = WidgetUi.Text("TitleText", Format(quote.Close), 13);
            price.HorizontalAlignment = HorizontalAlignment.Right;
            var change = new TextBlock { Text = Change(quote), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right };
            change.SetResourceReference(TextBlock.ForegroundProperty, ChangeBrush(quote));
            values.Children.Add(price);
            values.Children.Add(change);
            var trend = NewTrend(70, 26);
            trend.SetResourceReference(Sparkline.PrimaryStrokeProperty, ChangeBrush(quote) == "AccentRedBrush" ? "AccentRedBrush" : "AccentGreenBrush");
            trend.SetData(quote.History, Array.Empty<double>());
            Grid.SetColumn(values, 1);
            Grid.SetColumn(trend, 2);
            grid.Children.Add(name);
            grid.Children.Add(values);
            grid.Children.Add(trend);
            _panel.Children.Add(grid);
        }
        var source = WidgetUi.Text("CaptionText", Source);
        source.Margin = new Thickness(4, 8, 4, 0);
        source.TextWrapping = TextWrapping.Wrap;
        _panel.Children.Add(source);
        if (_error is not null)
        {
            var error = WidgetUi.Text("CaptionText", _error);
            error.Margin = new Thickness(4, 2, 4, 0);
            error.TextWrapping = TextWrapping.Wrap;
            _panel.Children.Add(error);
        }
    }

    protected override void UpdateCompact(CompactTile tile)
    {
        var first = _quotes.FirstOrDefault();
        tile.ShowGlyph(Descriptor.Icon, first is null ? "TextSecondaryBrush" : ChangeBrush(first));
        tile.Text = first is null ? null : Format(first.Close);
    }

    public override void AddContextMenuItems(ItemCollection items)
    {
        items.Add(DockMenu.Item(L.T("Refresh now"), "", () => _ = LoadAsync()));
    }
}
