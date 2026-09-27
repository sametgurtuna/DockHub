using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CustomDock.Services;
using CustomDock.Shell;

namespace CustomDock.Dock;

public sealed partial class AppButton
{
    // ------------------------------------------------------------------ Keyboard (Win+number, dock focus)

    /// <summary>Runs what Win+number (with modifiers) does on the Windows taskbar.</summary>
    public void InvokeShortcut(AppShortcutMode mode)
    {
        switch (mode)
        {
            case AppShortcutMode.Activate:
                if (_group is not { WindowCount: > 0 } && Item is not null) BeginLaunchFeedback();
                AppLauncher.Activate(Item, _group);
                break;
            case AppShortcutMode.NewInstance:
                StartNewInstance();
                break;
            case AppShortcutMode.RunAsAdmin:
                if (LaunchPath is { } path && !path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
                    AppLauncher.RunAsAdmin(path);
                break;
            case AppShortcutMode.JumpList:
                OpenContextMenu();
                break;
        }
        AnimatePress(0.86);
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () => AnimatePress(1));
    }

    /// <summary>Opens the right-click menu from the keyboard.</summary>
    public void OpenContextMenu()
    {
        BuildContextMenu();
        ContextMenu.PlacementTarget = this;
        ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
        ContextMenu.IsOpen = true;
    }

    private StackPanel? _dots;

    private void EnsureDots()
    {
        if (_dots is not null) return;
        _dots = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 2),
            IsHitTestVisible = false,
        };
        for (int i = 0; i < 3; i++)
            _dots.Children.Add(new System.Windows.Shapes.Ellipse { Width = 4, Height = 4, Margin = new Thickness(1, 0, 1, 0) });
        Children.Add(_dots);
    }

    private Border? _numberBadge;

    /// <summary>Shows (or with null hides) the Win+number hint on the button.</summary>
    public void ShowShortcutNumber(string? number)
    {
        if (number is null)
        {
            if (_numberBadge is not null) _numberBadge.Visibility = Visibility.Collapsed;
            return;
        }
        if (_numberBadge is null)
        {
            var text = new TextBlock
            {
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            text.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
            _numberBadge = new Border
            {
                MinWidth = 16,
                Height = 16,
                CornerRadius = new CornerRadius(4),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(2, 2, 0, 0),
                Child = text,
                IsHitTestVisible = false,
            };
            _numberBadge.SetResourceReference(Border.BackgroundProperty, "PopupBrush");
            _numberBadge.SetResourceReference(Border.BorderBrushProperty, "PopupBorderBrush");
            _numberBadge.BorderThickness = new Thickness(1);
            Children.Add(_numberBadge);
        }
        ((TextBlock)_numberBadge.Child).Text = number;
        _numberBadge.Visibility = Visibility.Visible;
    }
}
