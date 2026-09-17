using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Threading;
using CustomDock.Shell;
using CustomDock.Widgets;

namespace CustomDock.Core;

/// <summary>config.json'ı yükler/taşır, değişiklikleri gecikmeli kaydeder ve öğe başına widget ayarlarını yönetir.</summary>
public sealed class ConfigService
{
    private readonly DispatcherTimer _saveTimer;
    private readonly Dictionary<string, ObservableObject> _itemSettings = new();

    public ConfigService()
    {
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _saveTimer.Tick += (_, _) => SaveNow();
    }

    public AppConfig Config { get; private set; } = new();

    /// <summary>Yüklenmemiş (varsayılan) yapılandırma asla diske yazılmaz; ör. ikinci örnek kapanırken.</summary>
    public bool IsLoaded { get; private set; }

    public void Load()
    {
        bool firstRun = !File.Exists(AppPaths.ConfigFile);
        Config = firstRun ? new AppConfig() : ReadConfig();
        Config.IsFirstRun = firstRun;

        if (firstRun)
        {
            Config.Items = DefaultItems.Create();
            if (StartupManager.InstallerChoice() is { } startWithWindows)
                Config.StartWithWindows = startWithWindows;
        }
        else if (Config.Version < AppConfig.CurrentVersion)
        {
            MigrateFromV1(Config);
        }

        Config.Items.RemoveAll(i => i.Kind == DockItemKind.Widget && WidgetRegistry.Find(i.Widget) is null);
        Config.Version = AppConfig.CurrentVersion;
        Config.PropertyChanged += (_, _) => ScheduleSave();
        Config.ItemsChanged += (_, _) => ScheduleSave();
        IsLoaded = true;
        SaveNow();
    }

