import Foundation

/// ~/Library/Application Support/DockHub/config.json icerigi.
/// Windows karsiligi: src/CustomDock/Core/AppConfig.cs
///
/// Sema uyumu sozlesmesi (MIMARI.md bolum 3):
///  - Anahtarlar camelCase; Swift property adlari zaten camelCase oldugu icin
///    C#'in JsonNamingPolicy.CamelCase ciktisiyla birebir ortusur.
///  - Enum'lar PascalCase string (bkz. Enums.swift ham degerleri).
///  - `monitorDevice` ve `pinnedTrayIcons` C# tarafinda [JsonIgnore(WhenWritingNull)]
///    TASIMIYOR, yani nil oldugunda bile `null` olarak YAZILIR.
///  - `widgets`, `widgetSettings`, `reserveSpace` v1 uyum alanlaridir ve
///    WhenWritingNull tasir: nil ise yazilmaz. Okunup aynen geri yazilirlar.
///  - Eksik anahtar varsayilan degerle okunur. System.Text.Json da boyle yapar;
///    Swift'in sentezledigi init(from:) ise eksik anahtarda hata atar ve
///    ConfigService dosyayi bozuk sayip varsayilanlara doner. Yeni surumde
///    eklenen bir alan (v0.6: showOnAllDisplays vb.) eski dosyayi okunamaz
///    hale getirmesin diye init(from:) elle yazildi.
public struct AppConfig: Codable, Sendable {
    public static let currentVersion = 2

    public var version: Int = currentVersion

    // ---------------- Genel / gorev cubugu
    public var taskbarMode: TaskbarMode = .replace
    public var hideOnFullscreen: Bool = true
    public var startWithWindows: Bool = true
    public var explorerPinMenu: Bool = true
    public var showStartButton: Bool = true
    public var showSearchButton: Bool = true
    public var showTaskViewButton: Bool = false
    public var showRunningApps: Bool = true
    public var showTray: Bool = true
    public var showClock: Bool = true
    public var clockShowDate: Bool = true
    public var clockShowSeconds: Bool = false
    public var showDesktopButton: Bool = true
    /// null = Windows ayarlarindan ice aktar. macOS'ta wf-tray parite disi
    /// (d-yok-cikarma) oldugu icin okunur ve aynen geri yazilir.
    public var pinnedTrayIcons: [String]?
    public var knownTrayIcons: [String] = []

    // ---------------- Gorunum / yerlesim
    public var edge: DockEdge = .bottom
    /// Windows'ta monitor aygit adi (ör. \\.\DISPLAY2). macOS'ta
    /// NSScreen.localizedName kullanilir. null = birincil ekran.
    public var monitorDevice: String?
    /// Diger ekranlara da dock konur; widget'lar ve tepsi ana ekranda kalir.
    public var showOnAllDisplays: Bool = false
    /// Tum ekranlarda dock varken: sabitlenmemis calisan uygulama yalniz
    /// penceresinin bulundugu ekrandaki dock'ta gorunur.
    public var runningAppsOnOwnDisplay: Bool = true
    public var autoHide: Bool = false
    public var theme: ThemePreference = .dark
    public var backdrop: BackdropKind = .blur
    public var tintOpacity: Double = 0.55
    /// Ana dock'un (ve kendi boyutu olmayan diger ekranlarin) boyutu.
    public var size: DockSize = .small
    /// Diger ekranlarin kendi boyutlari. Anahtar ekran adi (monitorDevice gibi
    /// NSScreen.localizedName); C# tarafinda buyuk/kucuk harf duyarsiz sozluk.
    public var displaySizes: [String: DockSize] = [:]
    public var layout: DockLayout = .floating
    public var widthMode: DockWidthMode = .full
    public var alignment: DockAlignment = .center
    public var edgeMargin: Double = 6
    public var hoverEffect: Bool = true

