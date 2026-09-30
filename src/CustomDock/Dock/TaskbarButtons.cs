using CustomDock.Core;

namespace CustomDock.Dock;

/// <summary>A running app for <see cref="TaskbarButtons"/>: its key (<see cref="AppKeys"/>), when it was first seen and its windows.</summary>
public sealed record RunningApp<TWindow>(string Key, long Order, IReadOnlyList<TWindow> Windows);

/// <summary>Which part of the dock a button belongs to.</summary>
public enum TaskbarSlot
{
    /// <summary>A pinned app (with its app's windows, or one of them).</summary>
    Pinned,

    /// <summary>One more window of a pinned app, right after it (buttons not combined).</summary>
    PinnedWindow,

    /// <summary>An app that isn't pinned (or one of its windows), after the dock's items.</summary>
    Running,
}

/// <summary>
/// One app button. <see cref="Window"/> is the window it stands for when buttons aren't combined; null for a button that
/// stands for all of an app's windows (or none: a pinned app that isn't running).
/// </summary>
public sealed record TaskbarButton<TWindow>(TaskbarSlot Slot, string? PinId, string Key, TWindow? Window) where TWindow : class;

/// <summary>
/// The app buttons of a dock, in order. Combined (Always), each pinned app has one button for all its windows and every
/// other running app one button after the dock's items, in the order the apps were first seen. Not combined (Never),
/// every window has a button: a pinned app's button becomes its first window's and its other windows follow right after
/// it; the other apps' windows follow the dock's items app by app, so an app's windows stay side by side.
/// </summary>
public static class TaskbarButtons
{
    /// <param name="pins">The dock's pinned apps in order: item id and app key.</param>
    /// <param name="folderKeys">Apps in folders: pinned too (the folder shows that they run), so they get no button.</param>
    /// <param name="running">The running apps; with <see cref="CombineButtons.Never"/> only the windows to show here.</param>
    public static IReadOnlyList<TaskbarButton<TWindow>> Layout<TWindow>(IReadOnlyList<(string Id, string Key)> pins,
        IReadOnlySet<string> folderKeys, IReadOnlyList<RunningApp<TWindow>> running, CombineButtons mode) where TWindow : class
    {
        var byKey = new Dictionary<string, RunningApp<TWindow>>();
        foreach (var app in running) byKey.TryAdd(app.Key, app);

        var buttons = new List<TaskbarButton<TWindow>>();
        var pinnedKeys = new HashSet<string>(folderKeys);
        var splitKeys = new HashSet<string>();
        foreach (var (id, key) in pins)
        {
            pinnedKeys.Add(key);
            // The same app pinned twice: only the first pin splits into its windows.
            if (mode == CombineButtons.Always || !splitKeys.Add(key) || !byKey.TryGetValue(key, out var app) || app.Windows.Count == 0)
            {
                buttons.Add(new(TaskbarSlot.Pinned, id, key, null));
                continue;
            }
            buttons.Add(new(TaskbarSlot.Pinned, id, key, app.Windows[0]));
            foreach (var window in app.Windows.Skip(1)) buttons.Add(new(TaskbarSlot.PinnedWindow, id, key, window));
        }

        foreach (var app in running.Where(a => !pinnedKeys.Contains(a.Key)).OrderBy(a => a.Order))
        {
            if (mode == CombineButtons.Always) buttons.Add(new(TaskbarSlot.Running, null, app.Key, null));
            else foreach (var window in app.Windows) buttons.Add(new(TaskbarSlot.Running, null, app.Key, window));
        }
        return buttons;
    }
}
