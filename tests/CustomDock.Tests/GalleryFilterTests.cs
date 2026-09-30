using CustomDock.Core;
using CustomDock.Dock;
using CustomDock.Settings;
using CustomDock.Widgets;
using CustomDock.Widgets.Web;

namespace CustomDock.Tests;

public class GalleryFilterTests
{
    // As the gallery builds them in Turkish: names and descriptions translated, the English texts as keywords.
    private static readonly GalleryEntry Weather = new("weather", "Hava durumu", "Anlık hava ve saatlik tahmin", WidgetCategories.Weather,
        new[] { "Weather", "Current weather and hourly forecast", "Weather", "Hava durumu", "Hourly forecast", "Saatlik tahmin" });
    private static readonly GalleryEntry Clock = new("clock", "Saat", "Analog veya dijital saat", WidgetCategories.Clocks,
        new[] { "Clock", "Analog or digital clock", "Clocks", "Saatler" });
    private static readonly GalleryEntry WorldClock = new("world-clock", "Dünya saati", "Başka şehirlerde saat kaç", WidgetCategories.Clocks,
        new[] { "World clock", "Time in other cities", "Clocks", "Saatler" });
    private static readonly GalleryEntry Stopwatch = new("stopwatch", "Kronometre", "Saati başlatın ve durdurun", WidgetCategories.Clocks,
        new[] { "Stopwatch", "Start and stop", "Clocks", "Saatler" });
    private static readonly GalleryEntry Recycle = new("recycle-bin", "Geri dönüşüm kutusu", "Çöp kutusunu boşaltın", WidgetCategories.System,
        new[] { "Recycle bin", "Empty the bin", "System", "Sistem" });
    private static readonly GalleryEntry Ticker = new("ticker", "Hisse", "Borsa fiyatları", GalleryFilter.Community,
        new[] { "Stocks", "Stock prices", "Jane", "Topluluk", "web" });

    private static readonly GalleryEntry[] All = { Weather, Clock, WorldClock, Stopwatch, Recycle, Ticker };

    private static IReadOnlyList<string> Ids(string? query, string? category = null)
        => GalleryFilter.Apply(All, query, category).Select(e => e.Id).ToList();

    [Fact]
    public void An_empty_search_shows_everything_in_order()
    {
        Assert.Equal(All.Select(e => e.Id), Ids(null));
        Assert.Equal(All.Select(e => e.Id), Ids("   "));
    }

    [Fact]
    public void The_translated_and_the_English_name_both_find_a_widget()
    {
        Assert.Equal(new[] { "weather" }, Ids("hava"));
        Assert.Equal(new[] { "weather" }, Ids("WEATHER"));
        Assert.Equal(new[] { "weather" }, Ids("forecast"));
    }

    [Fact]
    public void Case_and_accents_do_not_matter()
    {
        Assert.Equal(new[] { "world-clock" }, Ids("DUNYA"));
        Assert.Equal(new[] { "recycle-bin" }, Ids("geri donusum"));
        // German and Spanish names reach the same way.
        var german = new GalleryEntry("weather", "Wetter", "Aktuelles Wetter und Vorhersage", WidgetCategories.Weather, new[] { "Weather" });
        var spanish = new GalleryEntry("clock", "Reloj", "Reloj analógico o digital", WidgetCategories.Clocks, new[] { "Clock" });
        Assert.Single(GalleryFilter.Apply(new[] { german }, "WETTER", null));
        Assert.Single(GalleryFilter.Apply(new[] { spanish }, "analogico", null));
        // Turkish dotted and dotless i.
        var lighting = new GalleryEntry("brightness", "Işık", "Ekran parlaklığı", WidgetCategories.System, new[] { "Brightness" });
        Assert.Single(GalleryFilter.Apply(new[] { lighting }, "isik", null));
        Assert.Single(GalleryFilter.Apply(new[] { lighting }, "IŞIK", null));
    }

    [Fact]
    public void Every_word_has_to_match()
    {
        Assert.Equal(new[] { "world-clock" }, Ids("dünya saat"));
        Assert.Empty(Ids("dünya kronometre"));
    }

    [Fact]
    public void Names_starting_with_the_search_come_first()
    {
        // "saat": Saat (name starts with it), Dünya saati (name contains it), then Hava durumu (description: saatlik),
        // Kronometre (description: saati); the gallery order stays within each group.
        Assert.Equal(new[] { "clock", "world-clock", "weather", "stopwatch" }, Ids("saat"));
    }

