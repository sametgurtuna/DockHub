import Foundation

/// GitHub Releases varlik modeli.
public struct ReleaseAsset: Codable, Sendable {
    public let name: String
    public let size: Int64
    public let browserDownloadUrl: String

    enum CodingKeys: String, CodingKey {
        case name
        case size
        case browserDownloadUrl = "browser_download_url"
    }
}

/// GitHub Releases API yanit modeli.
public struct GitHubReleaseInfo: Codable, Sendable {
    public let tagName: String
    public let name: String?
    public let body: String?
    public let htmlUrl: String
    public let draft: Bool
    public let prerelease: Bool
    public let assets: [ReleaseAsset]

    enum CodingKeys: String, CodingKey {
        case tagName = "tag_name"
        case name
        case body
        case htmlUrl = "html_url"
        case draft
        case prerelease
        case assets
    }
}

/// DockHub macOS surum bilgisi.
public struct MacRelease: Sendable {
    public let version: String
    public let tagName: String
    public let name: String
    public let notes: String
    public let pageUrl: URL
    public let dmgUrl: URL?
    public let dmgSize: Int64

    public var hasDmg: Bool { dmgUrl != nil }

    public init(version: String, tagName: String, name: String, notes: String, pageUrl: URL, dmgUrl: URL?, dmgSize: Int64) {
        self.version = version
        self.tagName = tagName
        self.name = name
        self.notes = notes
        self.pageUrl = pageUrl
        self.dmgUrl = dmgUrl
        self.dmgSize = dmgSize
    }
}

/// GitHub uzerinden macOS guncellemelerini denetler.
/// Windows karsiligi: Services/UpdateService.cs
public final class UpdateService: @unchecked Sendable {
    public static let shared = UpdateService()

    public static let latestReleaseURL = URL(string: "https://api.github.com/repos/sametgurtuna/DockHub/releases/latest")!

    public static var currentVersion: String {
        let b = Bundle.main
        return b.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "0.10.0"
    }

    public init() {}

    /// GitHub'dan en son yayinlanan surumu sorgular.
    public func checkLatestRelease() async throws -> MacRelease? {
        var req = URLRequest(url: Self.latestReleaseURL)
        req.setValue("application/vnd.github+json", forHTTPHeaderField: "Accept")
        req.setValue("DockHub-macOS", forHTTPHeaderField: "User-Agent")
        req.timeoutInterval = 15

        let (data, response) = try await URLSession.shared.data(for: req)
        guard let http = response as? HTTPURLResponse, http.statusCode == 200 else {
            return nil
        }

        let release = try JSONDecoder().decode(GitHubReleaseInfo.self, from: data)
        if release.draft { return nil }

        let version = release.tagName.hasPrefix("v") ? String(release.tagName.dropFirst()) : release.tagName
        let dmgAsset = release.assets.first { $0.name.lowercased().hasSuffix(".dmg") }

        let macRelease = MacRelease(
            version: version,
            tagName: release.tagName,
            name: release.name ?? release.tagName,
            notes: release.body ?? "",
            pageUrl: URL(string: release.htmlUrl) ?? Self.latestReleaseURL,
            dmgUrl: dmgAsset.flatMap { URL(string: $0.browserDownloadUrl) },
            dmgSize: dmgAsset?.size ?? 0
        )

        return macRelease
    }

    /// Iki surum numarasini karsilastirir (orn. "1.0.0" vs "0.10.0").
    /// v1 > v2 ise pozitif, esitse 0, v1 < v2 ise negatif.
    public static func compareVersions(_ v1: String, _ v2: String) -> Int {
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
