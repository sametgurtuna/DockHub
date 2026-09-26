using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Threading;
using CustomDock.Shell;
using CustomDock.Widgets;

namespace CustomDock.Core;

/// <summary>Loads/migrates config.json, debounces saves on change, and manages per-item widget settings.</summary>
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

    /// <summary>An unloaded (default) configuration is never written to disk; e.g. when a second instance exits.</summary>
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

        // Widgets unknown to this version (e.g. after a downgrade) stay in the config; the dock just skips them.
        RepairAppPaths(Config.Items);
        Config.Version = AppConfig.CurrentVersion;
        Config.PropertyChanged += (_, _) => ScheduleSave();
        Config.ItemsChanged += (_, _) => ScheduleSave();
        IsLoaded = true;
        SaveNow();
    }

    /// <summary>
    /// Pins made from running apps used to store versioned install folders (Squirrel <c>app-1.2.3</c>, MSIX
    /// <c>WindowsApps\Name_1.2.3.0_...</c>) that disappear when the app updates. Rewrites them to stable launchers
    /// and finds the current folder for pins that already broke.
    /// </summary>
    private static void RepairAppPaths(IEnumerable<DockItem> items)
    {
        foreach (var item in items)
        {
            if (item.Kind == DockItemKind.Group && item.Children is { } children)
            {
                RepairAppPaths(children);
                continue;
            }
            if (item.Kind != DockItemKind.App || item.Path is not { } path ||
                !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                bool versioned = AppPathResolver.ParseSquirrel(path) is not null || AppPathResolver.ParseMsix(path) is not null;
                string current = File.Exists(path) ? path : AppPathResolver.Repair(path) ?? path;
                var (stable, arguments) = versioned ? AppPathResolver.StablePinPath(current, null) : (current, null);
                arguments ??= item.Arguments;
                if (string.Equals(stable, path, StringComparison.OrdinalIgnoreCase) && arguments == item.Arguments) continue;

                Log.Info($"Pinned app path updated: {path} -> {stable}{(arguments is null ? "" : " " + arguments)}");
                item.Path = stable;
                item.Arguments = arguments;
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Failed to check pinned path {path}");
            }
        }
    }

    /// <summary>Date of the daily backup used because config.json was corrupt (shown to the user once), or null.</summary>
    public DateTime? RecoveredFromBackup { get; private set; }

    private AppConfig ReadConfig()
    {
        try
        {
            return ParseConfig(AppPaths.ConfigFile) ?? new AppConfig { Items = DefaultItems.Create() };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to read config.json");
            try { File.Copy(AppPaths.ConfigFile, $"{AppPaths.ConfigFile}.corrupt-{DateTime.Now:yyyyMMddHHmmss}", overwrite: true); } catch { /* ignore */ }

            // Fall back to the newest daily backup before starting over with defaults.
            if (BackupService.LatestValidDailyBackup() is { } backup)
            {
                try
                {
                    if (ParseConfig(backup) is { } recovered)
                    {
                        RecoveredFromBackup = File.GetLastWriteTime(backup);
                        Log.Info($"Settings restored from daily backup {backup}.");
                        return recovered;
                    }
                }
                catch (Exception backupEx)
                {
                    Log.Error(backupEx, $"Backup {backup} is not usable either");
                }
            }
            Log.Warn("Using default settings.");
            return new AppConfig { Items = DefaultItems.Create() };
        }
    }

    private static AppConfig? ParseConfig(string path)
    {
        var node = JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip,
        }) as JsonObject;
        if (node is null) return null;

        // Map v1 enum values to new values (otherwise JSON cannot be deserialized)
        MapEnum(node, "taskbarMode", ("HideTaskbar", "Replace"));
        MapEnum(node, "backdrop", ("Mica", "Blur"), ("Transparent", "Solid"));
        return node.Deserialize<AppConfig>(JsonStore.Options) ?? new AppConfig();
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

    /// <summary>v1 (fixed widget list + pinned-apps.json) → v2 (freeform items list).</summary>
    private static void MigrateFromV1(AppConfig config)
    {
        Log.Info("Migrating config.json v1 → v2.");
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

        // Migrate legacy single notes file to first notes item
        if (widgetItems.FirstOrDefault(i => i.Widget == "notes") is { } noteItem && File.Exists(JsonStore.DataPath("notes")))
        {
            try { File.Copy(JsonStore.DataPath("notes"), JsonStore.DataPath("notes-" + noteItem.Id), overwrite: true); } catch { /* ignore */ }
        }

        config.Items = items;
        config.Widgets = null;
        config.WidgetSettings = null;
        config.ReserveSpace = null;
        // User wants DockHub to replace the taskbar.
        config.TaskbarMode = TaskbarMode.Replace;
        if (config.Backdrop == BackdropKind.Acrylic) config.Backdrop = BackdropKind.Blur;
        // v1 took too much space: start with a small dock matching Windows taskbar thickness (48 DIP).
        config.Size = DockSize.Small;
        config.EdgeMargin = Math.Min(config.EdgeMargin, 6);
    }

    // ------------------------------------------------------------------ Items

    public DockItem? FindItem(string id) =>
        Config.Items.FirstOrDefault(i => i.Id == id)
        ?? Config.Items.Where(i => i.Kind == DockItemKind.Group)
            .SelectMany(g => g.Children ?? Enumerable.Empty<DockItem>())
            .FirstOrDefault(i => i.Id == id);

    /// <summary>Finds the group that contains the given item id.</summary>
    public DockItem? FindParentGroup(string itemId) =>
        Config.Items.FirstOrDefault(g => g.Kind == DockItemKind.Group && g.Children?.Any(c => c.Id == itemId) == true);

    public void AddItem(DockItem item, int index = -1)
    {
        History.Push(Config, L.T("Added {0}", Describe(item)));
        if (index < 0 || index > Config.Items.Count) Config.Items.Add(item);
        else Config.Items.Insert(index, item);
        Config.NotifyItemsChanged();
    }

    public void RemoveItem(string id)
    {
        // Try top-level first
        var topLevel = Config.Items.FirstOrDefault(i => i.Id == id);
        if (topLevel is not null)
        {
            var entry = History.Push(Config, L.T("Removed {0}", Describe(topLevel)), destructive: true);
            Config.Items.Remove(topLevel);
            OnItemsRemoved(new[] { topLevel }, entry);
            Config.NotifyItemsChanged();
            return;
        }
        // Try inside groups
        foreach (var group in Config.Items.Where(i => i.Kind == DockItemKind.Group))
        {
            var child = group.Children?.FirstOrDefault(i => i.Id == id);
            if (child is null) continue;
            var entry = History.Push(Config, L.T("Removed {0}", Describe(child)), destructive: true);
            group.Children!.Remove(child);
            OnItemsRemoved(new[] { child }, entry);
            // Auto-delete empty groups
            if (group.Children.Count == 0)
                Config.Items.RemoveAll(i => i.Id == group.Id);
            Config.NotifyItemsChanged();
            return;
        }
    }

    /// <summary>Undo stack of dock changes.</summary>
    public ConfigHistory History { get; } = new();

    /// <summary>Reverts the latest dock change. Returns its description, or null if there was nothing to undo.</summary>
    public string? Undo()
    {
        var entry = History.Undo(Config);
        if (entry is null) return null;
        // Settings objects of items that came back are recreated from their restored JSON.
        foreach (var id in _itemSettings.Keys.ToList())
            if (FindItem(id) is null) _itemSettings.Remove(id);
        Config.NotifyItemsChanged();
        return entry.Description;
    }

    /// <summary>Short user-facing name of an item ("Weather widget", "“AI” folder").</summary>
    public static string Describe(DockItem item) => item.Kind switch
    {
        DockItemKind.App => !string.IsNullOrWhiteSpace(item.Name) ? item.Name! : Path.GetFileNameWithoutExtension(item.Path ?? "app"),
        DockItemKind.Widget => L.T("{0} widget", WidgetRegistry.Find(item.Widget)?.Name ?? L.T("Unknown")),
        DockItemKind.Group => L.T("“{0}” folder", item.GroupName ?? L.T("Folder")),
        _ => L.T("separator"),
    };

    /// <summary>Forgets cached settings of removed items (and folder contents) and trashes their data files.</summary>
    private void OnItemsRemoved(IReadOnlyList<DockItem> removed, HistoryEntry? entry)
    {
        foreach (var item in ItemDataStore.Flatten(removed))
            _itemSettings.Remove(item.Id);

        // Widgets save their state once more when the dock detaches them, so trash after the dock has rebuilt.
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            var files = ItemDataStore.Trash(removed);
            entry?.TrashedFiles.AddRange(files);
        });
    }

    /// <summary>Moves item to <paramref name="newIndex"/> position (insertion index relative to pre-move list).</summary>
    public void MoveItem(string id, int newIndex)
    {
        int oldIndex = Config.Items.FindIndex(i => i.Id == id);
        if (oldIndex < 0)
        {
            // Item might be inside a group -- pull it out to top level
            if (FindParentGroup(id) is { } source && source.Children!.FirstOrDefault(c => c.Id == id) is { } moving)
                History.Push(Config, L.T("Moved {0} out of the folder", Describe(moving)));
            var parentGroup = FindParentGroup(id);
            if (parentGroup is null) return;
            var child = parentGroup.Children!.FirstOrDefault(c => c.Id == id);
            if (child is null) return;
            parentGroup.Children!.Remove(child);
            if (parentGroup.Children.Count == 0)
                Config.Items.RemoveAll(i => i.Id == parentGroup.Id);
            newIndex = Math.Clamp(newIndex, 0, Config.Items.Count);
            Config.Items.Insert(newIndex, child);
            Config.NotifyItemsChanged();
            return;
        }
        var item = Config.Items[oldIndex];
        int target = Math.Clamp(newIndex > oldIndex ? newIndex - 1 : newIndex, 0, Config.Items.Count - 1);
        if (target != oldIndex) History.Push(Config, L.T("Moved {0}", Describe(item)));
        Config.Items.RemoveAt(oldIndex);
        if (newIndex > oldIndex) newIndex--;
        newIndex = Math.Clamp(newIndex, 0, Config.Items.Count);
        Config.Items.Insert(newIndex, item);
        if (oldIndex != newIndex) Config.NotifyItemsChanged();
    }

    public void ReplaceItems(IEnumerable<DockItem> items, string description = "Changed dock items")
    {
        History.Push(Config, description, destructive: true);
        Config.Items = items.ToList();
        Config.NotifyItemsChanged();
    }

    // ------------------------------------------------------------------ Group operations

    /// <summary>Adds an item to an existing group.</summary>
    public void AddToGroup(string groupId, DockItem item, int index = -1)
    {
        var group = Config.Items.FirstOrDefault(g => g.Id == groupId && g.Kind == DockItemKind.Group);
        if (group is null) return;
        History.Push(Config, L.T("Added {0} to {1}", Describe(item), Describe(group)));
        group.Children ??= new List<DockItem>();
        if (index < 0 || index > group.Children.Count) group.Children.Add(item);
        else group.Children.Insert(index, item);
        Config.NotifyItemsChanged();
    }

    /// <summary>Creates a new group from two existing top-level items at the position of the first one.</summary>
    public DockItem CreateGroupFromItems(string name, string itemId1, string itemId2)
    {
        int idx1 = Config.Items.FindIndex(i => i.Id == itemId1);
        int idx2 = Config.Items.FindIndex(i => i.Id == itemId2);
        if (idx1 < 0 || idx2 < 0) return DockItem.Group(name);

        var item1 = Config.Items[idx1];
        var item2 = Config.Items[idx2];
        int insertAt = Math.Min(idx1, idx2);

        Config.Items.Remove(item1);
        Config.Items.Remove(item2);

        History.Push(Config, L.T("Created “{0}” folder", name));
        var group = DockItem.Group(name, new List<DockItem> { item1, item2 });
        insertAt = Math.Clamp(insertAt, 0, Config.Items.Count);
        Config.Items.Insert(insertAt, group);
        Config.NotifyItemsChanged();
        return group;
    }

    /// <summary>Dissolves a group, moving all children back to the dock at the group's position.</summary>
    public void UngroupAll(string groupId)
    {
        int index = Config.Items.FindIndex(i => i.Id == groupId);
        if (index < 0) return;
        var group = Config.Items[index];
        if (group.Kind != DockItemKind.Group) return;
        History.Push(Config, L.T("Ungrouped {0}", Describe(group)), destructive: true);
        Config.Items.RemoveAt(index);
        var children = group.Children ?? new List<DockItem>();
        for (int i = 0; i < children.Count; i++)
            Config.Items.Insert(index + i, children[i]);
        Config.NotifyItemsChanged();
    }

    // ------------------------------------------------------------------ Widget settings

    /// <summary>
    /// Returns the settings object for an item. The same instance is shared between widget and settings window;
    /// changes are serialized back into the item's <see cref="DockItem.Settings"/> property.
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
                Log.Error(ex, $"Failed to read widget settings: {item.Widget}/{item.Id}");
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

    /// <summary>Stops writing config.json (used right before a restart that must keep an imported file).</summary>
    public void DisableSaving() { _saveTimer.Stop(); IsLoaded = false; }

    public void SaveNow()
    {
        _saveTimer.Stop();
        if (!IsLoaded) return;
        JsonStore.Save(AppPaths.ConfigFile, Config);
        BackupService.DailyBackup();
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
