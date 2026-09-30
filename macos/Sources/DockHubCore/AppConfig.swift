import Foundation

/// ~/Library/Application Support/DockHub/config.json icerigi.
/// Windows karsiligi: src/CustomDock/Core/AppConfig.cs
///
/// Sema uyumu sozlesmesi (MIMARI.md bolum 3):
///  - Anahtarlar camelCase; Swift property adlari zaten camelCase oldugu icin
///    C#'in JsonNamingPolicy.CamelCase ciktisiyla birebir ortusur.
///  - Enum'lar PascalCase string (bkz. Enums.swift ham degerleri).
///  - Windows'taki HER alan burada da var, ayni varsayilanla ve ayni yazma
///    kuraliyla: nitelik yoksa nil de `null` yazilir (monitorDevice,
///    pinnedTrayIcons, syncFolder...), WhenWritingNull alanlari nil ise
///    (activeProfileId, widgets, widgetSettings, reserveSpace), WhenWritingDefault
///    alanlari false ise (welcomeShown, debugLogging) yazilmaz. CI'da
///    Scripts/compare-config-schema.py ve tests/fixtures/config-windows.json
///    (WindowsConfigTests) bunu denetler.
///  - Mac'te karsiligi olmayan alanlar (tepsi, Win+sayi, sanal masaustu...)
///    etkisizdir ama okunur ve aynen geri yazilir: dosya Windows'a donunce
///    ayar kaybolmaz.
///  - Bilinmeyen anahtarlar (daha yeni bir surumun ayarlari) `extras`'ta
///    saklanir ve geri yazilir.
///  - Eksik ya da tipi uymayan anahtar varsayilan degerle okunur (JSONObjectReader);
///    tek bir alan yuzunden dosya bozuk sayilmaz.
public struct AppConfig: Codable, Sendable, Equatable {
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
    public var combineButtons: CombineButtons = .always
    public var showTray: Bool = true
    public var showClock: Bool = true
    public var clockShowDate: Bool = true
    public var clockShowSeconds: Bool = false
    public var showDesktopButton: Bool = true
    public var showNetworkIcon: Bool = true
    public var showVolumeIcon: Bool = true
    public var showBatteryIcon: Bool = true
    public var showKeyboardLayout: Bool = true
    public var microphoneIcon: MicrophoneIconMode = .whenInUse
    public var showNotificationIndicator: Bool = true
    public var showDesktopIndicator: Bool = true
    public var runningAppsAllDesktops: Bool = false
    public var previewPeek: Bool = true
    public var searchButtonAction: SearchButtonAction = .windowsSearch
    public var launcherFileSearch: Bool = true
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
    public var runningIndicator: RunningIndicatorStyle = .line
    public var alignWidgetWidths: Bool = true
    public var widgetStyle: WidgetStyle = .cards
    public var topBar = TopBarSettings()
    /// Ayarlar, paneller ve menulerin yazi boyutu; 0 sistemi izler, aksi 1...2.
    public var textScale: Double = 0
    public var customPresets: [CustomLayoutPreset] = []
    /// Bir onayarin dock'tan kaldirdigi widget'larin ayarlari, widget turune gore.
    public var removedWidgetSettings: [String: JSONValue] = [:]
    public var smartAutoHide: Bool = false

    // ---------------- Erisilebilirlik, dil, guncelleme
    public var motion: MotionPreference = .system
    public var language: UiLanguage = .system
    public var checkForUpdates: Bool = true
    public var includePrereleases: Bool = false

    // ---------------- Klavye
    /// Windows'ta Win+1...9; macOS karsiligi docs/PARITE-1.0.md (P2).
    public var winNumberHotkeys: Bool = true
    /// Eylem kimligine gore genel kisayollar. Eksik: varsayilan, bos metin: kapali.
    public var hotkeys: [String: String] = [:]

    // ---------------- Esitleme
    public var syncFolder: String?
    public var syncDeviceId: String?
    /// C# DateTime? (UTC, ISO 8601). Mac yorumlamaz; metin olarak aynen tasinir.
    public var syncAppliedAt: String?

    /// Ilk calistirma karsilamasi tamamlandi (false ise yazilmaz).
    public var welcomeShown: Bool = false
    /// Yalniz config.json'dan acilir (false ise yazilmaz).
    public var debugLogging: Bool = false

    // ---------------- Profiller
    public var profiles: [DockProfile] = []
    public var activeProfileId: String?

    // ---------------- Ogeler
    public var items: [DockItem] = []

    // ---------------- v1 uyumlulugu (okunur, nil ise yazilmaz)
    public var widgets: [LegacyWidgetEntry]?
    public var widgetSettings: [String: JSONValue]?
    /// wf-reserved-space macOS'ta karsiliksiz (d-yok-cikarma); deger korunur,
    /// etkisi yoktur. Ayni dosya Windows'a donerse bilgi kaybolmasin.
    public var reserveSpace: Bool?

