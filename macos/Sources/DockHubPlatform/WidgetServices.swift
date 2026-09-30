import AppKit
import Combine
import Security
import DockHubCore

/// Widget'larin ag istekleri. Windows'taki HttpClient'lar gibi zaman sinirli ve kendini tanitan.
public enum WidgetHTTP {
    static let session: URLSession = {
        let c = URLSessionConfiguration.ephemeral
        c.timeoutIntervalForRequest = 20
        c.httpAdditionalHeaders = ["User-Agent": "DockHub (+https://github.com/sametgurtuna/DockHub)"]
        return URLSession(configuration: c)
    }()

    public struct StatusError: Error, Sendable {
        public let status: Int
    }

    public static func get(_ url: URL, headers: [String: String] = [:]) async throws -> Data {
        try await send(URLRequest(url: url), headers: headers)
    }

    public static func send(_ request: URLRequest, headers: [String: String] = [:]) async throws -> Data {
        var r = request
        headers.forEach { r.setValue($0.value, forHTTPHeaderField: $0.key) }
        let (data, response) = try await session.data(for: r)
        let status = (response as? HTTPURLResponse)?.statusCode ?? 0
        guard (200..<300).contains(status) else { throw StatusError(status: status) }
        return data
    }
}

/// Paylasilan onbellek: ayni kaynak birden cok widget'ta tek istek (Windows'ta da oyle).
actor WidgetCache {
    static let shared = WidgetCache()
    private var entries: [String: (Date, Data)] = [:]

    func get(_ key: String, maxAge: TimeInterval) -> Data? {
        guard let entry = entries[key], Date().timeIntervalSince(entry.0) < maxAge else { return nil }
        return entry.1
    }

    func set(_ key: String, _ data: Data) { entries[key] = (Date(), data) }

    /// Suresi gecmis olsa da son veri (sinira takilinca eskisi gosterilir).
    func stale(_ key: String) -> Data? { entries[key]?.1 }
}

// MARK: - Takvim

public enum CalendarFeeds {
    /// Abonelik baglantilarindaki etkinlikler, bugunden `days` gun sonrasina (15 dk onbellek).
    public static func upcoming(_ links: [String], days: Int = 7) async throws -> [CalendarEntry] {
        let calendar = Calendar.current
        let from = calendar.startOfDay(for: Date())
        let to = from.addingTimeInterval(TimeInterval(days + 1) * 86_400)
        var entries: [CalendarEntry] = []
        var firstError: Error?
        for link in links {
            guard let url = ICalendar.feedURL(link) else { continue }
            do {
                let data: Data
                if let cached = await WidgetCache.shared.get("ics:" + link, maxAge: 15 * 60) {
                    data = cached
                } else {
                    data = try await WidgetHTTP.get(url)
                    await WidgetCache.shared.set("ics:" + link, data)
                }
                entries += ICalendar.occurrences(String(decoding: data, as: UTF8.self), from: from, to: to)
            } catch {
                firstError = firstError ?? error
                Log.error("Takvim okunamadi", error)
            }
        }
        if entries.isEmpty, let firstError { throw firstError }
        return ICalendar.upcoming(entries, now: Date())
    }
}

// MARK: - Hisseler ve doviz

public enum MarketService {
    /// Istenen sirayla; bir sembolun hatasi digerlerini gizlemez (15 dk onbellek).
    public static func stocks(_ symbols: [String]) async throws -> [StockQuote] {
        var quotes: [StockQuote] = []
        var firstError: Error?
        for symbol in symbols {
            guard let url = StockData.url(for: symbol) else { continue }
            do {
                let key = "stooq:" + symbol
                let data: Data
                if let cached = await WidgetCache.shared.get(key, maxAge: 15 * 60) {
                    data = cached
                } else {
                    data = try await WidgetHTTP.get(url)
                    await WidgetCache.shared.set(key, data)
                }
                if let q = StockData.parse(symbol, csv: String(decoding: data, as: UTF8.self)) { quotes.append(q) }
            } catch {
                firstError = firstError ?? error
            }
        }
        if quotes.isEmpty, let firstError { throw firstError }
        return quotes
    }