    private static AppConfig ReadConfig()
    {
        try
        {
            var node = JsonNode.Parse(File.ReadAllText(AppPaths.ConfigFile), documentOptions: new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            }) as JsonObject;
            if (node is null) return new AppConfig { Items = DefaultItems.Create() };

            // v1 enum değerlerini yeni değerlere çevir (aksi halde JSON okunamaz)
            MapEnum(node, "taskbarMode", ("HideTaskbar", "Replace"));
            MapEnum(node, "backdrop", ("Mica", "Blur"), ("Transparent", "Solid"));
            return node.Deserialize<AppConfig>(JsonStore.Options) ?? new AppConfig();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "config.json okunamadı, varsayılanlar kullanılıyor");
            try { File.Copy(AppPaths.ConfigFile, $"{AppPaths.ConfigFile}.corrupt-{DateTime.Now:yyyyMMddHHmmss}", overwrite: true); } catch { /* yoksay */ }
            var config = new AppConfig { Items = DefaultItems.Create() };
            return config;
        }
    }

    private static void MapEnum(JsonObject node, string property, params (string From, string To)[] map)
    {
        if (node[property] is JsonValue value && value.TryGetValue(out string? text))
        {
            foreach (var (from, to) in map)
            {
                if (string.Equals(text, from, StringComparison.OrdinalIgnoreCase))
                    node[property] = to;
            }
        }
    }

    /// <summary>v1 (sabit widget listesi + pinned-apps.json) → v2 (serbest öğe listesi).</summary>
    private static void MigrateFromV1(AppConfig config)
    {
        Log.Info("config.json v1 → v2 taşınıyor.");
        var items = new List<DockItem>();

        var pinned = JsonStore.LoadData<LegacyPinnedStore>("pinned-apps");
        if (pinned.Apps is { Count: > 0 })
            items.AddRange(pinned.Apps.Where(a => !string.IsNullOrWhiteSpace(a.Path)).Select(a => DockItem.App(a.Path, a.Name)));
        else
            items.AddRange(DefaultItems.ImportTaskbarPins());

        var widgetItems = new List<DockItem>();
        foreach (var entry in config.Widgets ?? new List<LegacyWidgetEntry>())
        {
            if (!entry.Enabled || entry.Id == "pinned-apps") continue;
            var (type, variant) = entry.Id switch
            {
                "clock" => ("clock", "analog"),
                "system" => ("system", "rings"),
                "media" => ("media", "full"),
                "weather" => ("weather", "current"),
                "hydration" => ("hydration", "timer"),
                "time-progress" => ("time-progress", "bar"),
                "notes" => ("notes", "sticky"),
                "reminders" => ("reminders", "list"),
                "world-clock" => ("world-clock", "multi"),
                _ => ("", ""),
            };
            if (type.Length == 0) continue;

            var item = DockItem.ForWidget(type, variant);
            if (config.WidgetSettings?.TryGetValue(entry.Id, out var settings) == true)
                item.Settings = settings.DeepClone().AsObject();
            widgetItems.Add(item);
        }

        if (items.Count > 0 && widgetItems.Count > 0)
            items.Add(DockItem.Separator());
        items.AddRange(widgetItems);

        // Eski tek not dosyasını ilk not öğesine taşı
        if (widgetItems.FirstOrDefault(i => i.Widget == "notes") is { } noteItem && File.Exists(JsonStore.DataPath("notes")))
        {
            try { File.Copy(JsonStore.DataPath("notes"), JsonStore.DataPath("notes-" + noteItem.Id), overwrite: true); } catch { /* yoksay */ }
        }

        config.Items = items;
        config.Widgets = null;
        config.WidgetSettings = null;
        config.ReserveSpace = null;
        // Kullanıcı DockHub'ın görev çubuğunun yerini almasını istiyor.
        config.TaskbarMode = TaskbarMode.Replace;
        if (config.Backdrop == BackdropKind.Acrylic) config.Backdrop = BackdropKind.Blur;
        // v1 çok yer kaplıyordu: Windows görev çubuğu kalınlığında (48 DIP) küçük dock ile başla.
        config.Size = DockSize.Small;
        config.EdgeMargin = Math.Min(config.EdgeMargin, 6);
    }

    // ------------------------------------------------------------------ Öğeler

    public DockItem? FindItem(string id) => Config.Items.FirstOrDefault(i => i.Id == id);

    public void AddItem(DockItem item, int index = -1)
    {
        if (index < 0 || index > Config.Items.Count) Config.Items.Add(item);
        else Config.Items.Insert(index, item);
        Config.NotifyItemsChanged();
    }

    public void RemoveItem(string id)
    {
        if (Config.Items.RemoveAll(i => i.Id == id) > 0)
        {
            _itemSettings.Remove(id);
            Config.NotifyItemsChanged();
        }
    }

    /// <summary>Öğeyi <paramref name="newIndex"/> konumuna taşır (taşıma öncesi listeye göre ekleme indeksi).</summary>
    public void MoveItem(string id, int newIndex)
    {
        int oldIndex = Config.Items.FindIndex(i => i.Id == id);
        if (oldIndex < 0) return;
        var item = Config.Items[oldIndex];
        Config.Items.RemoveAt(oldIndex);
        if (newIndex > oldIndex) newIndex--;
        newIndex = Math.Clamp(newIndex, 0, Config.Items.Count);
        Config.Items.Insert(newIndex, item);
        if (oldIndex != newIndex) Config.NotifyItemsChanged();
    }

    public void ReplaceItems(IEnumerable<DockItem> items)
    {
        Config.Items = items.ToList();
        Config.NotifyItemsChanged();
    }

    // ------------------------------------------------------------------ Widget ayarları

    /// <summary>
    /// Bir öğenin ayar nesnesini döndürür. Aynı örnek widget ve ayarlar penceresi arasında paylaşılır;
    /// değişiklikler öğenin <see cref="DockItem.Settings"/> alanına yazılır.
    /// </summary>
    public T GetItemSettings<T>(DockItem item) where T : ObservableObject, new()
        => (T)GetItemSettings(item, typeof(T));

    public ObservableObject GetItemSettings(DockItem item, Type settingsType)
    {
        if (_itemSettings.TryGetValue(item.Id, out var cached) && cached.GetType() == settingsType)
            return cached;

        ObservableObject? settings = null;
        if (item.Settings is not null)
        {
            try
            {
                settings = (ObservableObject?)item.Settings.Deserialize(settingsType, JsonStore.Options);
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Widget ayarı okunamadı: {item.Widget}/{item.Id}");
            }
        }

        settings ??= (ObservableObject)Activator.CreateInstance(settingsType)!;
        settings.PropertyChanged += (_, _) => StoreItemSettings(item, settings);
        _itemSettings[item.Id] = settings;
        StoreItemSettings(item, settings);
        return settings;
    }

    private void StoreItemSettings(DockItem item, ObservableObject settings)
    {
        item.Settings = JsonSerializer.SerializeToNode(settings, settings.GetType(), JsonStore.Options)!.AsObject();
        ScheduleSave();
    }

    public void ScheduleSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public void SaveNow()
    {
        _saveTimer.Stop();
        if (!IsLoaded) return;
        JsonStore.Save(AppPaths.ConfigFile, Config);
    }

    private sealed class LegacyPinnedStore
    {
        public List<LegacyPinnedApp>? Apps { get; set; }
    }

    private sealed class LegacyPinnedApp
    {
        public string Path { get; set; } = "";
        public string? Name { get; set; }
    }
}
