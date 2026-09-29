using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CustomDock.Core;

/// <summary>One undoable change: the dock items (and optionally appearance) as they were before it.</summary>
public sealed class HistoryEntry
{
    public required string Description { get; init; }
    public required string ItemsJson { get; init; }
    public JsonObject? Appearance { get; init; }
    /// <summary>Destructive steps show the undo toast; an edit session becomes one when it changed something.</summary>
    public bool Destructive { get; set; }
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
    private HistoryEntry? _openEntry;

    public HistoryEntry? Latest => _entries.Last?.Value;

    public bool CanUndo => _batchDepth == 0 && _entries.Count > 0;

    /// <summary>True while a step is open (edit mode); undo waits until it closes.</summary>
    public bool IsStepOpen => _batchDepth > 0;

    /// <summary>Raised after an entry is added (the undo toast listens for destructive ones) or undone.</summary>
    public event Action<HistoryEntry, bool>? Changed;

    /// <summary>
    /// Records the current state before a change. Inside <see cref="Batch"/> or <see cref="BeginSession"/> no new step is
    /// added; the open step is returned instead, so data files trashed meanwhile still come back with its undo.
    /// </summary>
    public HistoryEntry? Push(AppConfig config, string description, bool destructive = false, bool includeAppearance = false)
    {
        if (_batchDepth > 0) return _openEntry;
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
        var entry = Push(config, description, destructive);
        if (_batchDepth++ == 0) _openEntry = entry;
        return new BatchScope(this);
    }

    /// <summary>
    /// Opens one undo step that lasts until <see cref="HistorySession.End"/>: every change made meanwhile (moves,
    /// removals, new widgets, other layouts) is undone together. Used by the dock's edit mode.
    /// </summary>
    public HistorySession BeginSession(AppConfig config, string description)
    {
        var scope = Batch(config, description);
        return new HistorySession(this, _openEntry, scope, LayoutOf(config.Items));
    }

    /// <summary>Called by <see cref="HistorySession.End"/>.</summary>
    internal bool EndSession(HistorySession session, AppConfig config, bool announce)
    {
        if (session.Entry is not { } entry) return false;
        // Only what undo gives back counts: a widget's own settings changing meanwhile (an alarm that went off) is not
        // an edit of the dock.
        bool changed = entry.TrashedFiles.Count > 0 || LayoutOf(config.Items) != session.StartLayout;
        if (!changed)
        {
            // Nothing changed: the session leaves no step behind.
            if (_entries.Last?.Value == entry) _entries.RemoveLast();
            else _entries.Remove(entry);
            return false;
        }
        entry.Destructive = announce;
        Changed?.Invoke(entry, false);
        return true;
    }

    /// <summary>The part of the items that <see cref="Undo"/> restores (see <see cref="Reuse"/>), as comparable text.</summary>
    internal static string LayoutOf(IEnumerable<DockItem> items)
    {
        var text = new StringBuilder();
        void Append(IEnumerable<DockItem> list)
        {
            foreach (var item in list)
            {
                text.Append('{').Append(item.Id).Append('\u001f').Append(item.Kind).Append('\u001f').Append(item.Path)
                    .Append('\u001f').Append(item.Arguments).Append('\u001f').Append(item.Name).Append('\u001f').Append(item.Widget)
                    .Append('\u001f').Append(item.Variant).Append('\u001f').Append(item.PinnedEnd).Append('\u001f').Append(item.Display)
                    .Append('\u001f').Append(item.GroupName).Append('\u001f').Append(item.GroupAccent);
                if (item.Children is { } children)
                {
                    text.Append('[');
                    Append(children);
                    text.Append(']');
                }
                text.Append('}');
            }
        }
        Append(items);
        return text.ToString();
    }

    /// <summary>Restores the latest snapshot. Returns it, or null when there's nothing to undo.</summary>
    public HistoryEntry? Undo(AppConfig config)
    {
        // While a step is still open (edit mode) its changes can't be undone yet.
        if (_batchDepth > 0 || _entries.Last is not { } node) return null;
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
        live.Display = snapshot.Display;
        live.GroupName = snapshot.GroupName;
        live.GroupAccent = snapshot.GroupAccent;
        live.Children = snapshot.Children?.Select(c => Reuse(c, existing)).ToList();
        return live;
    }

    public static JsonObject CaptureAppearance(AppConfig config)
    {
        var node = new JsonObject();
        foreach (var name in AppearanceProperties)
        {
            var property = typeof(AppConfig).GetProperty(name, BindingFlags.Public | BindingFlags.Instance)!;
            node[name] = JsonSerializer.SerializeToNode(property.GetValue(config), property.PropertyType, JsonStore.Options);
        }
        return node;
    }

    public static void RestoreAppearance(AppConfig config, JsonObject appearance)
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
            if (--_owner._batchDepth == 0) _owner._openEntry = null;
            _owner = null;
        }
    }
}

/// <summary>One undo step kept open while the dock is edited (see <see cref="ConfigHistory.BeginSession"/>).</summary>
public sealed class HistorySession
{
    private readonly ConfigHistory _owner;
    private IDisposable? _scope;

    internal HistorySession(ConfigHistory owner, HistoryEntry? entry, IDisposable scope, string startLayout)
    {
        _owner = owner;
        Entry = entry;
        _scope = scope;
        StartLayout = startLayout;
    }

    /// <summary>The step; null only if the history refused to record one.</summary>
    public HistoryEntry? Entry { get; }

    public bool IsOpen => _scope is not null;

    internal string StartLayout { get; }

    /// <summary>
    /// Closes the step. Returns true when the dock changed: the step stays and, with <paramref name="announce"/>, is
    /// announced like a removal (undo toast); otherwise it is dropped, so opening and closing edit mode leaves no trace
    /// in the undo history.
    /// </summary>
    public bool End(AppConfig config, bool announce = true)
    {
        if (_scope is null) return false;
        _scope.Dispose();
        _scope = null;
        return _owner.EndSession(this, config, announce);
    }
}
