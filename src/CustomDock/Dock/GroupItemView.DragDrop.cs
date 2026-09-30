using System.Windows;
using CustomDock.Controls;
using CustomDock.Core;
using CustomDock.Services;

namespace CustomDock.Dock;

public sealed partial class GroupItemView
{
    // ------------------------------------------------------------------ Drag and Drop handling

    private void OnDragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DockDragHelper.ItemFormat) ||
            e.Data.GetDataPresent(DockDragHelper.RunningAppFormat) ||
            e.Data.GetDataPresent(DockDragHelper.NewWidgetFormat) ||
            e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
            Motion.Fade(_hover, 1, 100);
            AnimatePress(1.12);
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DockDragHelper.ItemFormat) ||
            e.Data.GetDataPresent(DockDragHelper.RunningAppFormat) ||
            e.Data.GetDataPresent(DockDragHelper.NewWidgetFormat) ||
            e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }
    }

    private void OnDragLeave(object sender, DragEventArgs e)
    {
        Motion.Fade(_hover, 0, 160);
        AnimatePress(1);
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        Motion.Fade(_hover, 0, 160);
        AnimatePress(1);

        var config = AppServices.ConfigService;

        if (e.Data.GetData(DockDragHelper.ItemFormat) is string itemId)
        {
            e.Handled = true;
            if (itemId == _item.Id) return;
            var existing = config.FindItem(itemId);
            if (existing is null || existing.Kind == DockItemKind.Group) return;

            config.RemoveItem(itemId);
            config.AddToGroup(_item.Id, existing);
            RefreshAppearance();
        }
        else if (e.Data.GetData(DockDragHelper.RunningAppFormat) is string key &&
                 App.Instance.Shell?.RunningApps.Find(key) is { } group &&
                 AppLauncher.PinItem(group) is { } pinItem)
        {
            e.Handled = true;
            config.AddToGroup(_item.Id, pinItem);
            RefreshAppearance();
        }
        else if (DockDragHelper.NewWidgetItem(e.Data) is { } widget)
        {
            // A widget dragged from the gallery goes into the folder.
            e.Handled = true;
            config.AddToGroup(_item.Id, widget);
            RefreshAppearance();
        }
        else if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            e.Handled = true;
            foreach (var file in files.Where(f => !string.IsNullOrWhiteSpace(f)))
                config.AddToGroup(_item.Id, DockItem.App(file));
            RefreshAppearance();
        }
    }
}
