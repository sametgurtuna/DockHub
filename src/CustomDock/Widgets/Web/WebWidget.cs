using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CustomDock.Core;
using CustomDock.Dock;
using CustomDock.Widgets.Web;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace CustomDock.Widgets;

/// <summary>Settings of a web widget: the values of the fields its manifest declares.</summary>
public sealed class WebWidgetSettings : ObservableObject
{
    public JsonObject Values { get; set; } = new();

    public void SetValue(string key, JsonNode? value)
    {
        Values[key] = value;
        OnPropertyChanged(nameof(Values));
    }
}

/// <summary>
/// Hosts an HTML/JS widget from %AppData%\DockHub\widgets in WebView2. Files are served from a private virtual
/// host, network access is limited to the hosts in the manifest, and the page talks to DockHub through
/// <c>window.dockhub</c> (settings, storage, notifications, menu, theme).
/// </summary>
public sealed class WebWidget : WidgetBase
{
    private const int MaxStorageBytes = 256 * 1024;
    private static CoreWebView2Environment? s_environment;
    private static Task<CoreWebView2Environment>? s_environmentTask;

    private readonly WebWidgetManifest _manifest;
    private readonly Border _frame = new() { Background = Brushes.Transparent };
    private WebView2? _web;
    private WebWidgetSettings _settings = new();
    private List<(string Id, string Label)> _menu = new();
    private DateTime _lastUrlOpen;

    public WebWidget(WebWidgetManifest manifest)
    {
        _manifest = manifest;
        Content = _frame;
        CardPadding = new Thickness(0);
    }

    /// <summary>Widget type for a manifest, registered in <see cref="WidgetRegistry"/>.</summary>
    public static WidgetDescriptor CreateDescriptor(WebWidgetManifest manifest) => new()
    {
        Id = manifest.WidgetId,
        Name = manifest.Name,
        Category = WidgetCategories.Web,
        Description = string.IsNullOrWhiteSpace(manifest.Author) ? manifest.Description : $"{manifest.Description} ({manifest.Author}, {manifest.Version})",
        IconPath = WebWidgetCatalog.Icon,
        AccentKey = "AccentPurpleBrush",
        Variants = manifest.Variants.Select(v => new WidgetVariant(v.Id, v.Name)).ToList(),
        Factory = () => new WebWidget(manifest),
        SettingsType = manifest.Settings.Count > 0 ? typeof(WebWidgetSettings) : null,
        SettingsViewFactory = manifest.Settings.Count > 0 ? s => WebWidgetSettingsView.Create(manifest, (WebWidgetSettings)s) : null,
    };

    private string SizeName => _manifest.Variants.FirstOrDefault(v => v.Id == Variant)?.Size ?? "standard";

    private (double Width, double Height) SizeOf(string size) => size switch
    {
        "compact" => (44, 44),
        "wide" => (260, 44),
        _ => (170, 44),
    };

    protected override void OnAttached()
    {
        if (_manifest.Settings.Count > 0)
        {
            _settings = GetSettings<WebWidgetSettings>();
            _settings.PropertyChanged += OnSettingsChanged;
        }
        ThemeManager.ThemeChanged += PostTheme;
        _ = StartAsync();
    }

    protected override void OnDetached()
    {
        _settings.PropertyChanged -= OnSettingsChanged;
        ThemeManager.ThemeChanged -= PostTheme;
        if (_web is not null)
        {
            _frame.Child = null;
            _web.Dispose();
            _web = null;
        }
    }

    protected override void OnVariantChanged()
    {
        var (w, h) = SizeOf(SizeName);
        _frame.Width = w;
        _frame.Height = h;
        Post(new JsonObject { ["event"] = "size", ["data"] = SizeName });
    }