    /// Bu surumun bilmedigi anahtarlar; okundugu gibi geri yazilir.
    public var extras: [String: JSONValue] = [:]

    public init() {}

    // C# Math.Clamp karsiliklari
    public var clampedTintOpacity: Double { min(max(tintOpacity, 0), 1) }
    public var clampedEdgeMargin: Double { min(max(edgeMargin, 0), 32) }
    /// C#: TextScale setter'i. 0 (sistem) ya da 1...2.
    public var clampedTextScale: Double { textScale <= 0 ? 0 : min(max(textScale, 1), 2) }

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

    public init(from decoder: Decoder) throws {
        var r = try JSONObjectReader(decoder)
        let d = AppConfig()

        version = r("version", d.version)
        taskbarMode = r("taskbarMode", d.taskbarMode)
        hideOnFullscreen = r("hideOnFullscreen", d.hideOnFullscreen)
        startWithWindows = r("startWithWindows", d.startWithWindows)
        explorerPinMenu = r("explorerPinMenu", d.explorerPinMenu)
        showStartButton = r("showStartButton", d.showStartButton)
        showSearchButton = r("showSearchButton", d.showSearchButton)
        showTaskViewButton = r("showTaskViewButton", d.showTaskViewButton)
        showRunningApps = r("showRunningApps", d.showRunningApps)
        combineButtons = r("combineButtons", d.combineButtons)
        showTray = r("showTray", d.showTray)
        showClock = r("showClock", d.showClock)
        clockShowDate = r("clockShowDate", d.clockShowDate)
        clockShowSeconds = r("clockShowSeconds", d.clockShowSeconds)
        showDesktopButton = r("showDesktopButton", d.showDesktopButton)
        showNetworkIcon = r("showNetworkIcon", d.showNetworkIcon)
        showVolumeIcon = r("showVolumeIcon", d.showVolumeIcon)
        showBatteryIcon = r("showBatteryIcon", d.showBatteryIcon)
        showKeyboardLayout = r("showKeyboardLayout", d.showKeyboardLayout)
        microphoneIcon = r("microphoneIcon", d.microphoneIcon)
        showNotificationIndicator = r("showNotificationIndicator", d.showNotificationIndicator)
        showDesktopIndicator = r("showDesktopIndicator", d.showDesktopIndicator)
        runningAppsAllDesktops = r("runningAppsAllDesktops", d.runningAppsAllDesktops)
        previewPeek = r("previewPeek", d.previewPeek)
        searchButtonAction = r("searchButtonAction", d.searchButtonAction)
        launcherFileSearch = r("launcherFileSearch", d.launcherFileSearch)
        pinnedTrayIcons = r.optional("pinnedTrayIcons")
        knownTrayIcons = r("knownTrayIcons", d.knownTrayIcons)

        edge = r("edge", d.edge)
        monitorDevice = r.optional("monitorDevice")
        showOnAllDisplays = r("showOnAllDisplays", d.showOnAllDisplays)
        runningAppsOnOwnDisplay = r("runningAppsOnOwnDisplay", d.runningAppsOnOwnDisplay)
        autoHide = r("autoHide", d.autoHide)
        theme = r("theme", d.theme)
        backdrop = r("backdrop", d.backdrop)
        tintOpacity = r("tintOpacity", d.tintOpacity)
        size = r("size", d.size)
        displaySizes = r("displaySizes", d.displaySizes)
        layout = r("layout", d.layout)
        widthMode = r("widthMode", d.widthMode)
        alignment = r("alignment", d.alignment)
        edgeMargin = r("edgeMargin", d.edgeMargin)
        hoverEffect = r("hoverEffect", d.hoverEffect)
        runningIndicator = r("runningIndicator", d.runningIndicator)
        alignWidgetWidths = r("alignWidgetWidths", d.alignWidgetWidths)
        widgetStyle = r("widgetStyle", d.widgetStyle)
        topBar = r("topBar", d.topBar)
        textScale = r("textScale", d.textScale)
        customPresets = r("customPresets", d.customPresets)
        removedWidgetSettings = r("removedWidgetSettings", d.removedWidgetSettings)
        smartAutoHide = r("smartAutoHide", d.smartAutoHide)

        motion = r("motion", d.motion)
        language = r("language", d.language)
        checkForUpdates = r("checkForUpdates", d.checkForUpdates)
        includePrereleases = r("includePrereleases", d.includePrereleases)
        winNumberHotkeys = r("winNumberHotkeys", d.winNumberHotkeys)
        hotkeys = r("hotkeys", d.hotkeys)
        syncFolder = r.optional("syncFolder")
        syncDeviceId = r.optional("syncDeviceId")
        syncAppliedAt = r.optional("syncAppliedAt")
        welcomeShown = r("welcomeShown", d.welcomeShown)
        debugLogging = r("debugLogging", d.debugLogging)

        profiles = r("profiles", d.profiles)
        activeProfileId = r.optional("activeProfileId")
        items = r("items", d.items)

        widgets = r.optional("widgets")
        widgetSettings = r.optional("widgetSettings")
        reserveSpace = r.optional("reserveSpace")
        extras = r.extras()
    }

