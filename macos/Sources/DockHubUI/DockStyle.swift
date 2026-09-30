import AppKit

/// Dock'un gorsel dili. Degerler koda sabit yazilmaz, cubuk yuksekliginden
/// ORANLA turetilir; boylece Small/Medium/Large boyutlarinda tutarli kalir.
///
/// Oranlar karar `d-gorsel-hedef` icindeki olculmus degerlerden gelir
/// (bkz. docs/GORSEL-DIL.md). Referansta cubuk yuksekligi 42.9 birimdi.
public struct DockStyle: Sendable {
    public let height: CGFloat

    public init(height: CGFloat) { self.height = height }

    // ---- Olculmus oranlar (referans / 42.9)
    /// 12.24 / 42.9
    public var cornerRadius: CGFloat { height * 0.285 }
    /// 4.23 / 42.9
    public var padding: CGFloat { height * 0.098 }
    /// 4 / 42.9
    public var gap: CGFloat { height * 0.093 }
    /// 34 / 42.9 — oge kutucugunun yuksekligi
    public var itemHeight: CGFloat { height * 0.792 }
    /// 14 / 34 — kutucuk yariciapi, kendi yuksekligine oranli
    public var itemRadius: CGFloat { itemHeight * 0.412 }
    /// Uygulama ikonu kutucuktan biraz kucuk durur
    public var iconSize: CGFloat { itemHeight * 0.82 }
    /// Calisiyor noktasi
    public var runningDot: CGFloat { max(3, height * 0.075) }

    // ---- Olculmus renkler
    /// Cam kenar isigi: ust kenar belirgin, alt kenar cok hafif.
    /// Cam hissini veren asil ayrinti bu.
    public static let topEdgeLight = NSColor(white: 1, alpha: 0.133)
    public static let bottomEdgeLight = NSColor(white: 1, alpha: 0.04)
    /// Widget kutucugu arka plani: neredeyse gorunmez
    public static let itemBackground = NSColor(white: 1, alpha: 0.04)
    public static let separatorColor = NSColor(white: 1, alpha: 0.12)

    /// Klasor vurgu renkleri. Windows config'e WPF firca anahtarini yazar
    /// (Themes/Dark.xaml); degerler zaten macOS sistem renklerinin koyu tema
    /// karsiliklari, o yuzden sistem rengine eslenir ve acik temaya da uyar.
    /// Bilinmeyen anahtar -> systemBlue (C# varsayilani AccentBlueBrush).
    public static func groupAccent(_ key: String?) -> NSColor {
        switch key {
        case "AccentGreenBrush":   .systemGreen
        case "AccentOrangeBrush":  .systemOrange
        case "AccentCyanBrush":    .systemCyan
        case "AccentMagentaBrush", "AccentPurpleBrush": .systemPurple
        case "AccentPinkBrush":    .systemPink
        case "AccentYellowBrush":  .systemYellow
        case "AccentRedBrush":     .systemRed
        default:                   .systemBlue
        }
    }

    // ---- Dis golge: 0 5px 20px rgba(0,0,0,0.2)
    public static let shadowColor = NSColor(white: 0, alpha: 0.2)
    public var shadowRadius: CGFloat { height * 0.47 }
    public var shadowOffsetY: CGFloat { -height * 0.12 }
}
