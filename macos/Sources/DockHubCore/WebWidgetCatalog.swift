import Foundation

/// Web widget paketlerini kesfeder, dogrular ve yukler.
/// Windows karsiligi: Widgets/Web/WebWidgetCatalog.cs
public final class WebWidgetCatalog: @unchecked Sendable {
    public static let shared = WebWidgetCatalog()

    public static let category = "Web widgets"
    public static let maxPackageBytes: Int64 = 20 * 1024 * 1024       // 20 MB
    public static let maxUnpackedBytes: Int64 = 50 * 1024 * 1024      // 50 MB
    public static let maxPackageEntries = 256

    private static let idRegex = try! NSRegularExpression(pattern: "^[a-z0-9][a-z0-9.\\-]{2,63}$")

    private let lock = NSLock()
    private var _installed: [WebWidgetManifest] = []

    public var installed: [WebWidgetManifest] {
        lock.lock()
        defer { lock.unlock() }
        return _installed
    }

    public init() {}

    /// AppPaths.widgetsDir altindaki tum yuklu web widget'larini okur.
    public func loadAll() -> [WebWidgetManifest] {
        let dir = AppPaths.widgetsDir
        var list: [WebWidgetManifest] = []
        guard let items = try? FileManager.default.contentsOfDirectory(at: dir, includingPropertiesForKeys: [.isDirectoryKey]) else {
            lock.lock()
            _installed = []
            lock.unlock()
            return []
        }

        for folder in items {
            var isDir: ObjCBool = false
            if FileManager.default.fileExists(atPath: folder.path, isDirectory: &isDir), isDir.boolValue {
                if let manifest = try? Self.read(folder: folder) {
                    list.append(manifest)
                }
            }
        }

        lock.lock()
        _installed = list
        lock.unlock()
        return list
    }

    /// Belirtilen widgetId ("web.<id>") icin yuklu widget tanimini arar.
    public func find(_ widgetId: String) -> WidgetDefinition? {
        lock.lock()
        defer { lock.unlock() }
        guard let manifest = _installed.first(where: { $0.widgetId == widgetId }) else {
            return nil
        }
        return Self.definition(for: manifest)
    }

    /// Belirtilen manifestId ("<id>") icin yuklu manifesti dondurur.
    public func findManifest(_ id: String) -> WebWidgetManifest? {
        lock.lock()
        defer { lock.unlock() }
        return _installed.first(where: { $0.id == id })
    }

    /// Bir manifestten WidgetDefinition uretir.
    public static func definition(for manifest: WebWidgetManifest) -> WidgetDefinition {
        WidgetDefinition(
            id: manifest.widgetId,
            name: manifest.name,
            category: category,
            variants: manifest.variants.map { WidgetVariant($0.id, $0.name) }
        )
    }

    // ------------------------------------------------------------------ Ayristirma ve Dogrulama

    /// manifest.json metnini ayristirir ve alanlarini dogrular.
    public static func parse(json: String) throws -> WebWidgetManifest {
        guard let data = json.data(using: .utf8) else {
            throw WebWidgetError(L.t("manifest.json is empty."))
        }

        let manifest: WebWidgetManifest
        do {
            manifest = try JSONDecoder().decode(WebWidgetManifest.self, from: data)
        } catch {
            throw WebWidgetError(L.t("manifest.json is invalid: {0}", error.localizedDescription))
        }

        let range = NSRange(manifest.id.startIndex..<manifest.id.endIndex, in: manifest.id)
        if idRegex.firstMatch(in: manifest.id, options: [], range: range) == nil {
            throw WebWidgetError(L.t("The id must use lower-case letters, digits, dots and dashes."))
        }

        if manifest.name.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
            throw WebWidgetError(L.t("The name is missing."))
        }

        for key in manifest.permissions.networkFromSettings {
            guard let setting = manifest.settings.first(where: { $0.key == key && $0.type == "text" }) else {
                throw WebWidgetError(L.t("networkFromSettings names \"{0}\", which is not a text setting.", key))
            }
            if let def = setting.default, def != .null {
                throw WebWidgetError(L.t("The server setting \"{0}\" can't have a default value.", key))
            }
        }