    [Fact]
    public void A_category_shows_only_its_widgets()
    {
        Assert.Equal(new[] { "clock", "world-clock", "stopwatch" }, Ids(null, WidgetCategories.Clocks));
        Assert.Equal(new[] { "world-clock" }, Ids("dünya", WidgetCategories.Clocks));
        Assert.Empty(Ids("hava", WidgetCategories.Clocks));
        Assert.Equal(new[] { "ticker" }, Ids(null, GalleryFilter.Community));
        Assert.True(Ticker.IsCommunity);
        Assert.False(Weather.IsCommunity);
    }

    [Fact]
    public void Nothing_found_is_an_empty_list()
    {
        Assert.Empty(Ids("zzz"));
        Assert.Empty(GalleryFilter.Apply(Array.Empty<GalleryEntry>(), "saat", null));
    }

    [Fact]
    public void Community_widgets_are_found_by_author_and_web()
    {
        Assert.Equal(new[] { "ticker" }, Ids("jane"));
        Assert.Equal(new[] { "ticker" }, Ids("stock"));
    }

    [Fact]
    public void Copies_on_the_dock_are_counted_in_folders_too()
    {
        var items = new List<DockItem>
        {
            DockItem.ForWidget("weather"),
            DockItem.App(@"C:\a.exe"),
            DockItem.Group("Work", new List<DockItem> { DockItem.ForWidget("Weather"), DockItem.ForWidget("clock") }),
        };
        Assert.Equal(2, GalleryFilter.CountOnDock(items, "weather"));
        Assert.Equal(1, GalleryFilter.CountOnDock(items, "clock"));
        Assert.Equal(0, GalleryFilter.CountOnDock(items, "notes"));
        Assert.Equal(0, GalleryFilter.CountOnDock(new List<DockItem>(), "weather"));
    }

    [Fact]
    public void Layouts_without_a_width_show_as_standard()
    {
        Assert.Equal(WidgetWidth.Standard, GalleryFilter.DisplayWidth(WidgetWidth.Auto));
        Assert.Equal(WidgetWidth.Compact, GalleryFilter.DisplayWidth(WidgetWidth.Compact));
        Assert.Equal(WidgetWidth.Wide, GalleryFilter.DisplayWidth(WidgetWidth.Wide));
    }

    [Fact]
    public void The_permission_summary_names_what_a_web_widget_can_reach()
    {
        Assert.Equal("No internet access, no notifications", GalleryFilter.PermissionSummary(new WebWidgetPermissions(), k => k));

        var permissions = new WebWidgetPermissions
        {
            Network = new() { "api.example.com", "*.cdn.example.com" },
            NetworkFromSettings = new() { "server" },
            Notifications = true,
        };
        Assert.Equal("Internet: api.example.com, *.cdn.example.com · Internet: the address in “Server address” · Notifications",
            GalleryFilter.PermissionSummary(permissions, key => key == "server" ? "Server address" : key));
    }
}

public class NewWidgetDragTests
{
    [Theory]
    [InlineData("weather", "hourly")]
    [InlineData("web.example.stocks", "wide")]
    [InlineData("clock", null)]
    public void The_widget_and_its_layout_come_back(string id, string? variant)
        => Assert.Equal((id, variant), NewWidgetDrag.Decode(NewWidgetDrag.Encode(id, variant)));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\nhourly")]
    public void Anything_else_is_not_a_new_widget(string? data) => Assert.Null(NewWidgetDrag.Decode(data));

    [Fact]
    public void An_empty_layout_means_the_default_one()
        => Assert.Equal(("clock", (string?)null), NewWidgetDrag.Decode("clock\n"));
}

/// <summary>Needs WPF's data object: runs on Windows (CI).</summary>
public class NewWidgetDragDataTests
{
    [Fact]
    public void A_new_widget_survives_the_drag_and_is_not_a_dock_item()
    {
        var data = DockDragHelper.StringData(DockDragHelper.NewWidgetFormat, NewWidgetDrag.Encode("weather", "hourly"));
        Assert.Equal(("weather", "hourly"), NewWidgetDrag.Decode(DockDragHelper.ReadString(data, DockDragHelper.NewWidgetFormat)));
        Assert.Null(DockDragHelper.ReadString(data, DockDragHelper.ItemFormat));
    }
}

/// <summary>Needs the widget registry, which loads WPF: runs on Windows (CI).</summary>
public class GalleryRegistryTests
{
    [Fact]
    public void Every_widget_gets_a_card_that_its_names_find()
    {
        var entries = WidgetRegistry.All.Select(GalleryEntry.From).ToList();
        Assert.Equal(WidgetRegistry.All.Count, entries.Count);
        foreach (var descriptor in WidgetRegistry.All)
        {
            Assert.Contains(GalleryFilter.Apply(entries, descriptor.EnglishName, null), e => e.Id == descriptor.Id);
            Assert.Contains(GalleryFilter.Apply(entries, null, descriptor.Category), e => e.Id == descriptor.Id);
        }
    }
}
