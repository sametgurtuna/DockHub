using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CustomDock.Core;
using CustomDock.Dock;
using CustomDock.Services;

namespace CustomDock.Widgets;

public sealed class CurrencySettings : ObservableObject
{
    private string _base = "USD";
    private string _targets = "TRY, EUR";

    public string Base { get => _base; set => Set(ref _base, (value ?? "USD").Trim().ToUpperInvariant()); }

    /// <summary>Comma separated currency codes shown against the base currency.</summary>
    public string Targets { get => _targets; set => Set(ref _targets, value ?? ""); }

    public IReadOnlyList<string> TargetList => Targets.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries);
}

/// <summary>Exchange rates with the daily change and a two-week trend line (ECB data via Frankfurter).</summary>
public partial class CurrencyWidget : WidgetBase
{
    public const string Icon = "M12,3 A9,9 0 1 1 11.99,3 Z M14.8,8.5 C14.2,7.6 13.2,7.2 12,7.2 C10.4,7.2 9.3,8 9.3,9.2 C9.3,11.9 14.9,10.8 14.9,13.8 C14.9,15.1 13.6,16 12,16 C10.6,16 9.5,15.4 9,14.4 M12,5.8 V7.2 M12,16 V17.8";

    private static readonly DispatcherTimer SharedTimer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMinutes(60) };
    private static event Action? RefreshAll;
    private CurrencySettings _settings = new();
    private List<CurrencyQuote> _quotes = new();
    private string? _error;

    static CurrencyWidget()
    {
        SharedTimer.Tick += (_, _) => RefreshAll?.Invoke();
    }

    public CurrencyWidget()
    {
        InitializeComponent();
    }

    protected override void OnAttached()
    {
        _settings = GetSettings<CurrencySettings>();
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
        ShowLayout(Layout_single, Layout_list);
        Render();
    }

    private async Task LoadAsync()
    {
        if (IsPreview)
        {
            _quotes = new()
            {
                new("USD", "TRY", 48.933, 48.855, new[] { 48.78, 48.8, 48.81, 48.84, 48.86, 48.93 }, DateTime.Today),
                new("USD", "EUR", 0.8801, 0.8797, new[] { 0.874, 0.876, 0.879, 0.878, 0.8797, 0.8801 }, DateTime.Today),
            };
            Render();
            return;
        }
        try
        {
            _quotes = await CurrencyService.GetAsync(_settings.Base, _settings.TargetList);
            _error = _quotes.Count == 0 ? L.T("Choose currencies in the widget settings.") : null;
        }
        catch (Exception ex)
        {
            _error = L.T("Rates couldn't be loaded. Retrying in an hour.");
            Log.Warn($"Currency rates failed: {ex.Message}");
        }
        Render();
    }

    private static string Format(double rate) => rate.ToString(rate >= 100 ? "N2" : rate >= 1 ? "N3" : "N4", CultureInfo.CurrentCulture);

    private static string Change(CurrencyQuote quote) => quote.ChangePercent is { } change
        ? $"{(change > 0.005 ? "▲" : change < -0.005 ? "▼" : "•")} {Math.Abs(change).ToString("0.00", CultureInfo.CurrentCulture)}%"
        : "";

    private static string ChangeBrush(CurrencyQuote quote) => quote.ChangePercent switch
    {
        > 0.005 => "AccentGreenBrush",
        < -0.005 => "AccentRedBrush",
        _ => "TextSecondaryBrush",
    };

    private void Render()
    {
        var first = _quotes.FirstOrDefault();
        if (first is null)
        {
            PairText.Text = $"{_settings.Base}";
            RateText.Text = "—";
            ChangeText.Text = "";
            Trend.SetData(Array.Empty<double>(), Array.Empty<double>());
        }
        else
        {
            PairText.Text = $"{first.Base}/{first.Target}";
            RateText.Text = Format(first.Rate);
            ChangeText.Text = Change(first);
            ChangeText.SetResourceReference(TextBlock.ForegroundProperty, ChangeBrush(first));
            Trend.SetResourceReference(Controls.Sparkline.PrimaryStrokeProperty, ChangeBrush(first) == "AccentRedBrush" ? "AccentRedBrush" : "AccentGreenBrush");
            Trend.SetData(first.History.ToList(), Array.Empty<double>());
        }

        Layout_list.Children.Clear();
        foreach (var quote in _quotes.Take(4))
        {
            var change = new TextBlock { Text = Change(quote), FontSize = 10.5 };
            change.SetResourceReference(TextBlock.ForegroundProperty, ChangeBrush(quote));
            Layout_list.Children.Add(new StackPanel
            {
                Margin = new Thickness(0, 0, 14, 0),
                Children =
                {
                    new TextBlock { Text = quote.Target, Style = (Style)FindResource("CaptionText") },
                    new TextBlock { Text = Format(quote.Rate), Style = (Style)FindResource("TitleText"), FontSize = 13 },
                    change,
                },
            });
        }

        var lines = _quotes.Select(q => $"1 {q.Base} = {Format(q.Rate)} {q.Target}  {Change(q)}").ToList();
        if (first is not null) lines.Add(L.T("ECB rate of {0}", first.Date.ToString("d", CultureInfo.CurrentCulture)));
        if (_error is not null) lines.Add(_error);
        ToolTip = string.Join("\n", lines);
        Opacity = _error is not null && first is null ? 0.6 : 1;
        RefreshCompact();
    }

    protected override void UpdateCompact(CompactTile tile)
    {
        var first = _quotes.FirstOrDefault();
        tile.ShowGlyph(System.Windows.Media.Geometry.Parse(Icon), first is null ? "TextSecondaryBrush" : ChangeBrush(first));
        tile.Text = first is null ? null : Format(first.Rate);
    }

    public override void AddContextMenuItems(ItemCollection items)
    {
        items.Add(DockMenu.Item(L.T("Refresh now"), "", () => _ = LoadAsync()));
    }
}
