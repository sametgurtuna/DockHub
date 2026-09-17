using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using CustomDock.Core;
using CustomDock.Dock;
using CustomDock.Native;
using CustomDock.Shell;
using ManagedShell.ShellFolders;
using Microsoft.Win32;

namespace CustomDock.Settings;

public sealed class AppEntry
{
    private ImageSource? _icon;
    private bool _iconLoaded;

    public AppEntry(string name, string parsingName)
    {
        Name = name;
        ParsingName = parsingName;

        // "{KnownFolder}\...\app.exe" biçimindeki masaüstü uygulamalarını gerçek .exe yoluna çevir;
        // böylece dock, çalışan pencereleri bu uygulamayla eşleştirebilir.
        var expanded = TrayPreferences.ExpandKnownFolder(parsingName);
        DockPath = expanded.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(expanded)
            ? expanded
            : AppKeys.AppsFolderPrefix + parsingName;
    }

    public string Name { get; }

    public string ParsingName { get; }

    public string DockPath { get; }

    public bool IsPinned { get; set; }

    public Visibility PinnedVisibility => IsPinned ? Visibility.Visible : Visibility.Collapsed;

    public ImageSource? Icon
    {
        get
        {
            if (_iconLoaded) return _icon;
            _iconLoaded = true;
            _icon = ShellIcons.GetIcon(AppKeys.AppsFolderPrefix + ParsingName, 48);
            return _icon;
        }
    }
}

/// <summary>Başlat menüsündeki uygulamalardan (shell:AppsFolder) dock'a sabitleme.</summary>
public partial class AppPickerWindow : Window
{
    private readonly List<AppEntry> _entries = new();
    private ICollectionView? _view;

    public AppPickerWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            WindowEffects.SetDarkMode(hwnd, ThemeManager.IsDark);
        };
        Loaded += (_, _) =>
        {
            SearchBox.Focus();
            Dispatcher.BeginInvoke(DispatcherPriority.Background, LoadApps);
        };
    }

    private void LoadApps()
    {
        var pinned = AppServices.Config.Items
            .Where(i => i.Kind == DockItemKind.App && i.Path is not null)
            .Select(i => i.Path!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var folder = new ShellFolder("shell:AppsFolder", IntPtr.Zero, false, false);
            foreach (var file in folder.Files.ToList())
            {
                var name = file.DisplayName;
                var parsing = file.FileName;
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(parsing)) continue;
                var entry = new AppEntry(name, parsing);
                entry.IsPinned = pinned.Contains(entry.DockPath);
                _entries.Add(entry);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Uygulama listesi alınamadı");
        }

        // Başlat menüsü aynı uygulamayı (kullanıcı + tüm kullanıcılar kısayolu) iki kez listeleyebilir;
        // aynı adı taşıyanlardan gerçek .exe yolu olanı tercih et.
        var unique = _entries
            .GroupBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(g => g.FirstOrDefault(e => !e.DockPath.StartsWith(AppKeys.AppsFolderPrefix, StringComparison.OrdinalIgnoreCase)) ?? g.First())
            .ToList();
        _entries.Clear();
        _entries.AddRange(unique);
        _entries.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        _view = CollectionViewSource.GetDefaultView(_entries);
        _view.Filter = Matches;
        AppList.ItemsSource = _view;
        LoadingText.Visibility = _entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        LoadingText.Text = "Uygulama bulunamadı. 'Dosyadan ekle' ile .exe veya kısayol seçebilirsiniz.";
        if (_entries.Count > 0) AppList.SelectedIndex = 0;
    }

    private bool Matches(object obj)
    {
        var query = SearchBox.Text.Trim();
        return query.Length == 0 || (obj is AppEntry entry && entry.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase));
    }

    private void OnSearchChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        _view?.Refresh();
        if (AppList.Items.Count > 0) AppList.SelectedIndex = 0;
    }

    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down when AppList.Items.Count > 0:
                AppList.SelectedIndex = Math.Min(AppList.Items.Count - 1, AppList.SelectedIndex + 1);
                AppList.ScrollIntoView(AppList.SelectedItem);
                e.Handled = true;
                break;
            case Key.Up when AppList.Items.Count > 0:
                AppList.SelectedIndex = Math.Max(0, AppList.SelectedIndex - 1);
                AppList.ScrollIntoView(AppList.SelectedItem);
                e.Handled = true;
                break;
            case Key.Enter:
                AddSelected();
                e.Handled = true;
                break;
            case Key.Escape:
                Close();
                break;
        }
    }

    private void OnListDoubleClick(object sender, MouseButtonEventArgs e) => AddSelected();

    private void OnAddClick(object sender, RoutedEventArgs e) => AddSelected();

    private void AddSelected()
    {
        if (AppList.SelectedItem is not AppEntry entry) return;
        Add(entry.DockPath, entry.Name);
        entry.IsPinned = true;
        _view?.Refresh();
    }

    private void Add(string path, string? name)
    {
        AppServices.ConfigService.AddItem(DockItem.App(path, name), DockItemsIndex.EndOfApps());
        StatusText.Text = $"{name ?? Path.GetFileNameWithoutExtension(path)} eklendi";
    }

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Dock'a eklenecek uygulamayı seçin",
            Filter = "Uygulamalar ve kısayollar (*.exe;*.lnk;*.url)|*.exe;*.lnk;*.url|Tüm dosyalar (*.*)|*.*",
            DereferenceLinks = false,
            Multiselect = true,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
        };
        if (dialog.ShowDialog(this) != true) return;
        foreach (var file in dialog.FileNames)
            Add(file, null);
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
