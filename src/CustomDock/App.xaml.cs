using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using CustomDock.Core;
using CustomDock.Dock;
using CustomDock.Settings;
using CustomDock.Shell;
using Microsoft.Win32;

namespace CustomDock;

public partial class App : Application
{
    private SingleInstance? _singleInstance;
    private bool _running;
    private ShellHost? _shell;
    private DockWindow? _dock;
    private TrayIconManager? _tray;
    private SettingsWindow? _settings;
    private AppPickerWindow? _appPicker;
    private bool _cleanedUp;
    private static bool _taskbarTouched;

    public static App Instance => (App)Current;

    protected override void OnStartup(StartupEventArgs e)
    {
        // XAML bağlamalarındaki tarih/sayı biçimleri sistem dilini kullansın (varsayılan en-US).
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag)));

        // WPF animasyonları varsayılan 60 FPS'e sınırlıdır; yüksek yenileme hızlı ekranlarda akıcı olsun.
        System.Windows.Media.Animation.Timeline.DesiredFrameRateProperty.OverrideMetadata(
            typeof(System.Windows.Media.Animation.Timeline),
            new FrameworkPropertyMetadata { DefaultValue = Math.Clamp(Native.NativeMethods.GetRefreshRate(), 60, 240) });

        base.OnStartup(e);
        AppPaths.EnsureCreated();

        // Acil durum: "DockHub.exe --restore-taskbar"
        if (e.Args.Any(a => a.Equals("--restore-taskbar", StringComparison.OrdinalIgnoreCase)))
        {
            TaskbarController.ForceShow();
            Shutdown();
            return;
        }

        // "DockHub.exe --pin <dosya>": Explorer sağ tık menüsünden sabitleme
        if (PinArgumentPath(e.Args) is { } pinPath)
            ExplorerPinMenu.Enqueue(pinPath);

        _singleInstance = new SingleInstance();
        bool exitRequested = e.Args.Any(a => a.Equals("--exit", StringComparison.OrdinalIgnoreCase));
        if (exitRequested)
        {
            // "DockHub.exe --exit": çalışan örneği düzgünce kapatır.
            if (!_singleInstance.TryAcquire())
                SingleInstance.SignalExit();
            Shutdown();
            return;
        }

        if (!_singleInstance.TryAcquire())
        {
            // Toast tıklamasıyla açılan ikinci örnek sessizce kapanır; aksi halde mevcut örnek ayarları gösterir.
            if (PinArgumentPath(e.Args) is not null)
                SingleInstance.SignalPin();
            else if (!e.Args.Any(a => a.Contains("ToastActivated", StringComparison.OrdinalIgnoreCase)))
                SingleInstance.SignalExisting();
            Shutdown();
            return;
        }

        _running = true;
        RegisterCrashHandlers();

        // Önceki oturum görev çubuğu gizliyken çöktüyse geri getir (ManagedShell başlamadan önce!).
        TaskbarController.RecoverFromPreviousSession();

        System.Windows.Forms.Application.EnableVisualStyles();

        AppServices.ConfigService.Load();
        var config = AppServices.Config;
        ThemeManager.Apply(config.Theme);

        if (config.StartWithWindows != StartupManager.IsEnabled())
            StartupManager.Set(config.StartWithWindows); // exe taşındıysa yolu da günceller
        ExplorerPinMenu.Set(config.ExplorerPinMenu);

        StartShell();

        _tray = new TrayIconManager();
        AppServices.Notifications.Fallback = (title, body) => _tray?.ShowBalloon(title, body);
        AppServices.Notifications.Initialize();
        AppServices.Reminders.Start();

        CreateDock();
        ApplyTaskbarMode();

        config.PropertyChanged += OnConfigChanged;
        WidgetItemView.SettingsRequested += item => ShowSettings("items", item.Id);

        _singleInstance.Listen(Dispatcher, () => ShowSettings(), ExitApplication, ProcessPinRequests);
        ProcessPinRequests();
        SystemEvents.SessionEnding += (_, _) => Cleanup();

        if (config.IsFirstRun)
            ShowSettings("gallery");

        Log.Info($"DockHub başlatıldı (v{typeof(App).Assembly.GetName().Version}, mod: {config.TaskbarMode}).");
    }

    private void StartShell()
    {
        var config = AppServices.Config;
        _shell = new ShellHost(config.TaskbarMode == TaskbarMode.Replace, config.PinnedTrayIcons);
        AppServices.Shell = _shell;
        _taskbarTouched = true;

        if (_shell.Tray is { } tray)
        {
            if (config.PinnedTrayIcons is null)
                TrayPreferences.ImportWindowsPromotedIcons(tray);
            tray.TrayIcons.CollectionChanged += (_, _) => TrayPreferences.ApplyNewIcons(tray);
        }
    }

    private void RegisterCrashHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error(args.Exception, "İşlenmeyen UI hatası");
            args.Handled = true; // Tek bir widget hatası tüm dock'u kapatmasın.
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                Log.Error(ex, "Kritik hata");
            SafeRestoreTaskbar();
        };

        AppDomain.CurrentDomain.ProcessExit += (_, _) => SafeRestoreTaskbar();

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error(args.Exception, "Gözlenmeyen görev hatası");
            args.SetObserved();
        };
    }

    /// <summary>Kullanıcı görev çubuksuz kalmasın: her çıkış yolunda çağrılır.</summary>
    private static void SafeRestoreTaskbar()
    {
        if (!_taskbarTouched) return;
        try
        {
            AppServices.Shell?.Taskbar.Restore();
        }
        catch
        {
            // Son çare: bir sonraki açılışta session.json üzerinden geri yüklenir.
        }
    }

    private void CreateDock()
    {
        _dock = new DockWindow(AppServices.Config, _shell!);
        _dock.Start();
    }

    private void ApplyTaskbarMode()
    {
        if (_shell is null) return;
        if (AppServices.Config.TaskbarMode == TaskbarMode.Replace)
            _shell.Taskbar.Hide();
        else
            _shell.Taskbar.Restore();
    }

    private void OnConfigChanged(object? sender, PropertyChangedEventArgs e)
    {
        var config = AppServices.Config;
        switch (e.PropertyName)
        {
            case nameof(AppConfig.Theme):
                ThemeManager.Apply(config.Theme);
                break;
            case nameof(AppConfig.TaskbarMode):
                // Sistem tepsisi yalnızca görev çubuğunun yerini alırken devralınır; kabuk servislerini yeniden kur.
                AppServices.ConfigService.SaveNow();
                RestartApplication();
                break;
            case nameof(AppConfig.StartWithWindows):
                StartupManager.Set(config.StartWithWindows);
                break;
            case nameof(AppConfig.ExplorerPinMenu):
                ExplorerPinMenu.Set(config.ExplorerPinMenu);
                break;
            case nameof(AppConfig.PinnedTrayIcons):
                break;
            default:
                _dock?.ApplySettings();
                break;
        }
    }

    private static string? PinArgumentPath(string[] args)
    {
        int index = Array.FindIndex(args, a => a.Equals(ExplorerPinMenu.PinArgument, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length && !string.IsNullOrWhiteSpace(args[index + 1]) ? args[index + 1] : null;
    }

    /// <summary>Explorer'dan gelen "sabitle" isteklerini sabitlenmiş uygulamaların sonuna ekler.</summary>
    private void ProcessPinRequests()
    {
        var requests = ExplorerPinMenu.Dequeue();
        if (requests.Count == 0) return;

        var service = AppServices.ConfigService;
        var pinnedKeys = AppServices.Config.Items
            .Where(i => i.Kind == DockItemKind.App)
            .Select(AppKeys.ForItem)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        int added = 0;
        foreach (var path in requests)
        {
            if (!File.Exists(path)) continue;
            var item = DockItem.App(path);
            if (!pinnedKeys.Add(AppKeys.ForItem(item))) continue; // zaten sabitli
            service.AddItem(item, DockItemsIndex.EndOfApps());
            added++;
        }

        _dock?.Reveal();
        if (added == 0)
            _tray?.ShowBalloon(AppInfo.Name, "Bu uygulama zaten sabitlenmiş.");
        Log.Info($"Explorer'dan {added} uygulama sabitlendi.");
    }

    public void ShowSettings(string? page = null, string? itemId = null)
    {
        if (_settings is null)
        {
            _settings = new SettingsWindow();
            _settings.Closed += (_, _) => _settings = null;
            _settings.Show();
        }

        if (page is not null)
            _settings.NavigateTo(page, itemId);

        if (_settings.WindowState == WindowState.Minimized)
            _settings.WindowState = WindowState.Normal;
        _settings.Activate();
    }

    public void ShowAppPicker()
    {
        if (_appPicker is null)
        {
            _appPicker = new AppPickerWindow();
            _appPicker.Closed += (_, _) => _appPicker = null;
            _appPicker.Show();
        }
        _appPicker.Activate();
    }

    public void RevealDock() => _dock?.Reveal();

    public void RestartApplication()
    {
        Log.Info("Uygulama yeniden başlatılıyor.");
        Cleanup();
        _singleInstance?.Dispose();
        _singleInstance = null;
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Yeniden başlatılamadı");
        }
        Shutdown();
    }

    public void ExitApplication()
    {
        Cleanup();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Cleanup();
        base.OnExit(e);
    }

    private void Cleanup()
    {
        if (_cleanedUp) return;
        _cleanedUp = true;
        if (!_running)
        {
            // İkinci örnek / --exit / --restore-taskbar: kapatılacak bir şey yok.
            _singleInstance?.Dispose();
            return;
        }

        SafeRestoreTaskbar();
        try
        {
            _settings?.Close();
            _appPicker?.Close();
            _dock?.CloseDock();
            AppServices.ConfigService.SaveNow();
            _shell?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Kapanış sırasında hata");
        }

        _tray?.Dispose();
        _singleInstance?.Dispose();
        Log.Info("DockHub kapatıldı.");
    }
}