        return manifest
    }

    /// Bir widget klasorunu okur ve giris dosyasini denetler.
    public static func read(folder: URL) throws -> WebWidgetManifest {
        let manifestFile = folder.appendingPathComponent("manifest.json")
        guard FileManager.default.fileExists(atPath: manifestFile.path) else {
            throw WebWidgetError(L.t("manifest.json is missing."))
        }

        let content = try String(contentsOf: manifestFile, encoding: .utf8)
        var manifest = try parse(json: content)

        let entryFile = folder.appendingPathComponent(manifest.entry).standardizedFileURL
        let folderStandard = folder.standardizedFileURL
        guard entryFile.path.hasPrefix(folderStandard.path),
              FileManager.default.fileExists(atPath: entryFile.path) else {
            throw WebWidgetError(L.t("The entry file {0} doesn't exist.", manifest.entry))
        }

        if let minVersion = manifest.minDockHubVersion,
           compareVersions(minVersion, "1.0.0") > 0 {
            throw WebWidgetError(L.t("Needs DockHub {0} or newer.", minVersion))
        }

        manifest.folder = folder
        return manifest
    }

    // ------------------------------------------------------------------ Host Eslestirme

    public static func isHostAllowed(manifest: WebWidgetManifest, host: String, settingsHosts: Set<String> = []) -> Bool {
        if host.caseInsensitiveCompare(manifest.hostName) == .orderedSame {
            return true
        }
        for allowed in manifest.permissions.network {
            if allowed.hasPrefix("*.") {
                let suffix = String(allowed.dropFirst(1)) // ".example.com"
                let domain = String(allowed.dropFirst(2)) // "example.com"
                if host.hasSuffix(suffix) || host.caseInsensitiveCompare(domain) == .orderedSame {
                    return true
                }
            } else if host.caseInsensitiveCompare(allowed) == .orderedSame {
                return true
            }
        }
        for sh in settingsHosts {
            if host.caseInsensitiveCompare(sh) == .orderedSame {
                return true
            }
        }
        return false
    }

    public static func settingsHosts(manifest: WebWidgetManifest, values: [String: JSONValue]) -> Set<String> {
        var hosts = Set<String>()
        for key in manifest.permissions.networkFromSettings {
            if let string = values[key]?.stringValue?.trimmingCharacters(in: .whitespacesAndNewlines),
               let url = URL(string: string),
               let scheme = url.scheme?.lowercased(), (scheme == "http" || scheme == "https"),
               let host = url.host, !host.isEmpty {
                hosts.insert(host)
            }
        }
        return hosts
    }

    // ------------------------------------------------------------------ Paket Yonetimi (.dockwidget / zip)

    /// .dockwidget (zip) dosyasinin guvenlik limitlerini denetler.
    public static func checkPackage(url: URL) throws {
        guard let attrs = try? FileManager.default.attributesOfItem(atPath: url.path),
              let fileSize = attrs[.size] as? Int64 else {
            throw WebWidgetError(L.t("The package can't be opened: {0}", "File not found"))
        }

        if fileSize > maxPackageBytes {
            throw WebWidgetError(L.t("The package is larger than {0} MB.", "\(maxPackageBytes / (1024 * 1024))"))
        }

        let entries = try inspectZipEntries(fileURL: url)
        if entries.count > maxPackageEntries {
            throw WebWidgetError(L.t("The package has more than {0} files.", "\(maxPackageEntries)"))
        }

        let unpackedTotal = entries.reduce(0) { $0 + $1.uncompressedSize }
        if unpackedTotal > maxUnpackedBytes {
            throw WebWidgetError(L.t("The package is larger than {0} MB.", "\(maxUnpackedBytes / (1024 * 1024))"))
        }

        for entry in entries {
            let name = entry.name
            if name.hasPrefix("/") || name.hasPrefix("\\") || name.contains("../") || name.contains("..\\") || name == ".." {
                throw WebWidgetError(L.t("The package can't be opened: {0}", "Unsafe entry path: \(name)"))
            }
        }
    }

    public struct ZipEntryInfo {
        public let name: String
        public let uncompressedSize: Int64
    }

    /// ZIP dosyasinin Central Directory'sini okuyarak oge sayisini ve acilmis boyutunu cikarir.
    public static func inspectZipEntries(fileURL: URL) throws -> [ZipEntryInfo] {
        let handle = try FileHandle(forReadingFrom: fileURL)
        defer { try? handle.close() }

        let fileSize = try handle.seekToEnd()
        guard fileSize >= 22 else {
            throw WebWidgetError(L.t("The package can't be opened: {0}", "File too short"))
        }

        let maxSearch = min(fileSize, 65557)
        try handle.seek(toOffset: fileSize - maxSearch)
        guard let tailData = try handle.readToEnd(), tailData.count >= 22 else {
            throw WebWidgetError(L.t("The package can't be opened: {0}", "Unable to read archive"))
        }

        // EOCD imzasini ara: 0x06054b50 (PK\x05\x06)
        var eocdOffsetInTail: Int? = nil
        let bytes = [UInt8](tailData)
        for i in stride(from: bytes.count - 22, through: 0, by: -1) {
            if bytes[i] == 0x50 && bytes[i+1] == 0x4b && bytes[i+2] == 0x05 && bytes[i+3] == 0x06 {
                eocdOffsetInTail = i
                break
            }
        }

        guard let eocdPos = eocdOffsetInTail else {
            throw WebWidgetError(L.t("The package can't be opened: {0}", "EOCD record not found"))
        }

        let entryCount = Int(readUInt16(bytes, eocdPos + 10))
        let cdSize = Int(readUInt32(bytes, eocdPos + 12))
        let cdOffset = UInt64(readUInt32(bytes, eocdPos + 16))

        guard cdOffset + UInt64(cdSize) <= fileSize else {
            throw WebWidgetError(L.t("The package can't be opened: {0}", "Invalid Central Directory offset"))
        }

        try handle.seek(toOffset: cdOffset)
        guard let cdData = try handle.read(upToCount: cdSize), cdData.count == cdSize else {
            throw WebWidgetError(L.t("The package can't be opened: {0}", "Failed to read Central Directory"))
        }

        let cdBytes = [UInt8](cdData)
        var offset = 0
        var entries: [ZipEntryInfo] = []

        while offset + 46 <= cdBytes.count {
            guard cdBytes[offset] == 0x50 && cdBytes[offset+1] == 0x4b &&
                  cdBytes[offset+2] == 0x01 && cdBytes[offset+3] == 0x02 else {
                break
            }

            let uncompressedSize = Int64(readUInt32(cdBytes, offset + 24))
            let nameLen = Int(readUInt16(cdBytes, offset + 28))
            let extraLen = Int(readUInt16(cdBytes, offset + 30))
            let commentLen = Int(readUInt16(cdBytes, offset + 32))

            guard offset + 46 + nameLen <= cdBytes.count else { break }
            let nameData = Data(cdBytes[(offset + 46)..<(offset + 46 + nameLen)])
            let name = String(decoding: nameData, as: UTF8.self)

            entries.append(ZipEntryInfo(name: name, uncompressedSize: uncompressedSize))
            offset += 46 + nameLen + extraLen + commentLen
        }

        return entries
    }

    private static func readUInt16(_ bytes: [UInt8], _ offset: Int) -> UInt16 {
        UInt16(bytes[offset]) | (UInt16(bytes[offset+1]) << 8)
    }

    private static func readUInt32(_ bytes: [UInt8], _ offset: Int) -> UInt32 {
        UInt32(bytes[offset]) | (UInt32(bytes[offset+1]) << 8) | (UInt32(bytes[offset+2]) << 16) | (UInt32(bytes[offset+3]) << 24)
    }

    /// .dockwidget paketini gecici klasore acar ve manifestini inceler.
    public static func inspect(packageURL: URL) throws -> (manifest: WebWidgetManifest, tempDir: URL) {
        try checkPackage(url: packageURL)

        let tempDir = URL(fileURLWithPath: NSTemporaryDirectory())
            .appendingPathComponent("dockhub-widget-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: tempDir, withIntermediateDirectories: true)

        let process = Process()
        process.executableURL = URL(fileURLWithPath: "/usr/bin/ditto")
        process.arguments = ["-xk", packageURL.path, tempDir.path]
        try process.run()
        process.waitUntilExit()

        guard process.terminationStatus == 0 else {
            throw WebWidgetError(L.t("The package can't be opened: {0}", "Extraction failed"))
        }

        // manifest.json kokte mi yoksa tek bir alt klasor icinde mi?
        var rootDir = tempDir
        let directManifest = tempDir.appendingPathComponent("manifest.json")
        if !FileManager.default.fileExists(atPath: directManifest.path) {
            let subdirs = (try? FileManager.default.contentsOfDirectory(at: tempDir, includingPropertiesForKeys: [.isDirectoryKey])) ?? []
            for sub in subdirs {
                if FileManager.default.fileExists(atPath: sub.appendingPathComponent("manifest.json").path) {
                    rootDir = sub
                    break
                }
            }
        }

        let manifest = try read(folder: rootDir)
        return (manifest, tempDir)
    }

    /// Incelenmis bir paketi widgets klasorune tasir/yukler.
    @discardableResult
    public func install(manifest: WebWidgetManifest) throws -> WebWidgetManifest {
        guard let sourceFolder = manifest.folder else {
            throw WebWidgetError("Source folder is missing")
        }

        let destFolder = AppPaths.widgetsDir.appendingPathComponent(manifest.id)
        if FileManager.default.fileExists(atPath: destFolder.path) {
            try? FileManager.default.removeItem(at: destFolder)
        }
        try FileManager.default.createDirectory(at: AppPaths.widgetsDir, withIntermediateDirectories: true)
        try FileManager.default.copyItem(at: sourceFolder, to: destFolder)

        let installedManifest = try Self.read(folder: destFolder)

        lock.lock()
        _installed.removeAll { $0.id == installedManifest.id }
        _installed.append(installedManifest)
        lock.unlock()

        return installedManifest
    }

    /// Bir web widget'ini siler.
    public func uninstall(id: String) throws {
        let destFolder = AppPaths.widgetsDir.appendingPathComponent(id)
        if FileManager.default.fileExists(atPath: destFolder.path) {
            try FileManager.default.removeItem(at: destFolder)
        }
        lock.lock()
        _installed.removeAll { $0.id == id }
        lock.unlock()
    }

    // ------------------------------------------------------------------ Surum Karsilastirma

    private static func compareVersions(_ v1: String, _ v2: String) -> Int {
        let p1 = v1.split(separator: ".").compactMap { Int($0) }
        let p2 = v2.split(separator: ".").compactMap { Int($0) }
        let count = max(p1.count, p2.count)
        for i in 0..<count {
            let n1 = i < p1.count ? p1[i] : 0
            let n2 = i < p2.count ? p2[i] : 0
            if n1 != n2 { return n1 < n2 ? -1 : 1 }
        }
        return 0
    }
}
