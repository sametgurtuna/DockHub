import Foundation

/// Ayarlar penceresinin dock'a haber verdigi bildirim ve "dock yeniden
/// kurulmali mi" karari. Windows karsiligi: AppConfig.PropertyChanged +
/// DockWindow.ApplySettings.
public enum ConfigEvents {
    /// Ayarlar penceresi config'i degistirip diske yazdiktan sonra gonderilir.
    public static let didChange = Notification.Name("DockHub.configDidChange")
}

public extension AppConfig {
    /// Dock'un gorunumunu ve yerlesimini belirleyen her seyin imzasi: widget
    /// ogelerinin kendi ayarlari (settings) HARIC butun config.
    ///
    /// Neden: widget kendi ayarini (su sayaci, not metni) DockModel.setSetting
    /// ile yaziyor ve gorunum zaten tazeleniyor. Dock'u bunun icin yeniden
    /// kurmak her tiklamada titreme demek. Kenar, tema, oge sirasi, varyant
    /// ya da ad degisimi ise yeniden kurmayi gerektirir.
    func layoutSignature() -> Data {
        func ayarsiz(_ items: [DockItem]) -> [DockItem] {
            items.map { item in
                var it = item
                it.settings = nil
                it.children = it.children.map(ayarsiz)
                return it
            }
        }
        var kopya = self
        kopya.items = ayarsiz(items)
        return (try? JSONStore.encoder.encode(kopya)) ?? Data()
    }
}

/// Uygulama ogesinin baslatma argumanlari (DockItem.arguments). Windows bu
/// metni ShellExecute'a oldugu gibi verir; macOS'ta NSWorkspace argumanlari
/// dizi olarak ister, bu yuzden kabuk benzeri bolunur: bosluk ayirir, cift
/// veya tek tirnak icindeki bosluk korunur, ters bolu sonraki karakteri kacirir.
public enum LaunchArguments {
    public static func split(_ text: String) -> [String] {
        var sonuc: [String] = []
        var parca = ""
        var varParca = false
        var tirnak: Character?
        var kacis = false
        for c in text {
            if kacis { parca.append(c); varParca = true; kacis = false; continue }
            if c == "\\" && tirnak != "'" { kacis = true; continue }
            if let t = tirnak {
                if c == t { tirnak = nil } else { parca.append(c) }
                continue
            }
            if c == "\"" || c == "'" { tirnak = c; varParca = true; continue }
            if c.isWhitespace {
                if varParca { sonuc.append(parca); parca = ""; varParca = false }
                continue
            }
            parca.append(c); varParca = true
        }
        if varParca { sonuc.append(parca) }
        return sonuc
    }
}