    private static Task<CoreWebView2Environment> EnvironmentAsync()
    {
        if (s_environment is not null) return Task.FromResult(s_environment);
        // One shared browser process for all web widgets.
        return s_environmentTask ??= CoreWebView2Environment.CreateAsync(null,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DockHub", "WebView2"));
    }

    private async Task StartAsync()
    {
        try
        {
            var environment = s_environment ??= await EnvironmentAsync();
            _web = new WebView2 { DefaultBackgroundColor = System.Drawing.Color.Transparent };
            _frame.Child = _web;
            await _web.EnsureCoreWebView2Async(environment);
            if (_web?.CoreWebView2 is not { } core) return;

            var settings = core.Settings;
            bool dev = AppServices.Config.DebugLogging;
            settings.AreDevToolsEnabled = dev;
            settings.AreDefaultContextMenusEnabled = dev;
            settings.IsStatusBarEnabled = false;
            settings.IsZoomControlEnabled = false;
            settings.IsPinchZoomEnabled = false;
            settings.AreBrowserAcceleratorKeysEnabled = dev;
            settings.IsGeneralAutofillEnabled = false;
            settings.IsPasswordAutosaveEnabled = false;

            core.SetVirtualHostNameToFolderMapping(_manifest.HostName, _manifest.Folder, CoreWebView2HostResourceAccessKind.Deny);
            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += OnResourceRequested;
            core.NewWindowRequested += (_, e) => e.Handled = true;
            core.DownloadStarting += (_, e) => e.Cancel = true;
            core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
            core.WebMessageReceived += OnMessage;
            core.NavigationCompleted += (_, _) => { PostTheme(); OnVariantChanged(); };
            await core.AddScriptToExecuteOnDocumentCreatedAsync(BridgeScript);
            core.Navigate($"https://{_manifest.HostName}/{_manifest.Entry.Replace('\\', '/')}");
        }
        catch (WebView2RuntimeNotFoundException)
        {
            ShowError(L.T("Web widgets need the Microsoft Edge WebView2 Runtime."));
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Web widget {_manifest.Id} failed to start");
            ShowError(L.T("This widget couldn't start."));
        }
    }

    private void ShowError(string message)
    {
        _frame.Child = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 10.5, Margin = new Thickness(6, 2, 6, 2), Foreground = (Brush)FindResource("TextSecondaryBrush") };
        ToolTip = message;
    }