    /// Para birimleri ECB'den (Frankfurter), coinler CoinGecko'dan; bir saat onbellek.
    /// CoinGecko sinirina takilinca son bilinen fiyat gosterilir.
    public static func currencies(base: String, targets: [String]) async throws -> [CurrencyQuote] {
        let base = base.uppercased()
        var quotes: [CurrencyQuote] = []
        var firstError: Error?
        let fiat = targets.filter { CurrencyData.supported.contains($0) }
        if !fiat.isEmpty, let url = CurrencyData.frankfurterURL(base: base, targets: fiat) {
            let key = "fx:\(base):\(fiat.joined(separator: ","))"
            do {
                let data: Data
                if let cached = await WidgetCache.shared.get(key, maxAge: 3600) {
                    data = cached
                } else {
                    data = try await WidgetHTTP.get(url)
                    await WidgetCache.shared.set(key, data)
                }
                quotes += CurrencyData.fiatQuotes(base: base, targets: fiat, json: data)
            } catch {
                firstError = error
            }
        }
        for coin in targets where CurrencyData.cryptoIds[coin] != nil {
            guard let url = CurrencyData.coinGeckoURL(coin: coin, currency: base) else { continue }
            let key = "coin:\(coin):\(base)"
            do {
                let data: Data
                if let cached = await WidgetCache.shared.get(key, maxAge: 3600) {
                    data = cached
                } else {
                    do {
                        data = try await WidgetHTTP.get(url)
                        await WidgetCache.shared.set(key, data)
                    } catch let e as WidgetHTTP.StatusError where e.status == 429 {
                        guard let stale = await WidgetCache.shared.stale(key) else { throw e }
                        data = stale
                    }
                }
                if let q = CurrencyData.cryptoQuote(coin: coin, currency: base, json: data) { quotes.append(q) }
            } catch {
                firstError = firstError ?? error
            }
        }
        if quotes.isEmpty, let firstError { throw firstError }
        let bySymbol = Dictionary(quotes.map { ($0.symbol, $0) }, uniquingKeysWith: { a, _ in a })
        return targets.compactMap { bySymbol[$0] }
    }
}

// MARK: - Ping

public enum PingRunner {
    /// Tek ICMP yanki (`/sbin/ping -c 1`, 2 sn sinir); kayip ya da hatada nil.
    /// Hedef PingStats.target ile temizlenmis olmali (secenek ya da kabuk karakteri yok).
    public static func ping(_ target: String) async -> Double? {
        await Task.detached(priority: .utility) {
            let p = Process()
            p.executableURL = URL(fileURLWithPath: "/sbin/ping")
            p.arguments = ["-c", "1", "-t", "2", "-n", target]
            let pipe = Pipe()
            p.standardOutput = pipe
            p.standardError = Pipe()
            do { try p.run() } catch { return nil }
            let data = pipe.fileHandleForReading.readDataToEndOfFile()
            p.waitUntilExit()
            return PingStats.parse(String(decoding: data, as: UTF8.self))
        }.value
    }
}

// MARK: - Ekran goruntusu

public enum ScreenshotService {
    /// Secim aracini acar (Command-Shift-5 araci); kayit yeri macOS'un kendi ayarindan.
    public static func interactive() {
        run(["-iU"])
    }

    /// Her ekrani Resimler/Screenshots'a kaydeder, istenirse ilkini panoya kopyalar
    /// (Windows'taki "tum ekranlar" dugmesi). Ekran Kaydi izni ister.
    @MainActor
    public static func captureAll(delay: Int, copy: Bool) {
        let folder = FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Pictures/Screenshots")
        try? FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        let f = DateFormatter()
        f.dateFormat = "yyyy-MM-dd HH.mm.ss"
        let stamp = f.string(from: Date())
        let files = NSScreen.screens.indices.map { folder.appendingPathComponent("DockHub \(stamp)\($0 == 0 ? "" : " (\($0 + 1))").png") }
        var arguments = ["-x"]
        if delay > 0 { arguments += ["-T", String(min(delay, 30))] }
        arguments += files.map(\.path)
        run(arguments) {
            guard copy, let first = files.first, let image = NSImage(contentsOf: first) else { return }
            NSPasteboard.general.clearContents()
            NSPasteboard.general.writeObjects([image])
        }
    }

