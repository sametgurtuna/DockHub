using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CustomDock.Controls;
using CustomDock.Core;

namespace CustomDock.Settings;

/// <summary>Settings › Appearance › Layout presets.</summary>
public partial class SettingsWindow
{
    private void LoadPresets()
    {
        PresetPanel.Children.Clear();
        var custom = _config.CustomPresets.ToList();
        foreach (var preset in LayoutPresets.All.Concat(custom.Select(LayoutPresets.FromCustom)))
        {
            bool isCustom = custom.Any(c => c.Id == preset.Id);
            var title = new TextBlock { Text = preset.Name, FontWeight = FontWeights.SemiBold, FontSize = 13.5 };
            var description = new TextBlock { Text = preset.Description, TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 4, 0, 10) };
            description.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            var apply = new Button { Content = "Apply", HorizontalAlignment = HorizontalAlignment.Left, Cursor = Cursors.Hand };
            var p = preset;
            apply.Click += (_, _) => LayoutPresets.Apply(p, AppServices.ConfigService);
            UIElement buttons = apply;
            if (isCustom)
            {
                var delete = new Button { Content = "Delete", Margin = new Thickness(8, 0, 0, 0), Cursor = Cursors.Hand };
                delete.Click += (_, _) =>
                {
                    LayoutPresets.DeleteCustom(p.Id, AppServices.ConfigService);
                    LoadPresets();
                };
                buttons = new StackPanel { Orientation = Orientation.Horizontal, Children = { apply, delete } };
            }

            var card = new Border
            {
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(14, 12, 14, 12),
                Margin = new Thickness(0, 0, 8, 8),
                Child = new DockPanel
                {
                    LastChildFill = false,
                    Children = { WithDock(title, System.Windows.Controls.Dock.Top), WithDock(description, System.Windows.Controls.Dock.Top), WithDock(buttons, System.Windows.Controls.Dock.Bottom) },
                },
                ToolTip = L.T("Keeps your pinned apps and folders. You can undo it from the dock's right-click menu."),
            };
            card.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
            card.SetResourceReference(Border.BorderBrushProperty, "SurfaceBorderBrush");
            PresetPanel.Children.Add(card);
        }
    }

    private static UIElement WithDock(UIElement element, System.Windows.Controls.Dock dock)
    {
        DockPanel.SetDock(element, dock);
        return element;
    }
}
