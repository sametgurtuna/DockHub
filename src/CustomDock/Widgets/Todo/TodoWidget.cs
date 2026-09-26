using System.ComponentModel;
using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using CustomDock.Controls;
using CustomDock.Core;
using CustomDock.Dock;
using CustomDock.Services;

namespace CustomDock.Widgets;

public enum TodoSource
{
    Local,
    Todoist,
}

public sealed class TodoSettings : ObservableObject
{
    private TodoSource _source = TodoSource.Local;
    private string? _protectedToken;

    public TodoSource Source { get => _source; set => Set(ref _source, value); }

    /// <summary>The Todoist API token, encrypted for the current Windows user (DPAPI).</summary>
    public string? ProtectedToken { get => _protectedToken; set => Set(ref _protectedToken, value); }

    [JsonIgnore]
    public string TodoistToken
    {
        get
        {
            if (string.IsNullOrEmpty(ProtectedToken)) return "";
            try
            {
                return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(ProtectedToken), null, DataProtectionScope.CurrentUser));
            }
            catch (Exception ex) when (ex is CryptographicException or FormatException)
            {
                return "";
            }
        }
        set
        {
            value = value.Trim();
            ProtectedToken = value.Length == 0
                ? null
                : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser));
            OnPropertyChanged();
        }
    }

    public static IReadOnlyList<Option<TodoSource>> Sources { get; } = new[]
    {
        new Option<TodoSource>(TodoSource.Local, "On this PC"),
        new Option<TodoSource>(TodoSource.Todoist, "Todoist"),
    };
}

/// <summary>Settings for the to-do widget: where tasks come from and the Todoist token (typed into a password box).</summary>
public sealed class TodoSettingsView : StackPanel
{
    public TodoSettingsView(TodoSettings settings)
    {
        var source = new ComboBox { Width = 180, ItemsSource = TodoSettings.Sources, DisplayMemberPath = "Label", SelectedValuePath = "Value", SelectedValue = settings.Source };
        source.SelectionChanged += (_, _) =>
        {
            if (source.SelectedValue is TodoSource value) settings.Source = value;
        };
        Children.Add(new SettingRow
        {
            Header = L.T("Tasks from"),
            Description = L.T("Keep a simple list on this PC, or show today's and overdue tasks from Todoist."),
            Content = source,
        });

        var token = new PasswordBox { Width = 240, Password = settings.TodoistToken };
        token.LostFocus += (_, _) =>
        {
            if (token.Password != settings.TodoistToken) settings.TodoistToken = token.Password;
        };
        Children.Add(new SettingRow
        {
            Header = L.T("Todoist API token"),
            Description = L.T("Todoist › Settings › Integrations › Developer › API token. Stored encrypted for your Windows account."),
            Content = token,
        });
    }
}

/// <summary>To-do list for today: a local list or Todoist (today and overdue). Tick to complete, add from the flyout.</summary>
public sealed class TodoWidget : WidgetBase
{
    public const string Icon = "M4,6.5 L5.5,8 L8,5 M4,12.5 L5.5,14 L8,11 M4,18.5 L5.5,20 L8,17 M11,7 H20 M11,13 H20 M11,19 H17";
    private const int ShownInCard = 3;