    private static func run(_ arguments: [String], then: (@MainActor @Sendable () -> Void)? = nil) {
        let p = Process()
        p.executableURL = URL(fileURLWithPath: "/usr/sbin/screencapture")
        p.arguments = arguments
        p.terminationHandler = { _ in
            if let then { Task { @MainActor in then() } }
        }
        do { try p.run() } catch { Log.error("screencapture baslatilamadi", error) }
    }
}

// MARK: - Klasor yigini

public enum FolderStackService {
    public static var downloads: URL {
        FileManager.default.urls(for: .downloadsDirectory, in: .userDomainMask).first
            ?? FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Downloads")
    }

    /// Klasordeki dosyalar; ayar bossa ya da klasor yoksa Indirilenler.
    public static func files(folder setting: String?, sort: String) -> (folder: URL, files: [StackFile]) {
        var folder = downloads
        if let setting, !setting.trimmingCharacters(in: .whitespaces).isEmpty {
            let custom = URL(fileURLWithPath: (setting as NSString).expandingTildeInPath)
            var isDir: ObjCBool = false
            if FileManager.default.fileExists(atPath: custom.path, isDirectory: &isDir), isDir.boolValue { folder = custom }
        }
        let keys: [URLResourceKey] = [.contentModificationDateKey, .isDirectoryKey]
        let urls = (try? FileManager.default.contentsOfDirectory(at: folder, includingPropertiesForKeys: keys,
                                                                  options: [.skipsHiddenFiles])) ?? []
        let files = urls.map { url -> StackFile in
            let values = try? url.resourceValues(forKeys: Set(keys))
            return StackFile(url: url, name: url.lastPathComponent,
                             modified: values?.contentModificationDate ?? .distantPast,
                             isDirectory: values?.isDirectory ?? false)
        }
        return (folder, FolderStack.sorted(files, by: sort))
    }
}

// MARK: - Anahtar zinciri

/// Todoist anahtari gibi sirlar. Windows DPAPI ile sifreleyip config'e yaziyor; Mac'te
/// Anahtar Zinciri'nde durur ve hic config'e girmez (eşitlemeye de gitmez).
public enum KeychainStore {
    static let service = "com.dockhub.mac"

    public static func get(_ account: String) -> String? {
        let query: [String: Any] = [kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: service,
                                    kSecAttrAccount as String: account, kSecReturnData as String: true,
                                    kSecMatchLimit as String: kSecMatchLimitOne]
        var result: AnyObject?
        guard SecItemCopyMatching(query as CFDictionary, &result) == errSecSuccess, let data = result as? Data else { return nil }
        return String(data: data, encoding: .utf8)
    }

    public static func set(_ value: String?, for account: String) {
        let query: [String: Any] = [kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: service,
                                    kSecAttrAccount as String: account]
        SecItemDelete(query as CFDictionary)
        guard let value, !value.isEmpty else { return }
        var add = query
        add[kSecValueData as String] = Data(value.utf8)
        let status = SecItemAdd(add as CFDictionary, nil)
        if status != errSecSuccess { Log.error("Anahtar zincirine yazilamadi (\(status))") }
    }
}

// MARK: - Yapilacaklar

public enum TodoService {
    public struct Unauthorized: Error, Sendable {}

    // Yerel liste: data/todo-<oge>.json (Windows'la ayni bicim).
    public static func loadLocal(_ itemId: String) -> LocalTodoList {
        JSONStore.load(LocalTodoList.self, from: AppPaths.widgetData("todo", itemId)) ?? LocalTodoList()
    }

    public static func saveLocal(_ list: LocalTodoList, _ itemId: String) {
        do { try JSONStore.save(list, to: AppPaths.widgetData("todo", itemId)) } catch { Log.error("Yapilacaklar kaydedilemedi", error) }
    }

