using System.Windows;
using System.Windows.Controls;
using CustomDock.Controls;
using CustomDock.Core;

namespace CustomDock.Settings;

/// <summary>Settings › General › Keyboard shortcuts.</summary>
public partial class SettingsWindow
{
    private readonly Dictionary<string, SettingRow> _hotkeyRows = new();

    private void LoadHotkeys()
    {
        HotkeyRows.Children.Clear();
        _hotkeyRows.Clear();
        foreach (var action in HotkeyActions.All.Where(a => App.Instance.HasHotkeyHandler(a.Id)))
        {
            var box = new HotkeyBox { Gesture = _config.GetHotkey(action.Id) };
            var id = action.Id;
            box.GestureChanged += gesture => _config.SetHotkey(id, gesture);

            var reset = new Button
            {
                Content = "",
                FontSize = 12,
                Width = 32,
                Height = 32,
                Margin = new Thickness(6, 0, 0, 0),
                ToolTip = action.DefaultGesture is null ? "Clear" : $"Reset to {action.DefaultGesture}",
            };
            reset.SetResourceReference(FontFamilyProperty, "IconFont");
            reset.Click += (_, _) =>
            {
                _config.Hotkeys.Remove(id);
                _config.OnPropertyChanged(nameof(AppConfig.Hotkeys));
                box.Gesture = _config.GetHotkey(id);
            };

            var row = new SettingRow
            {
                Glyph = "",
                Header = action.Name,
                Description = action.Description.Length == 0 ? null : action.Description,
                Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { box, reset } },
            };
            _hotkeyRows[id] = row;
            HotkeyRows.Children.Add(row);
        }

        if (App.Instance.Hotkeys is { } hotkeys) hotkeys.RegistrationChanged += RefreshHotkeyStatus;
        RefreshHotkeyStatus();
    }

    /// <summary>Marks shortcuts another app already uses.</summary>
    private void RefreshHotkeyStatus()
    {
        var failed = App.Instance.Hotkeys?.Failed ?? (IReadOnlyCollection<string>)Array.Empty<string>();
        foreach (var (id, row) in _hotkeyRows)
        {
            var action = HotkeyActions.Find(id)!;
            string description = action.Description;
            if (failed.Contains(id))
                description = (description.Length > 0 ? description + " " : "") + "⚠ This shortcut is already used by another app; choose a different one.";
            row.Description = description.Length == 0 ? null : description;
        }
    }
}
