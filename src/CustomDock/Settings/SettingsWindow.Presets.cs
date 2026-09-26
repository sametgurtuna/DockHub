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
        foreach (var preset in LayoutPresets.All)
        {
            var title = new TextBlock { Text = preset.Name, FontWeight = FontWeights.SemiBold, FontSize = 13.5 };
            var description = new TextBlock { Text = preset.Description, TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 4, 0, 10) };
            description.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            var apply = new Button { Content = "Apply", HorizontalAlignment = HorizontalAlignment.Left, Cursor = Cursors.Hand };
            var p = preset;
            apply.Click += (_, _) => LayoutPresets.Apply(p, AppServices.ConfigService);

            var card = new Border
            {
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(14, 12, 14, 12),
                Margin = new Thickness(0, 0, 8, 8),
                Child = new DockPanel
                {
                    LastChildFill = false,
                    Children = { WithDock(title, System.Windows.Controls.Dock.Top), WithDock(description, System.Windows.Controls.Dock.Top), WithDock(apply, System.Windows.Controls.Dock.Bottom) },
                },
                ToolTip = "Keeps your pinned apps and folders. You can undo it from the dock's right-click menu.",
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