    // ---------------- Ogeler
    public var items: [DockItem] = []

    // ---------------- v1 uyumlulugu (okunur, nil ise yazilmaz)
    public var widgets: [LegacyWidgetEntry]?
    public var widgetSettings: [String: JSONValue]?
    /// wf-reserved-space macOS'ta karsiliksiz (d-yok-cikarma); deger korunur,
    /// etkisi yoktur. Ayni dosya Windows'a donerse bilgi kaybolmasin.
    public var reserveSpace: Bool?

    public init() {}

    // C# Math.Clamp karsiliklari
    public var clampedTintOpacity: Double { min(max(tintOpacity, 0), 1) }
    public var clampedEdgeMargin: Double { min(max(edgeMargin, 0), 32) }

    /// C#: DisplaySizeOf. Ekranin kendi boyutu; yoksa nil (ana boyut gecerli).
    public func displaySize(of display: String) -> DockSize? {
        displaySizes.first { $0.key.caseInsensitiveCompare(display) == .orderedSame }?.value
    }

    /// C#: SetDisplaySize. nil verilirse ekranin kendi boyutu silinir.
    public mutating func setDisplaySize(_ size: DockSize?, of display: String) {
        for key in displaySizes.keys where key.caseInsensitiveCompare(display) == .orderedSame {
            displaySizes.removeValue(forKey: key)
        }
        if let size { displaySizes[display] = size }
    }

    enum CodingKeys: String, CodingKey {
        case version, taskbarMode, hideOnFullscreen, startWithWindows, explorerPinMenu
        case showStartButton, showSearchButton, showTaskViewButton, showRunningApps
        case showTray, showClock, clockShowDate, clockShowSeconds, showDesktopButton
        case pinnedTrayIcons, knownTrayIcons
        case edge, monitorDevice, showOnAllDisplays, runningAppsOnOwnDisplay
        case autoHide, theme, backdrop, tintOpacity
        case size, displaySizes, layout, widthMode, alignment, edgeMargin, hoverEffect
        case items, widgets, widgetSettings, reserveSpace
    }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        let d = AppConfig()
        func v<T: Decodable>(_ key: CodingKeys, _ fallback: T) throws -> T {
            try c.decodeIfPresent(T.self, forKey: key) ?? fallback
        }

        version = try v(.version, d.version)
        taskbarMode = try v(.taskbarMode, d.taskbarMode)
        hideOnFullscreen = try v(.hideOnFullscreen, d.hideOnFullscreen)
        startWithWindows = try v(.startWithWindows, d.startWithWindows)
        explorerPinMenu = try v(.explorerPinMenu, d.explorerPinMenu)
        showStartButton = try v(.showStartButton, d.showStartButton)
        showSearchButton = try v(.showSearchButton, d.showSearchButton)
        showTaskViewButton = try v(.showTaskViewButton, d.showTaskViewButton)
        showRunningApps = try v(.showRunningApps, d.showRunningApps)
        showTray = try v(.showTray, d.showTray)
        showClock = try v(.showClock, d.showClock)
        clockShowDate = try v(.clockShowDate, d.clockShowDate)
        clockShowSeconds = try v(.clockShowSeconds, d.clockShowSeconds)
        showDesktopButton = try v(.showDesktopButton, d.showDesktopButton)
        pinnedTrayIcons = try c.decodeIfPresent([String].self, forKey: .pinnedTrayIcons)
        knownTrayIcons = try v(.knownTrayIcons, d.knownTrayIcons)

