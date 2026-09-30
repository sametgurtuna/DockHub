using System.Windows;
using System.Windows.Controls;
using CustomDock.Core;

namespace CustomDock.Dock;

public sealed partial class GroupItemView
{
    // ------------------------------------------------------------------ Rename & Context Menu

    public void PromptRename()
    {
        string current = _item.GroupName ?? "Folder";
        string? newName = RenameFolderDialog.Prompt(current, Window.GetWindow(this));
        if (!string.IsNullOrWhiteSpace(newName) && newName != current)
        {
            _item.GroupName = newName;
            RefreshAppearance();
            AppServices.ConfigService.ScheduleSave();
            AppServices.Config.NotifyItemsChanged();
        }
    }

    private void OnContextMenuOpening(object sender, ContextMenuEventArgs e) => BuildContextMenu();

    /// <summary>Opens the right-click menu from the keyboard.</summary>
    public void OpenContextMenu()
    {
        BuildContextMenu();
        ContextMenu.PlacementTarget = this;
        ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
        ContextMenu.IsOpen = true;
    }

    private void BuildContextMenu()
    {
        CloseFan();
        var menu = ContextMenu;
        menu.Items.Clear();
        menu.Items.Add(DockMenu.Header(_item.GroupName ?? "Folder"));

        menu.Items.Add(DockMenu.Item("Open folder", "\uE8B7", OpenFan));
        menu.Items.Add(DockMenu.Item("Rename folder…", "\uE8AC", PromptRename));

        // Color submenu
        menu.Items.Add(DockMenu.Submenu("Folder color", "\uE790", AccentColors.Select(ac =>
            DockMenu.Check(ac.Name, (_item.GroupAccent ?? "AccentBlueBrush") == ac.BrushKey, () =>
            {
                _item.GroupAccent = ac.BrushKey;
                RefreshAppearance();
                AppServices.ConfigService.ScheduleSave();
                AppServices.Config.NotifyItemsChanged();
            }))));

        menu.Items.Add(DockMenu.Separator());
        menu.Items.Add(DockMenu.Item("Add application…", "\uE710", () => App.Instance.ShowAppPicker(_item.Id)));

        var children = _item.Children ?? new List<DockItem>();
        if (children.Count > 0)
        {
            menu.Items.Add(DockMenu.Separator());
            menu.Items.Add(DockMenu.Item("Ungroup all", "\uE8C8", () => AppServices.ConfigService.UngroupAll(_item.Id)));
        }

        menu.Items.Add(DockMenu.Item("Remove folder…", "\uE77A", () => ConfirmRemoveFolder(_item, Window.GetWindow(this))));
    }

    /// <summary>
    /// Removing a folder with items asks what to do with them; moving them back to the dock is the default,
    /// so a stray click never deletes apps and widgets (with their settings) at once.
    /// </summary>
    public static void ConfirmRemoveFolder(DockItem folder, Window? owner)
    {
        var service = AppServices.ConfigService;
        int count = folder.Children?.Count ?? 0;
        if (count == 0)
        {
            service.RemoveItem(folder.Id);
            return;
        }

        string? choice = ConfirmDialog.Show(
            L.T("Remove “{0}”", folder.GroupName ?? L.T("Folder")),
            count == 1 ? L.T("This folder contains 1 item.") : L.T("This folder contains {0} items.", count),
            "", owner,
            new DialogButton("cancel", L.T("Cancel"), IsCancel: true),
            new DialogButton("delete", L.T("Delete all"), DialogButtonKind.Danger),
            new DialogButton("move", L.T("Move items to dock"), DialogButtonKind.Primary));

        switch (choice)
        {
            case "move":
                service.UngroupAll(folder.Id);
                break;
            case "delete":
                service.RemoveItem(folder.Id);
                break;
        }
    }
}
