import AppKit
import SwiftUI
import WebKit
import DockHubCore
import DockHubPlatform

/// HTML/JS web widget'ini barindiran gorunum.
/// Windows karsiligi: Widgets/Web/WebWidget.cs
public struct WebWidgetView: View {
    let item: DockItem
    let style: DockStyle
    @ObservedObject var model: DockModel

    @State private var hoverState = false
    @State private var customMenuItems: [(id: String, label: String)] = []

    private var manifest: WebWidgetManifest? {
        guard let widgetId = item.widget, widgetId.hasPrefix("web.") else { return nil }
        let manifestId = String(widgetId.dropFirst(4))
        return WebWidgetCatalog.shared.findManifest(manifestId)
    }

    private var currentVariant: WebWidgetVariant? {
        guard let manifest else { return nil }
        let vid = item.variant ?? manifest.variants.first?.id ?? "default"
        return manifest.variants.first { $0.id == vid } ?? manifest.variants.first
    }

    private var widgetWidth: CGFloat {
        let size = currentVariant?.size ?? "standard"
        switch size {
        case "compact": return style.itemHeight
        case "wide":    return 260
        default:        return 170
        }
    }

    public var body: some View {
        Group {
            if let manifest {
                WebWidgetRepresentable(
                    manifest: manifest,
                    item: item,
                    style: style,
                    variant: currentVariant,
                    onHover: { hoverState = $0 },
                    onMenuItemsChanged: { customMenuItems = $0 }
                )
                .frame(width: widgetWidth, height: style.itemHeight)
                .clipShape(RoundedRectangle(cornerRadius: style.itemRadius, style: .continuous))
                .contextMenu {
                    contextMenuContent
                }
            } else {
                HStack(spacing: 4) {
                    Image(systemName: "exclamationmark.triangle")
                    Text(L.t("This widget couldn't start."))
                        .font(.system(size: style.height * 0.18))
                }
                .foregroundStyle(.secondary)
                .frame(width: 170, height: style.itemHeight)
            }
        }
    }

    @ViewBuilder
    private var contextMenuContent: some View {
        ForEach(customMenuItems, id: \.id) { mItem in
            Button(mItem.label) {
                NotificationCenter.default.post(
                    name: .dockhubWebWidgetMenuAction,
                    object: nil,
                    userInfo: ["itemId": item.id, "actionId": mItem.id]
                )
            }
        }
        if !customMenuItems.isEmpty {
            Divider()
        }
        Button(L.t("Reload")) {
            NotificationCenter.default.post(
                name: .dockhubWebWidgetReload,
                object: nil,
                userInfo: ["itemId": item.id]
            )
        }
    }
}

extension Notification.Name {
    public static let dockhubWebWidgetMenuAction = Notification.Name("dockhubWebWidgetMenuAction")
    public static let dockhubWebWidgetReload = Notification.Name("dockhubWebWidgetReload")
}

// MARK: - NSViewRepresentable

struct WebWidgetRepresentable: NSViewRepresentable {
    let manifest: WebWidgetManifest
    let item: DockItem
    let style: DockStyle
    let variant: WebWidgetVariant?
    let onHover: (Bool) -> Void
    let onMenuItemsChanged: ([(id: String, label: String)]) -> Void

    func makeCoordinator() -> Coordinator {
        Coordinator(self)
    }

    func makeNSView(context: Context) -> WKWebView {
        let config = WKWebViewConfiguration()
        config.preferences.isElementFullscreenEnabled = false
        config.defaultWebpagePreferences.allowsContentJavaScript = true

        let userController = WKUserContentController()
        let script = WKUserScript(source: Self.bridgeScript, injectionTime: .atDocumentStart, forMainFrameOnly: true)
        userController.addUserScript(script)
        userController.add(context.coordinator, name: "dockhubBridge")
        config.userContentController = userController

        let webView = WKWebView(frame: .zero, configuration: config)
        webView.setValue(false, forKey: "drawsBackground")
        webView.navigationDelegate = context.coordinator
        if #available(macOS 13.3, *) {
            webView.isInspectable = true
        }

        context.coordinator.webView = webView
        context.coordinator.startObservingNotifications()