    /// <summary>Only the widget's own files and the hosts it declared may load.</summary>
    private void OnResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        if (!Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out var uri)) return;
        if (uri.Scheme is "data" or "blob") return;
        if (uri.Scheme is "https" or "wss" && WebWidgetCatalog.IsHostAllowed(_manifest, uri.Host)) return;
        e.Response = _web!.CoreWebView2.Environment.CreateWebResourceResponse(null, 403, "Blocked by DockHub", "");
        Log.Debug($"Web widget {_manifest.Id}: blocked {uri.Host}");
    }

    private void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        JsonObject? message;
        try { message = JsonNode.Parse(e.WebMessageAsJson) as JsonObject; }
        catch { return; }
        if (message?["id"] is not { } id || message["method"]?.GetValue<string>() is not { } method) return;
        var args = message["args"] as JsonObject;

        try
        {
            JsonNode? result = method switch
            {
                "settings.get" => MergedSettings(),
                "storage.get" => Storage()[args?["key"]?.GetValue<string>() ?? ""]?.DeepClone(),
                "storage.set" => SetStorage(args?["key"]?.GetValue<string>() ?? "", args?["value"]?.DeepClone()),
                "notify" => Notify(args),
                "openUrl" => OpenUrl(args?["url"]?.GetValue<string>()),
                "contextMenu.set" => SetMenu(args?["items"] as JsonArray),
                _ => throw new InvalidOperationException($"Unknown method {method}"),
            };
            Post(new JsonObject { ["id"] = id.DeepClone(), ["result"] = result });
        }
        catch (Exception ex)
        {
            Post(new JsonObject { ["id"] = id.DeepClone(), ["error"] = ex.Message });
        }
    }

    /// <summary>Manifest defaults overlaid with the values the user changed.</summary>
    private JsonObject MergedSettings()
    {
        var merged = new JsonObject();
        foreach (var field in _manifest.Settings)
            if (field.Default is { } value) merged[field.Key] = JsonNode.Parse(value.GetRawText());
        foreach (var (key, value) in _settings.Values) merged[key] = value?.DeepClone();
        return merged;
    }

    private JsonObject Storage()
    {
        try
        {
            string path = JsonStore.DataPath(StateKey);
            return File.Exists(path) && JsonNode.Parse(File.ReadAllText(path)) is JsonObject stored ? stored : new JsonObject();
        }
        catch
        {
            return new JsonObject();
        }
    }

    private JsonNode? SetStorage(string key, JsonNode? value)
    {
        if (IsPreview || key.Length == 0) return null;
        var storage = Storage();
        storage[key] = value;
        if (storage.ToJsonString().Length > MaxStorageBytes) throw new InvalidOperationException("Storage is full (256 KB).");
        JsonStore.SaveData(StateKey, storage);
        return null;
    }

    private JsonNode? Notify(JsonObject? args)
    {
        if (!_manifest.Permissions.Notifications) throw new InvalidOperationException("The manifest doesn't allow notifications.");
        if (IsPreview) return null;
        AppServices.Notifications.Show(args?["title"]?.GetValue<string>() ?? _manifest.Name, args?["body"]?.GetValue<string>() ?? "");
        return null;
    }

    private JsonNode? OpenUrl(string? url)
    {
        if (url is null || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
            throw new InvalidOperationException("Only web links can be opened.");
        // A page can't open a stream of browser tabs.
        if (DateTime.UtcNow - _lastUrlOpen < TimeSpan.FromSeconds(2)) return null;
        _lastUrlOpen = DateTime.UtcNow;
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        return null;
    }

    private JsonNode? SetMenu(JsonArray? items)
    {
        _menu = (items ?? new JsonArray()).OfType<JsonObject>()
            .Select(i => (i["id"]?.GetValue<string>() ?? "", i["label"]?.GetValue<string>() ?? ""))
            .Where(i => i.Item1.Length > 0 && i.Item2.Length > 0)
            .Take(8)
            .ToList();
        return null;
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
        => Post(new JsonObject { ["event"] = "settings", ["data"] = MergedSettings() });

    private void PostTheme()
    {
        string Hex(string key) => TryFindResource(key) is SolidColorBrush b ? $"rgba({b.Color.R},{b.Color.G},{b.Color.B},{b.Color.A / 255.0:0.###})" : "";
        Post(new JsonObject
        {
            ["event"] = "theme",
            ["data"] = new JsonObject
            {
                ["mode"] = ThemeManager.IsDark ? "dark" : "light",
                ["vars"] = new JsonObject
                {
                    ["--dh-text"] = Hex("TextPrimaryBrush"),
                    ["--dh-text-secondary"] = Hex("TextSecondaryBrush"),
                    ["--dh-accent"] = Hex("AccentBrush"),
                    ["--dh-card"] = Hex("CardBrush"),
                    ["--dh-font"] = "\"Segoe UI Variable Text\", \"Segoe UI\", sans-serif",
                },
            },
        });
    }

    private void Post(JsonObject message)
    {
        try { _web?.CoreWebView2?.PostWebMessageAsJson(message.ToJsonString()); }
        catch { /* page not ready */ }
    }

    protected override void UpdateCompact(CompactTile tile) => tile.ShowGlyph(Descriptor.Icon, "AccentPurpleBrush");

    public override void AddContextMenuItems(ItemCollection items)
    {
        foreach (var (id, label) in _menu)
        {
            var itemId = id;
            items.Add(DockMenu.Item(label, null, () => Post(new JsonObject { ["event"] = "menu", ["data"] = itemId })));
        }
        if (AppServices.Config.DebugLogging)
            items.Add(DockMenu.Item(L.T("Developer tools"), "", () => _web?.CoreWebView2?.OpenDevToolsWindow()));
        items.Add(DockMenu.Item(L.T("Reload"), "", () => _web?.CoreWebView2?.Reload()));
    }

    /// <summary>window.dockhub, installed before the widget's own scripts run.</summary>
    private const string BridgeScript = """
        (() => {
          const pending = new Map(); let nextId = 1; const listeners = {};
          const call = (method, args) => new Promise((resolve, reject) => {
            const id = nextId++; pending.set(id, { resolve, reject });
            window.chrome.webview.postMessage({ id, method, args });
          });
          const on = (event, handler) => { (listeners[event] = listeners[event] || []).push(handler); };
          window.chrome.webview.addEventListener('message', e => {
            const m = e.data;
            if (m && m.id !== undefined && pending.has(m.id)) {
              const p = pending.get(m.id); pending.delete(m.id);
              m.error ? p.reject(new Error(m.error)) : p.resolve(m.result);
            } else if (m && m.event) {
              if (m.event === 'theme') {
                const root = document.documentElement;
                for (const [k, v] of Object.entries(m.data.vars)) root.style.setProperty(k, v);
                root.dataset.theme = m.data.mode;
              }
              if (m.event === 'size') window.dockhub.size = m.data;
              (listeners[m.event] || []).forEach(f => { try { f(m.data); } catch (err) { console.error(err); } });
            }
          });
          window.dockhub = {
            apiVersion: 1,
            size: 'standard',
            settings: { get: () => call('settings.get'), onChange: f => on('settings', f) },
            storage: { get: key => call('storage.get', { key }), set: (key, value) => call('storage.set', { key, value }) },
            notify: options => call('notify', options),
            openUrl: url => call('openUrl', { url }),
            contextMenu: { set: items => call('contextMenu.set', { items }), onSelect: f => on('menu', f) },
            onTheme: f => on('theme', f),
            onSize: f => on('size', f),
          };
        })();
        """;
}