    // Todoist (API v1): bugunun ve gecikmis gorevler.
    public static func todoistToday(token: String) async throws -> [TodoTask] {
        var tasks: [TodoTask] = []
        var cursor: String?
        for _ in 0..<5 {
            var c = URLComponents(string: TodoistData.baseURL + "tasks/filter")!
            c.queryItems = [URLQueryItem(name: "query", value: "today | overdue"), URLQueryItem(name: "limit", value: "200")]
            if let cursor { c.queryItems?.append(URLQueryItem(name: "cursor", value: cursor)) }
            let data = try await request(URLRequest(url: c.url!), token: token)
            let page = TodoistData.page(data)
            tasks += page.tasks
            cursor = page.cursor
            if cursor == nil { break }
        }
        return TodoistData.sorted(tasks)
    }

    public static func todoistClose(token: String, id: String) async throws {
        var r = URLRequest(url: URL(string: TodoistData.baseURL + "tasks/\(id.addingPercentEncoding(withAllowedCharacters: .alphanumerics) ?? id)/close")!)
        r.httpMethod = "POST"
        _ = try await request(r, token: token)
    }

    public static func todoistAdd(token: String, text: String) async throws {
        var r = URLRequest(url: URL(string: TodoistData.baseURL + "tasks")!)
        r.httpMethod = "POST"
        r.httpBody = try JSONSerialization.data(withJSONObject: ["content": text, "due_string": "today"])
        _ = try await request(r, token: token, headers: ["Content-Type": "application/json"])
    }

    private static func request(_ r: URLRequest, token: String, headers: [String: String] = [:]) async throws -> Data {
        do {
            return try await WidgetHTTP.send(r, headers: headers.merging(["Authorization": "Bearer " + token.trimmingCharacters(in: .whitespaces)]) { a, _ in a })
        } catch let e as WidgetHTTP.StatusError where e.status == 401 || e.status == 403 {
            throw Unauthorized()
        }
    }
}

// MARK: - Pano

/// Panoyu izler (macOS pano degisikligi icin bildirim vermez; changeCount saniyede bir okunur).
/// Gecmis yalniz bellekte kalir; parola yoneticilerinin isaretledigi veriler hic alinmaz.
@MainActor
public final class ClipboardMonitor: ObservableObject {
    public static let shared = ClipboardMonitor()
    @Published public private(set) var history = ClipboardHistory()
    private var timer: Timer?
    private var lastChange = NSPasteboard.general.changeCount
    /// Kendi yazdigimiz (tekrar kopyala) gecmise yeniden girmesin.
    private var ownChange: Int?

    private init() {}

    public func start() {
        guard timer == nil else { return }
        timer = Timer.scheduledTimer(withTimeInterval: 1, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated { self?.poll() }
        }
    }

    private func poll() {
        let pb = NSPasteboard.general
        guard pb.changeCount != lastChange else { return }
        lastChange = pb.changeCount
        if lastChange == ownChange { return }
        let types = (pb.types ?? []).map(\.rawValue)
        guard ClipboardHistory.shouldRecord(types: types) else { return }
        if let text = pb.string(forType: .string) {
            history.add(ClipboardEntry(text: text))
        } else if let image = NSImage(pasteboard: pb), let tiff = image.tiffRepresentation,
                  let png = NSBitmapImageRep(data: tiff)?.representation(using: .png, properties: [:]) {
            history.add(ClipboardEntry(image: png))
        }
    }

    public func copy(_ entry: ClipboardEntry) {
        let pb = NSPasteboard.general
        pb.clearContents()
        if let text = entry.text {
            pb.setString(text, forType: .string)
        } else if let data = entry.image, let image = NSImage(data: data) {
            pb.writeObjects([image])
        }
        ownChange = pb.changeCount
        lastChange = pb.changeCount
    }

    public func togglePin(_ id: UUID) { history.togglePin(id) }
    public func remove(_ id: UUID) { history.remove(id) }
    public func clear() { history.clear() }
}
