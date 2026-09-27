using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using CustomDock.Core;
using CustomDock.Native;
using CustomDock.Services;
using CustomDock.Shell;
using ManagedShell.WindowsTasks;

namespace CustomDock.Dock;

public sealed partial class AppButton
{
    private void BuildContextMenu()
    {
        WindowPreviewWindow.Instance.HidePreview();
        _previewTimer.Stop();

        var menu = ContextMenu;
        menu.Items.Clear();
        menu.Items.Add(DockMenu.Header(Title));

        var windows = _group?.Windows ?? new List<ApplicationWindow>();
        if (windows.Count > 0)
        {
            menu.Items.Add(DockMenu.Separator());
            foreach (var window in windows.Take(12))
            {
                var w = window;
                string title = string.IsNullOrWhiteSpace(w.Title) ? Title : Trim(w.Title, 50);
                var item = DockMenu.Item(title, null, () =>
                {
                    if (w.IsMinimized) w.Restore();
                    w.BringToFront();
                });
                item.FontWeight = w.State == ApplicationWindow.WindowState.Active ? FontWeights.SemiBold : FontWeights.Normal;
                menu.Items.Add(item);
            }
        }

        string? resolvedExe = null;
        if (LaunchPath is { } lPath && !lPath.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
            resolvedExe = lPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? lPath : ShellIcons.ResolveShortcut(lPath);

        // --- Jump List (Tasks) ---
        var tasks = JumpListService.GetTasks(resolvedExe);
        if (tasks.Count > 0)
        {
            menu.Items.Add(DockMenu.Separator());
            foreach (var task in tasks)
            {
                menu.Items.Add(DockMenu.Item(task.Title, task.Glyph, task.Action));
            }
        }

        // --- Jump List (Recent Items) ---
        var recentItems = JumpListService.GetRecentItems(resolvedExe);
        if (recentItems.Count > 0)
        {
            menu.Items.Add(DockMenu.Separator());
            foreach (var recent in recentItems)
            {
                var r = recent;
                var item = DockMenu.Item(r.Title, null, () =>
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo(r.Path) { UseShellExecute = true });
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, $"Failed to open recent item: {r.Path}");
                    }
                });
                if (r.Icon is not null)
                {
                    item.Icon = new Image { Source = r.Icon, Width = 16, Height = 16, Margin = new Thickness(0, 0, 8, 0) };
                }
                menu.Items.Add(item);
            }
        }

        menu.Items.Add(DockMenu.Separator());
        menu.Items.Add(DockMenu.Item(windows.Count > 0 ? "New window" : "Open", "\uE8A7", StartNewInstance, LaunchPath is not null));

        if (resolvedExe is not null && resolvedExe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            menu.Items.Add(DockMenu.Item("Run as administrator", "\uE7EF", () => AppLauncher.RunAsAdmin(resolvedExe)));
            menu.Items.Add(DockMenu.Item("Open file location", "\uE8B7", () => AppLauncher.OpenLocation(resolvedExe)));
        }

        menu.Items.Add(DockMenu.Separator());
        if (Item is not null)
        {
            menu.Items.Add(DockMenu.Item("Unpin from dock", "\uE77A", () => AppServices.ConfigService.RemoveItem(Item.Id)));
        }
        else if (_group is not null && AppLauncher.PinItem(_group) is { } pinItem)
        {
            menu.Items.Add(DockMenu.Item(AppInfo.PinLabel, "\uE718", () =>
                AppServices.ConfigService.AddItem(pinItem, DockItemsIndex.EndOfApps())));
        }

        if (windows.Count > 0)
        {
            menu.Items.Add(DockMenu.Separator());
            menu.Items.Add(DockMenu.Item(windows.Count > 1 ? $"Close all windows ({windows.Count})" : "Close window", "\uE711",
                () => { foreach (var w in windows.ToList()) w.Close(); }));
            menu.Items.Add(DockMenu.Item(windows.Count > 1 ? "End processes" : "End process", "\uE9CE",
                () => TerminateWindows(windows.ToList())));
        }
    }

    /// <summary>Immediately and forcefully terminates processes owning the windows (like Task Manager "End Task").</summary>
    private static void TerminateWindows(IReadOnlyList<ApplicationWindow> windows)
    {
        foreach (var w in windows)
        {
            try
            {
                if (w.Handle != IntPtr.Zero)
                    NativeMethods.EndTask(w.Handle, false, true);
            }
            catch { /* ignore */ }
        }

        var pids = new HashSet<uint>();
        foreach (var w in windows)
        {
            uint pid = w.ProcId ?? 0;
            if (pid == 0)
                NativeMethods.GetWindowThreadProcessId(w.Handle, out pid);

            if (pid > 4 && pid != (uint)Environment.ProcessId)
            {
                pids.Add(pid);
            }
        }

        _ = Task.Run(() =>
        {
            foreach (uint pid in pids)
            {
                try
                {
                    using var process = Process.GetProcessById((int)pid);
                    if (process.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase))
                        continue;

                    process.Kill(true);
                }
                catch (Exception ex)
                {
                    Log.Warn($"Process {pid} could not be terminated directly, trying taskkill: {ex.Message}");
                    try
                    {
                        using var p = Process.Start(new ProcessStartInfo
                        {
                            FileName = "taskkill",
                            Arguments = $"/F /T /PID {pid}",
                            CreateNoWindow = true,
                            UseShellExecute = false,
                        });
                        p?.WaitForExit(1000);
                    }
                    catch (Exception taskKillEx)
                    {
                        Log.Error(taskKillEx, $"taskkill failed for PID {pid}");
                    }
                }
            }
        });
    }
}
