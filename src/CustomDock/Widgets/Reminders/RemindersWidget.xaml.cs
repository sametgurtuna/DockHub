using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using CustomDock.Core;
using CustomDock.Dock;
using CustomDock.Services;

namespace CustomDock.Widgets;

/// <summary>Reminders: list / next / count. Opens editor popup on click.</summary>
public partial class RemindersWidget : WidgetBase
{
    public sealed record CountModel(int Count);

    public RemindersWidget()
    {
        InitializeComponent();
        PendingList.ItemsSource = Reminders.Items;
    }

    private static ReminderService Reminders => AppServices.Reminders;

    protected override void OnAttached()
    {
        Reminders.Items.CollectionChanged += OnItemsChanged;
        AppServices.Clock.MinuteTick += OnMinuteTick;
    }

    protected override void OnDetached()
    {
        Reminders.Items.CollectionChanged -= OnItemsChanged;
        AppServices.Clock.MinuteTick -= OnMinuteTick;
        EditorPopup.IsOpen = false;
    }

    protected override void OnVariantChanged()
    {
        ShowLayout(Layout_list, Layout_next, Layout_count);
        Render();
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) => Render();

    private void OnMinuteTick(object? sender, DateTime e) => Render();

    private void Render()
    {
        RenderCore();
        RefreshCompact();
    }

    protected override void UpdateCompact(CompactTile tile)
    {
        var items = Reminders.Items;
        tile.ShowGlyph(Descriptor.Icon, items.Count > 0 ? "AccentBlueBrush" : "TextSecondaryBrush");
        if (items.Count == 0)
        {
            tile.Text = null;
            return;
        }
        var due = items[0].Due;
        var culture = CultureInfo.CurrentCulture;
        tile.Text = due.Date == DateTime.Today ? due.ToString("HH:mm", culture) : due.ToString("d MMM", culture);
    }

    public override bool OnCompactClick()
    {
        OpenEditor();
        return true;
    }

    private void RenderCore()
    {
        var items = Reminders.Items;
        int count = items.Count;
        var model = new CountModel(count);
        ListCount.Content = model;
        NextCount.Content = model;
        Layout_count.Content = model;

        ListItems.ItemsSource = items.Take(3).ToList();
        ListEmpty.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;

        if (count == 0)
        {
            NextText.Text = "No reminders";
            NextTime.Text = "Click to add";
            ToolTip = "Click to add reminder";
            return;
        }

        var next = items[0];
        NextText.Text = next.Text;
        NextTime.Text = DescribeDue(next.Due);
        ToolTip = string.Join("\n", items.Take(8).Select(r => $"{r.Due:ddd HH:mm}  {r.Text}"))
                  + (count > 8 ? $"\n+{count - 8} more" : "");
    }

    internal static string DescribeDue(DateTime due)
    {
        var now = DateTime.Now;
        var diff = due - now;
        if (diff < TimeSpan.FromMinutes(1)) return "now";
        if (diff < TimeSpan.FromHours(1)) return $"in {Math.Ceiling(diff.TotalMinutes):0}m";
        if (due.Date == now.Date) return $"today {due:HH:mm}";
        if (due.Date == now.Date.AddDays(1)) return $"tomorrow {due:HH:mm}";
        return due.ToString("d MMM HH:mm", CultureInfo.CurrentCulture);
    }

    public override void AddContextMenuItems(ItemCollection items)
    {
        items.Add(DockMenu.Item("Add reminder…", "\uE710", OpenEditor));
    }

    // ------------------------------------------------------------------ Popup

    private void OnBodyClick(object sender, MouseButtonEventArgs e) => OpenEditor();

    private void OpenEditor()
    {
        if (IsPreview) return;
        PrepareEditor();
        OpenPopup(EditorPopup);
    }

    private void PrepareEditor()
    {
        var culture = CultureInfo.CurrentCulture;
        var days = new List<Option<DateTime>>
        {
            new(DateTime.Today, "Today"),
            new(DateTime.Today.AddDays(1), "Tomorrow"),
        };
        for (int i = 2; i < 7; i++)
        {
            var d = DateTime.Today.AddDays(i);
            days.Add(new Option<DateTime>(d, d.ToString("dddd, d MMM", culture)));
        }
        DayCombo.ItemsSource = days;
        SetDue(RoundUp(DateTime.Now.AddMinutes(30), 5));
        NewText.Text = "";
        ErrorText.Text = "";
    }

    private async void OnPopupOpened(object? sender, EventArgs e)
    {
        Host.ActivateForInput();
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Input);
        NewText.Focus();
        Keyboard.Focus(NewText);
    }

    private static DateTime RoundUp(DateTime time, int minutes)
    {
        var rounded = new DateTime(time.Year, time.Month, time.Day, time.Hour, time.Minute / minutes * minutes, 0);
        return rounded < time ? rounded.AddMinutes(minutes) : rounded;
    }

    private void SetDue(DateTime due)
    {
        var days = (IReadOnlyList<Option<DateTime>>)DayCombo.ItemsSource;
        var match = days.FirstOrDefault(d => d.Value == due.Date);
        if (match is null)
        {
            match = new Option<DateTime>(due.Date, due.ToString("dddd, d MMM", CultureInfo.CurrentCulture));
            DayCombo.ItemsSource = days.Append(match).ToList();
        }
        DayCombo.SelectedItem = match;
        TimeBox.Text = due.ToString("HH:mm", CultureInfo.InvariantCulture);
    }

    private void OnQuickClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag } && int.TryParse(tag, out int minutes))
            SetDue(DateTime.Now.AddMinutes(minutes));
    }

    private void OnEditorKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            TryAdd();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            EditorPopup.IsOpen = false;
            e.Handled = true;
        }
    }

    private void OnAddClick(object sender, RoutedEventArgs e) => TryAdd();

    private void TryAdd()
    {
        var text = NewText.Text.Trim();
        if (text.Length == 0)
        {
            ErrorText.Text = "Please enter a reminder text.";
            NewText.Focus();
            return;
        }

        if (DayCombo.SelectedItem is not Option<DateTime> day || !TimeInput.TryParse(TimeBox.Text, out var time))
        {
            ErrorText.Text = "Enter time in HH:mm format.";
            TimeBox.Focus();
            return;
        }

        var due = day.Value.Date + time;
        if (due <= DateTime.Now)
        {
            ErrorText.Text = "Cannot set a reminder in the past.";
            return;
        }

        Reminders.Add(text, due);
        NewText.Text = "";
        ErrorText.Text = "";
        SetDue(RoundUp(DateTime.Now.AddMinutes(30), 5));
        NewText.Focus();
    }

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is Reminder reminder)
            Reminders.Remove(reminder);
    }
}