        loadEntry(in: webView)
        return webView
    }

    func updateNSView(_ webView: WKWebView, context: Context) {
        context.coordinator.parent = self
        context.coordinator.postTheme()
        context.coordinator.postSize()
        context.coordinator.postSettings()
    }

    static func dismantleNSView(_ nsView: WKWebView, coordinator: Coordinator) {
        coordinator.stopObservingNotifications()
        nsView.stopLoading()
        nsView.configuration.userContentController.removeScriptMessageHandler(forName: "dockhubBridge")
    }

    private func loadEntry(in webView: WKWebView) {
        guard let folder = manifest.folder else { return }
        let entryURL = folder.appendingPathComponent(manifest.entry)
        webView.loadFileURL(entryURL, allowingReadAccessTo: folder)
    }

    // MARK: - Coordinator

    @MainActor
    final class Coordinator: NSObject, WKNavigationDelegate, WKScriptMessageHandler {
        var parent: WebWidgetRepresentable
        weak var webView: WKWebView?
        private var lastUrlOpen: Date = .distantPast
        private var notificationObservers: [NSObjectProtocol] = []

        init(_ parent: WebWidgetRepresentable) {
            self.parent = parent
            super.init()
        }

        func startObservingNotifications() {
            let menuObs = NotificationCenter.default.addObserver(
                forName: .dockhubWebWidgetMenuAction,
                object: nil,
                queue: .main
            ) { [weak self] note in
                MainActor.assumeIsolated {
                    guard let self,
                          let targetItemId = note.userInfo?["itemId"] as? String,
                          targetItemId == self.parent.item.id,
                          let actionId = note.userInfo?["actionId"] as? String else { return }
                    self.post(event: "menu", data: actionId)
                }
            }
            notificationObservers.append(menuObs)

            let reloadObs = NotificationCenter.default.addObserver(
                forName: .dockhubWebWidgetReload,
                object: nil,
                queue: .main
            ) { [weak self] note in
                MainActor.assumeIsolated {
                    guard let self,
                          let targetItemId = note.userInfo?["itemId"] as? String,
                          targetItemId == self.parent.item.id else { return }
                    self.webView?.reload()
                }
            }
            notificationObservers.append(reloadObs)
        }

        func stopObservingNotifications() {
            for obs in notificationObservers {
                NotificationCenter.default.removeObserver(obs)
            }
            notificationObservers.removeAll()
        }

        // WKNavigationDelegate
        func webView(_ webView: WKWebView, didFinish navigation: WKNavigation!) {
            postTheme()
            postSize()
            postSettings()
        }

        func webView(
            _ webView: WKWebView,
            decidePolicyFor navigationAction: WKNavigationAction,
            decisionHandler: @escaping @MainActor @Sendable (WKNavigationActionPolicy) -> Void
        ) {
            guard let url = navigationAction.request.url else {
                decisionHandler(.cancel)
                return
            }

            // Giris dosyasi veya ayni klasordeki dosya yuklemelerine izin ver
            if url.isFileURL {
                if let folder = parent.manifest.folder, url.path.hasPrefix(folder.path) {
                    decisionHandler(.allow)
                    return
                }
            }

            // Sayfa icindeki harici link tiklamalari varsayilan tarayicida ac
            if navigationAction.navigationType == .linkActivated {
                if url.scheme == "http" || url.scheme == "https" {
                    NSWorkspace.shared.open(url)
                }
                decisionHandler(.cancel)
                return
            }

            decisionHandler(.allow)
        }

        // WKScriptMessageHandler
        func userContentController(_ userContentController: WKUserContentController, didReceive message: WKScriptMessage) {
            guard let dict = message.body as? [String: Any] else { return }

            if let event = dict["event"] as? String, event == "hover" {
                let val = (dict["value"] as? Bool) == true
                parent.onHover(val)
                return
            }

            guard let id = dict["id"], let method = dict["method"] as? String else { return }
            let args = dict["args"] as? [String: Any]

            handleMethod(id: id, method: method, args: args)
        }

        private func handleMethod(id: Any, method: String, args: [String: Any]?) {
            switch method {
            case "settings.get":
                let merged = mergedSettings()
                postResult(id: id, result: merged)

            case "storage.get":
                let key = args?["key"] as? String ?? ""
                let storage = loadStorage()
                postResult(id: id, result: storage[key] ?? NSNull())

            case "storage.set":
                let key = args?["key"] as? String ?? ""
                let value = args?["value"]
                do {
                    try saveStorage(key: key, value: value)
                    postResult(id: id, result: NSNull())
                } catch {
                    postResult(id: id, error: error.localizedDescription)
                }

            case "notify":
                guard parent.manifest.permissions.notifications else {
                    postResult(id: id, error: "The manifest doesn't allow notifications.")
                    return
                }
                let title = args?["title"] as? String ?? parent.manifest.name
                let body = args?["body"] as? String ?? ""
                Notifier.gonder(baslik: title, metin: body)
                postResult(id: id, result: NSNull())

            case "openUrl":
                guard let urlString = args?["url"] as? String,
                      let url = URL(string: urlString),
                      let scheme = url.scheme?.lowercased(), (scheme == "http" || scheme == "https") else {
                    postResult(id: id, error: "Only web links can be opened.")
                    return
                }
                let now = Date()
                if now.timeIntervalSince(lastUrlOpen) >= 2.0 {
                    lastUrlOpen = now
                    NSWorkspace.shared.open(url)
                }
                postResult(id: id, result: NSNull())

            case "contextMenu.set":
                var items: [(id: String, label: String)] = []
                if let rawList = args?["items"] as? [[String: Any]] {
                    for itemDict in rawList.prefix(8) {
                        if let itemId = itemDict["id"] as? String,
                           let itemLabel = itemDict["label"] as? String,
                           !itemId.isEmpty, !itemLabel.isEmpty {
                            items.append((itemId, itemLabel))
                        }
                    }
                }
                parent.onMenuItemsChanged(items)
                postResult(id: id, result: NSNull())

            case "http.request":
                handleHttpRequest(id: id, args: args)

            default:
                postResult(id: id, error: "Unknown method \(method)")
            }
        }

        private func handleHttpRequest(id: Any, args: [String: Any]?) {
            // [String: Any] -> [String: JSONValue]
            var convertedArgs: [String: JSONValue] = [:]
            if let args {
                for (k, v) in args {
                    convertedArgs[k] = convertToJSONValue(v)
                }
            }

            let manifest = parent.manifest
            let savedSettings = itemSettingsDict()
            let settingsHosts = WebWidgetCatalog.settingsHosts(manifest: manifest, values: savedSettings)

            Task { @MainActor [weak self] in
                do {
                    let request = try WebWidgetHttp.parse(args: convertedArgs) { host in
                        WebWidgetCatalog.isHostAllowed(manifest: manifest, host: host, settingsHosts: settingsHosts)
                    }
                    let res = try await WebWidgetHttp.send(request)
                    let resDict: [String: Any] = [
                        "status": res.status,
                        "ok": res.ok,
                        "contentType": res.contentType as Any,
                        "body": res.body
                    ]
                    self?.postResult(id: id, result: resDict)
                } catch {
                    self?.postResult(id: id, error: error.localizedDescription)
                }
            }
        }

        // ---------------- Ayarlar ve Depolama

        private func itemSettingsDict() -> [String: JSONValue] {
            if case .object(let dict)? = parent.item.settings {
                return dict
            }
            return [:]
        }

        private func mergedSettings() -> [String: Any] {
            var merged: [String: Any] = [:]
            for s in parent.manifest.settings {
                if let def = s.default {
                    merged[s.key] = convertFromJSONValue(def)
                }
            }
            let userSettings = itemSettingsDict()
            for (k, v) in userSettings {
                merged[k] = convertFromJSONValue(v)
            }
            return merged
        }

        private var storageURL: URL {
            AppPaths.widgetData(parent.manifest.id, parent.item.id)
        }

        private func loadStorage() -> [String: Any] {
            guard let data = try? Data(contentsOf: storageURL),
                  let obj = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else {
                return [:]
            }
            return obj
        }

        private func saveStorage(key: String, value: Any?) throws {
            guard !key.isEmpty else { return }
            var current = loadStorage()
            if let value {
                current[key] = value
            } else {
                current.removeValue(forKey: key)
            }

            let data = try JSONSerialization.data(withJSONObject: current, options: [])
            if data.count > 256 * 1024 {
                throw WebWidgetError("Storage is full (256 KB).")
            }

            try FileManager.default.createDirectory(at: AppPaths.dataDir, withIntermediateDirectories: true)
            try data.write(to: storageURL, options: .atomic)
        }

        // ---------------- JavaScript'e Mesaj Gonderimi

        func postTheme() {
            let isDark = NSApp.effectiveAppearance.bestMatch(from: [.darkAqua, .aqua]) == .darkAqua
            let data: [String: Any] = [
                "mode": isDark ? "dark" : "light",
                "vars": [
                    "--dh-text": isDark ? "rgba(255,255,255,0.92)" : "rgba(0,0,0,0.9)",
                    "--dh-text-secondary": isDark ? "rgba(255,255,255,0.55)" : "rgba(0,0,0,0.55)",
                    "--dh-accent": "rgba(0,122,255,1)",
                    "--dh-card": isDark ? "rgba(255,255,255,0.08)" : "rgba(0,0,0,0.06)",
                    "--dh-font": "-apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif"
                ]
            ]
            post(event: "theme", data: data)
        }

        func postSize() {
            let size = parent.variant?.size ?? "standard"
            post(event: "size", data: size)
        }

        func postSettings() {
            let settings = mergedSettings()
            post(event: "settings", data: settings)
        }

        func post(event: String, data: Any) {
            guard let payload = try? JSONSerialization.data(withJSONObject: ["event": event, "data": data]),
                  let jsonString = String(data: payload, encoding: .utf8) else { return }
            webView?.evaluateJavaScript("window.__dockhubReceive(\(jsonString))")
        }

        func postResult(id: Any, result: Any? = nil, error: String? = nil) {
            var dict: [String: Any] = ["id": id]
            if let error {
                dict["error"] = error
            } else if let result {
                dict["result"] = result
            } else {
                dict["result"] = NSNull()
            }
            guard let payload = try? JSONSerialization.data(withJSONObject: dict),
                  let jsonString = String(data: payload, encoding: .utf8) else { return }
            webView?.evaluateJavaScript("window.__dockhubReceive(\(jsonString))")
        }

        private func convertToJSONValue(_ val: Any) -> JSONValue {
            if let str = val as? String { return .string(str) }
            if let b = val as? Bool { return .bool(b) }
            if let n = val as? NSNumber { return .number(n.doubleValue) }
            if let arr = val as? [Any] { return .array(arr.map(convertToJSONValue)) }
            if let dict = val as? [String: Any] {
                var res: [String: JSONValue] = [:]
                for (k, v) in dict { res[k] = convertToJSONValue(v) }
                return .object(res)
            }
            return .null
        }

        private func convertFromJSONValue(_ val: JSONValue) -> Any {
            switch val {
            case .null: return NSNull()
            case .bool(let b): return b
            case .number(let n): return n
            case .string(let s): return s
            case .array(let arr): return arr.map(convertFromJSONValue)
            case .object(let obj):
                var res: [String: Any] = [:]
                for (k, v) in obj { res[k] = convertFromJSONValue(v) }
                return res
            }
        }
    }

    /// Sayfaya enjekte edilen window.dockhub koprusu.
    /// Windows karsiligi: BridgeScript
    private static let bridgeScript = """
    (() => {
      const pending = new Map(); let nextId = 1; const listeners = {};
      const call = (method, args) => new Promise((resolve, reject) => {
        const id = nextId++; pending.set(id, { resolve, reject });
        window.webkit.messageHandlers.dockhubBridge.postMessage({ id, method, args });
      });
      const on = (event, handler) => { (listeners[event] = listeners[event] || []).push(handler); };
      window.__dockhubReceive = (m) => {
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
      };
      let hovered = false;
      const hover = value => {
        if (hovered !== value) {
          hovered = value;
          window.webkit.messageHandlers.dockhubBridge.postMessage({ event: 'hover', value });
        }
      };
      window.addEventListener('mouseover', () => hover(true), { passive: true });
      window.addEventListener('mouseout', e => { if (!e.relatedTarget) hover(false); }, { passive: true });
      window.dockhub = {
        apiVersion: 2,
        size: 'standard',
        settings: { get: () => call('settings.get'), onChange: f => on('settings', f) },
        storage: { get: key => call('storage.get', { key }), set: (key, value) => call('storage.set', { key, value }) },
        notify: options => call('notify', options),
        openUrl: url => call('openUrl', { url }),
        http: { request: options => call('http.request', options) },
        contextMenu: { set: items => call('contextMenu.set', { items }), onSelect: f => on('menu', f) },
        onTheme: f => on('theme', f),
        onSize: f => on('size', f),
      };
    })();
    """
}