    private readonly StackPanel _listLayout;
    private readonly TextBlock _listTitle;
    private readonly TextBlock _listCount;
    private readonly StackPanel _listRows;
    private readonly StackPanel _countLayout;
    private readonly TextBlock _countNumber;
    private readonly TextBlock _countNext;
    private readonly Popup _popup;
    private readonly StackPanel _popupRows;
    private readonly TextBlock _popupStatus;
    private readonly TextBox _newTask;
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromMinutes(5) };
    private TodoSettings _settings = new();
    private List<TodoTask> _tasks = new();
    private string? _error;
    private bool _loading;

    public TodoWidget()
    {
        Cursor = Cursors.Hand;
        Background = System.Windows.Media.Brushes.Transparent;

        _listTitle = WidgetUi.Text("TitleText", L.T("Today"), 12);
        _listCount = WidgetUi.Text("CaptionText");
        _listCount.Margin = new Thickness(6, 0, 0, 0);
        _listCount.VerticalAlignment = VerticalAlignment.Bottom;
        _listRows = new StackPanel();
        _listLayout = new StackPanel
        {
            Name = "Layout_list",
            Width = 190,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 2), Children = { _listTitle, _listCount } }, _listRows },
        };

        var (disc, _) = WidgetUi.IconDisc("", "AccentGreenBrush", 30);
        _countNumber = WidgetUi.Text("ValueText", "", 13);
        _countNumber.HorizontalAlignment = HorizontalAlignment.Center;
        _countNumber.VerticalAlignment = VerticalAlignment.Center;
        _countNext = WidgetUi.Text("CaptionText");
        _countNext.TextTrimming = TextTrimming.CharacterEllipsis;
        _countLayout = new StackPanel
        {
            Name = "Layout_count",
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new Grid { Margin = new Thickness(0, 0, 8, 0), Children = { disc, _countNumber } },
                new StackPanel { Width = 130, VerticalAlignment = VerticalAlignment.Center, Children = { WidgetUi.Text("TitleText", L.T("To do"), 12), _countNext } },
            },
        };

        (_popup, var content) = WidgetUi.PopupShell(320);
        var refresh = WidgetUi.IconButton("", L.T("Refresh"), 26, 11);
        refresh.Click += (_, _) => _ = LoadAsync();
        content.Children.Add(WidgetUi.PopupHeader(L.T("Today"), refresh));
        _popupRows = new StackPanel();
        content.Children.Add(new ScrollViewer { MaxHeight = 320, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _popupRows });
        _popupStatus = WidgetUi.Text("CaptionText");
        _popupStatus.TextWrapping = TextWrapping.Wrap;
        _popupStatus.Margin = new Thickness(6, 6, 6, 0);
        content.Children.Add(_popupStatus);
        _newTask = new TextBox { Margin = new Thickness(4, 10, 4, 0) };
        _newTask.KeyDown += OnNewTaskKeyDown;
        content.Children.Add(_newTask);
        var hint = WidgetUi.Text("MicroText", L.T("Type a task and press Enter"));
        hint.Margin = new Thickness(6, 4, 6, 0);
        hint.SetResourceReference(TextBlock.ForegroundProperty, "TextTertiaryBrush");
        content.Children.Add(hint);
        _popup.Opened += OnPopupOpened;

        Content = new Grid { Background = System.Windows.Media.Brushes.Transparent, Children = { _listLayout, _countLayout, _popup } };
        _refreshTimer.Tick += (_, _) => _ = LoadAsync();
    }

    private bool UsesTodoist => _settings.Source == TodoSource.Todoist;

    protected override void OnAttached()
    {
        _settings = GetSettings<TodoSettings>();
        _settings.PropertyChanged += OnSettingsChanged;
        if (IsPreview)
        {
            _tasks = new List<TodoTask>
            {
                new("1", L.T("Reply to Deniz"), DateTime.Today),
                new("2", L.T("Book the dentist"), DateTime.Today),
                new("3", L.T("Buy milk"), null),
            };
            Render();
            return;
        }
        _ = LoadAsync();
    }

    protected override void OnDetached()
    {
        _settings.PropertyChanged -= OnSettingsChanged;
        _refreshTimer.Stop();
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (IsPreview) return;
        _tasks.Clear();
        _error = null;
        _ = LoadAsync();
    }

    protected override void OnVariantChanged()
    {
        ShowLayout(_listLayout, _countLayout);
        Render();
    }

    // ------------------------------------------------------------------ Data

    private sealed class LocalData
    {
        public List<LocalTask> Items { get; set; } = new();
    }

    private sealed class LocalTask
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        public string Text { get; set; } = "";

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }

    private LocalData LoadLocal() => JsonStore.LoadData<LocalData>(StateKey);

    private async Task LoadAsync()
    {
        if (IsPreview || _loading) return;
        if (!UsesTodoist)
        {
            _refreshTimer.Stop();
            _error = null;
            _tasks = LoadLocal().Items.Select(t => new TodoTask(t.Id, t.Text, null)).ToList();
            Render();
            return;
        }

        string token = _settings.TodoistToken;
        if (token.Length == 0)
        {
            _tasks.Clear();
            _error = L.T("Add your Todoist API token in the widget's settings.");
            Render();
            return;
        }
        _loading = true;
        _refreshTimer.Start();
        try
        {
            _tasks = await TodoistClient.GetTodayAsync(token);
            _error = null;
        }
        catch (TodoistException ex)
        {
            _error = ex.Message;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            _error = L.T("Couldn't reach Todoist. Retrying later.");
            Log.Debug($"Todoist refresh failed: {ex.Message}");
        }
        finally
        {
            _loading = false;
        }
        Render();
    }

    private async Task CompleteAsync(TodoTask task)
    {
        if (IsPreview) return;
        _tasks.Remove(task);
        Render();
        if (!UsesTodoist)
        {
            var data = LoadLocal();
            data.Items.RemoveAll(t => t.Id == task.Id);
            JsonStore.SaveData(StateKey, data);
            return;
        }
        try
        {
            await TodoistClient.CompleteAsync(_settings.TodoistToken, task.Id);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Couldn't complete the Todoist task");
            _error = ex is TodoistException ? ex.Message : L.T("Couldn't reach Todoist. Retrying later.");
            await LoadAsync();
        }
    }

    private async Task AddAsync(string text)
    {
        text = text.Trim();
        if (text.Length == 0 || IsPreview) return;
        if (!UsesTodoist)
        {
            var data = LoadLocal();
            var item = new LocalTask { Text = text };
            data.Items.Add(item);
            JsonStore.SaveData(StateKey, data);
            _tasks.Add(new TodoTask(item.Id, text, null));
            Render();
            return;
        }
        try
        {
            _tasks.Add(await TodoistClient.AddAsync(_settings.TodoistToken, text));
            _error = null;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Couldn't add the Todoist task");
            _error = ex is TodoistException ? ex.Message : L.T("Couldn't reach Todoist. Retrying later.");
        }
        Render();
    }

    // ------------------------------------------------------------------ View

    private void Render()
    {
        var culture = CultureInfo.CurrentCulture;
        int count = _tasks.Count;
        _listCount.Text = count > 0 ? count.ToString(culture) : "";
        _listTitle.Text = UsesTodoist ? L.T("Today") : L.T("To do");
        _listRows.Children.Clear();
        foreach (var task in _tasks.Take(ShownInCard))
            _listRows.Children.Add(TaskRow(task, compact: true));
        if (count == 0)
            _listRows.Children.Add(WidgetUi.Text("CaptionText", _error ?? L.T("All done for today")));
        else if (count > ShownInCard)
            _listRows.Children.Add(WidgetUi.Text("MicroText", L.T("+{0} more", count - ShownInCard)));

        _countNumber.Text = count.ToString(culture);
        _countNext.Text = _tasks.FirstOrDefault()?.Text ?? _error ?? L.T("All done for today");

        if (_popup.IsOpen) BuildPopup();

        ToolTip = _error ?? (count == 0 ? L.T("All done for today") : string.Join("\n", _tasks.Take(8).Select(t => "• " + t.Text)));
        Opacity = _error is not null && count == 0 ? 0.6 : 1;
        SetIdle(count == 0 && _error is null);
        RefreshCompact();
    }

    private FrameworkElement TaskRow(TodoTask task, bool compact)
    {
        var check = new Border
        {
            Width = compact ? 12 : 16,
            Height = compact ? 12 : 16,
            CornerRadius = new CornerRadius(compact ? 6 : 8),
            BorderThickness = new Thickness(1.4),
            Margin = new Thickness(0, 0, compact ? 6 : 9, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Background = System.Windows.Media.Brushes.Transparent,
            Cursor = Cursors.Hand,
            ToolTip = L.T("Mark as done"),
        };
        check.SetResourceReference(Border.BorderBrushProperty, task.Due is { } due && due.Date < DateTime.Today ? "AccentRedBrush" : "AccentGreenBrush");
        check.MouseEnter += (_, _) => check.SetResourceReference(Border.BackgroundProperty, "GreenTrackBrush");
        check.MouseLeave += (_, _) => check.Background = System.Windows.Media.Brushes.Transparent;
        check.MouseLeftButtonUp += (_, e) =>
        {
            if (DockDragHelper.JustDragged) return;
            e.Handled = true;
            _ = CompleteAsync(task);
        };

        var text = compact ? WidgetUi.Text("CaptionText", task.Text) : new TextBlock { Text = task.Text, TextWrapping = TextWrapping.Wrap };
        text.TextTrimming = TextTrimming.CharacterEllipsis;
        text.VerticalAlignment = VerticalAlignment.Center;
        text.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");

        var row = new DockPanel { Margin = new Thickness(0, compact ? 1 : 0, 0, compact ? 1 : 0), LastChildFill = true };
        DockPanel.SetDock(check, System.Windows.Controls.Dock.Left);
        row.Children.Add(check);
        if (!compact && task.Due is { } dueAt && (dueAt.TimeOfDay != TimeSpan.Zero || dueAt.Date < DateTime.Today))
        {
            var when = WidgetUi.Text("MicroText", dueAt.Date < DateTime.Today ? L.T("Overdue") : dueAt.ToString("t", CultureInfo.CurrentCulture));
            when.VerticalAlignment = VerticalAlignment.Center;
            when.Margin = new Thickness(8, 0, 0, 0);
            when.SetResourceReference(TextBlock.ForegroundProperty, dueAt.Date < DateTime.Today ? "AccentRedBrush" : "TextSecondaryBrush");
            DockPanel.SetDock(when, System.Windows.Controls.Dock.Right);
            row.Children.Add(when);
        }
        row.Children.Add(text);
        return row;
    }

    private void BuildPopup()
    {
        _popupRows.Children.Clear();
        foreach (var task in _tasks)
        {
            var row = TaskRow(task, compact: false);
            _popupRows.Children.Add(new Border { Padding = new Thickness(6, 6, 6, 6), Child = row });
        }
        if (_tasks.Count == 0)
            _popupRows.Children.Add(new Border { Padding = new Thickness(6, 8, 6, 8), Child = WidgetUi.Text("CaptionText", L.T("All done for today")) });
        _popupStatus.Text = _error ?? (UsesTodoist ? L.T("Today and overdue tasks from Todoist. New tasks are due today.") : "");
        _popupStatus.Visibility = _popupStatus.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _newTask.IsEnabled = !UsesTodoist || _settings.TodoistToken.Length > 0;
    }

    private async void OnPopupOpened(object? sender, EventArgs e)
    {
        if (IsPreview) return;
        if (UsesTodoist) _ = LoadAsync();
        Host.ActivateForInput();
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Input);
        _newTask.Focus();
        Keyboard.Focus(_newTask);
    }

    private void OnNewTaskKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            string text = _newTask.Text;
            _newTask.Text = "";
            _ = AddAsync(text);
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            ClosePopup(_popup);
        }
    }

    private void Open()
    {
        BuildPopup();
        OpenPopup(_popup);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (DockDragHelper.JustDragged || IsPreview || e.Handled) return;
        Open();
        e.Handled = true;
    }

    public override bool OnCompactClick()
    {
        Open();
        return true;
    }

    protected override void UpdateCompact(CompactTile tile)
    {
        tile.ShowGlyph(Descriptor.Icon, "AccentGreenBrush");
        tile.Text = _tasks.Count > 0 ? _tasks.Count.ToString(CultureInfo.CurrentCulture) : null;
    }

    public override void AddContextMenuItems(ItemCollection items)
    {
        items.Add(DockMenu.Item(L.T("Show list"), "", Open));
        if (UsesTodoist)
        {
            items.Add(DockMenu.Item(L.T("Refresh now"), "", () => _ = LoadAsync(), !_loading));
            items.Add(DockMenu.Item(L.T("Open Todoist"), "", () => NetworkStatusIconView.OpenSettings("https://app.todoist.com/app/today")));
        }
    }
}