    public func encode(to encoder: Encoder) throws {
        var w = JSONObjectWriter(encoder)

        try w("version", version)
        try w("taskbarMode", taskbarMode)
        try w("hideOnFullscreen", hideOnFullscreen)
        try w("startWithWindows", startWithWindows)
        try w("explorerPinMenu", explorerPinMenu)
        try w("showStartButton", showStartButton)
        try w("showSearchButton", showSearchButton)
        try w("showTaskViewButton", showTaskViewButton)
        try w("showRunningApps", showRunningApps)
        try w("combineButtons", combineButtons)
        try w("showTray", showTray)
        try w("showClock", showClock)
        try w("clockShowDate", clockShowDate)
        try w("clockShowSeconds", clockShowSeconds)
        try w("showDesktopButton", showDesktopButton)
        try w("showNetworkIcon", showNetworkIcon)
        try w("showVolumeIcon", showVolumeIcon)
        try w("showBatteryIcon", showBatteryIcon)
        try w("showKeyboardLayout", showKeyboardLayout)
        try w("microphoneIcon", microphoneIcon)
        try w("showNotificationIndicator", showNotificationIndicator)
        try w("showDesktopIndicator", showDesktopIndicator)
        try w("runningAppsAllDesktops", runningAppsAllDesktops)
        try w("previewPeek", previewPeek)
        try w("searchButtonAction", searchButtonAction)
        try w("launcherFileSearch", launcherFileSearch)
        try w("pinnedTrayIcons", pinnedTrayIcons)              // nil -> null
        try w("knownTrayIcons", knownTrayIcons)

        try w("edge", edge)
        try w("monitorDevice", monitorDevice)                  // nil -> null
        try w("showOnAllDisplays", showOnAllDisplays)
        try w("runningAppsOnOwnDisplay", runningAppsOnOwnDisplay)
        try w("autoHide", autoHide)
        try w("theme", theme)
        try w("backdrop", backdrop)
        try w("tintOpacity", tintOpacity)
        try w("size", size)
        try w("displaySizes", displaySizes)                    // bossa {}
        try w("layout", layout)
        try w("widthMode", widthMode)
        try w("alignment", alignment)
        try w("edgeMargin", edgeMargin)
        try w("hoverEffect", hoverEffect)
        try w("runningIndicator", runningIndicator)
        try w("alignWidgetWidths", alignWidgetWidths)
        try w("widgetStyle", widgetStyle)
        try w("topBar", topBar)
        try w("textScale", textScale)
        try w("customPresets", customPresets)
        try w("removedWidgetSettings", removedWidgetSettings)
        try w("smartAutoHide", smartAutoHide)

        try w("motion", motion)
        try w("language", language)
        try w("checkForUpdates", checkForUpdates)
        try w("includePrereleases", includePrereleases)
        try w("winNumberHotkeys", winNumberHotkeys)
        try w("hotkeys", hotkeys)
        try w("syncFolder", syncFolder)                        // nil -> null
        try w("syncDeviceId", syncDeviceId)                    // nil -> null
        try w("syncAppliedAt", syncAppliedAt)                  // nil -> null
        try w.unlessFalse("welcomeShown", welcomeShown)
        try w.unlessFalse("debugLogging", debugLogging)

        try w("profiles", profiles)
        try w.unlessNil("activeProfileId", activeProfileId)
        try w("items", items)

        // nil ise YAZILMAZ (C# WhenWritingNull)
        try w.unlessNil("widgets", widgets)
        try w.unlessNil("widgetSettings", widgetSettings)
        try w.unlessNil("reserveSpace", reserveSpace)
        try w.extras(extras)
    }
}

/// C# karsiligi: LegacyWidgetEntry
public struct LegacyWidgetEntry: Codable, Sendable, Equatable {
    public var id: String = ""
    public var enabled: Bool = false
    public init() {}

    public init(from decoder: Decoder) throws {
        var r = try JSONObjectReader(decoder)
        id = r("id", "")
        enabled = r("enabled", false)
    }

    public func encode(to encoder: Encoder) throws {
        var w = JSONObjectWriter(encoder)
        try w("id", id)
        try w("enabled", enabled)
    }
}