        edge = try v(.edge, d.edge)
        monitorDevice = try c.decodeIfPresent(String.self, forKey: .monitorDevice)
        showOnAllDisplays = try v(.showOnAllDisplays, d.showOnAllDisplays)
        runningAppsOnOwnDisplay = try v(.runningAppsOnOwnDisplay, d.runningAppsOnOwnDisplay)
        autoHide = try v(.autoHide, d.autoHide)
        theme = try v(.theme, d.theme)
        backdrop = try v(.backdrop, d.backdrop)
        tintOpacity = try v(.tintOpacity, d.tintOpacity)
        size = try v(.size, d.size)
        displaySizes = try v(.displaySizes, d.displaySizes)
        layout = try v(.layout, d.layout)
        widthMode = try v(.widthMode, d.widthMode)
        alignment = try v(.alignment, d.alignment)
        edgeMargin = try v(.edgeMargin, d.edgeMargin)
        hoverEffect = try v(.hoverEffect, d.hoverEffect)

        items = try v(.items, d.items)
        widgets = try c.decodeIfPresent([LegacyWidgetEntry].self, forKey: .widgets)
        widgetSettings = try c.decodeIfPresent([String: JSONValue].self, forKey: .widgetSettings)
        reserveSpace = try c.decodeIfPresent(Bool.self, forKey: .reserveSpace)
    }

    // Bazi nil'ler null yazilmali, bazilari hic yazilmamali; bu yuzden elle.
    public func encode(to encoder: Encoder) throws {
        var c = encoder.container(keyedBy: CodingKeys.self)

        try c.encode(version, forKey: .version)
        try c.encode(taskbarMode, forKey: .taskbarMode)
        try c.encode(hideOnFullscreen, forKey: .hideOnFullscreen)
        try c.encode(startWithWindows, forKey: .startWithWindows)
        try c.encode(explorerPinMenu, forKey: .explorerPinMenu)
        try c.encode(showStartButton, forKey: .showStartButton)
        try c.encode(showSearchButton, forKey: .showSearchButton)
        try c.encode(showTaskViewButton, forKey: .showTaskViewButton)
        try c.encode(showRunningApps, forKey: .showRunningApps)
        try c.encode(showTray, forKey: .showTray)
        try c.encode(showClock, forKey: .showClock)
        try c.encode(clockShowDate, forKey: .clockShowDate)
        try c.encode(clockShowSeconds, forKey: .clockShowSeconds)
        try c.encode(showDesktopButton, forKey: .showDesktopButton)

        // nil ise `null` yazilir (C# WhenWritingNull TASIMIYOR)
        try c.encode(pinnedTrayIcons, forKey: .pinnedTrayIcons)
        try c.encode(knownTrayIcons, forKey: .knownTrayIcons)

        try c.encode(edge, forKey: .edge)
        try c.encode(monitorDevice, forKey: .monitorDevice)   // nil -> null
        try c.encode(showOnAllDisplays, forKey: .showOnAllDisplays)
        try c.encode(runningAppsOnOwnDisplay, forKey: .runningAppsOnOwnDisplay)
        try c.encode(autoHide, forKey: .autoHide)
        try c.encode(theme, forKey: .theme)
        try c.encode(backdrop, forKey: .backdrop)
        try c.encode(tintOpacity, forKey: .tintOpacity)
        try c.encode(size, forKey: .size)
        try c.encode(displaySizes, forKey: .displaySizes)     // bossa {}
        try c.encode(layout, forKey: .layout)
        try c.encode(widthMode, forKey: .widthMode)
        try c.encode(alignment, forKey: .alignment)
        try c.encode(edgeMargin, forKey: .edgeMargin)
        try c.encode(hoverEffect, forKey: .hoverEffect)
        try c.encode(items, forKey: .items)

        // nil ise YAZILMAZ (C# WhenWritingNull)
        try c.encodeIfPresent(widgets, forKey: .widgets)
        try c.encodeIfPresent(widgetSettings, forKey: .widgetSettings)
        try c.encodeIfPresent(reserveSpace, forKey: .reserveSpace)
    }
}

/// C# karsiligi: LegacyWidgetEntry
public struct LegacyWidgetEntry: Codable, Sendable {
    public var id: String = ""
    public var enabled: Bool = false
    public init() {}
}
