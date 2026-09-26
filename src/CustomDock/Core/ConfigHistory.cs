using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CustomDock.Core;

/// <summary>One undoable change: the dock items (and optionally appearance) as they were before it.</summary>
public sealed class HistoryEntry
{
    public required string Description { get; init; }
    public required string ItemsJson { get; init; }
    public JsonObject? Appearance { get; init; }
    public bool Destructive { get; init; }
    public List<(string Original, string Trashed)> TrashedFiles { get; } = new();
}

/// <summary>
/// Undo stack for dock changes. Snapshots are JSON; restoring reuses existing <see cref="DockItem"/> instances by id,
/// because dock views and widget settings hold references to them.
/// </summary>
public sealed class ConfigHistory
{
    private const int Capacity = 20;

    /// <summary>Appearance settings captured for changes that touch them (presets, profiles).</summary>
    public static readonly string[] AppearanceProperties =
    {
        nameof(AppConfig.Edge), nameof(AppConfig.Theme), nameof(AppConfig.Backdrop), nameof(AppConfig.TintOpacity),
        nameof(AppConfig.Size), nameof(AppConfig.Layout), nameof(AppConfig.WidthMode), nameof(AppConfig.Alignment),
        nameof(AppConfig.EdgeMargin), nameof(AppConfig.AutoHide), nameof(AppConfig.HoverEffect),
        nameof(AppConfig.ShowClock), nameof(AppConfig.ShowTray), nameof(AppConfig.ShowRunningApps),
        nameof(AppConfig.ShowSearchButton), nameof(AppConfig.ShowTaskViewButton), nameof(AppConfig.ShowStartButton),
    };

    private readonly LinkedList<HistoryEntry> _entries = new();
    private int _batchDepth;

    public HistoryEntry? Latest => _entries.Last?.Value;

    public bool CanUndo => _entries.Count > 0;

    /// <summary>Raised after an entry is added (the undo toast listens for destructive ones) or undone.</summary>
    public event Action<HistoryEntry, bool>? Changed;

    /// <summary>Records the current state before a change. Nested calls inside <see cref="Batch"/> are ignored.</summary>
    public HistoryEntry? Push(AppConfig config, string description, bool destructive = false, bool includeAppearance = false)
    {
        if (_batchDepth > 0) return null;
        var entry = new HistoryEntry
        {
            Description = description,
            ItemsJson = JsonSerializer.Serialize(config.Items, JsonStore.Options),
            Appearance = includeAppearance ? CaptureAppearance(config) : null,
            Destructive = destructive,
        };
        _entries.AddLast(entry);
        while (_entries.Count > Capacity) _entries.RemoveFirst();
        Changed?.Invoke(entry, false);
        return entry;
    }

    /// <summary>Groups several changes (e.g. a drag with multiple moves) into one undo step.</summary>
    public IDisposable Batch(AppConfig config, string description, bool destructive = false)
    {
        Push(config, description, destructive);
        _batchDepth++;
        return new BatchScope(this);
    }

    /// <summary>Restores the latest snapshot. Returns it, or null when there's nothing to undo.</summary>
    public HistoryEntry? Undo(AppConfig config)
    {
        if (_entries.Last is not { } node) return null;
        _entries.RemoveLast();
        var entry = node.Value;

        var restored = JsonSerializer.Deserialize<List<DockItem>>(entry.ItemsJson, JsonStore.Options) ?? new();
        var existing = Flatten(config.Items).GroupBy(i => i.Id).ToDictionary(g => g.Key, g => g.First());
        config.Items = restored.Select(item => Reuse(item, existing)).ToList();

        if (entry.Appearance is { } appearance) RestoreAppearance(config, appearance);
        ItemDataStore.Restore(entry.TrashedFiles);
        Changed?.Invoke(entry, true);
        return entry;
    }

    /// <summary>Keeps the live instance for items that still exist, updated to the snapshot's state.</summary>
    private static DockItem Reuse(DockItem snapshot, Dictionary<string, DockItem> existing)
    {
        if (!existing.TryGetValue(snapshot.Id, out var live) || live.Kind != snapshot.Kind)
        {
            if (snapshot.Children is { } newChildren)
                snapshot.Children = newChildren.Select(c => Reuse(c, existing)).ToList();
            return snapshot;
        }

        live.Path = snapshot.Path;
        live.Arguments = snapshot.Arguments;
        live.Name = snapshot.Name;
        live.Widget = snapshot.Widget;
        live.Variant = snapshot.Variant;
        live.PinnedEnd = snapshot.PinnedEnd;
        live.GroupName = snapshot.GroupName;
        live.GroupAccent = snapshot.GroupAccent;
        live.Children = snapshot.Children?.Select(c => Reuse(c, existing)).ToList();
        return live;
    }

    private static JsonObject CaptureAppearance(AppConfig config)
    {
        var node = new JsonObject();
        foreach (var name in AppearanceProperties)
        {
            var property = typeof(AppConfig).GetProperty(name, BindingFlags.Public | BindingFlags.Instance)!;
            node[name] = JsonSerializer.SerializeToNode(property.GetValue(config), property.PropertyType, JsonStore.Options);
        }
        return node;
    }

    private static void RestoreAppearance(AppConfig config, JsonObject appearance)
    {
        foreach (var (name, value) in appearance)
        {
            var property = typeof(AppConfig).GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (property?.CanWrite != true || value is null) continue;
            try { property.SetValue(config, value.Deserialize(property.PropertyType, JsonStore.Options)); }
            catch (Exception ex) { Log.Error(ex, $"Failed to restore {name}"); }
        }
    }

    private static IEnumerable<DockItem> Flatten(IEnumerable<DockItem> items) => ItemDataStore.Flatten(items);

    private sealed class BatchScope : IDisposable
    {
        private ConfigHistory? _owner;

        public BatchScope(ConfigHistory owner) => _owner = owner;

        public void Dispose()
        {
            if (_owner is null) return;
            _owner._batchDepth--;
            _owner = null;
        }
    }
}
