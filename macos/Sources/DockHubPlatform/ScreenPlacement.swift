import AppKit
import DockHubCore

public struct DockGeometry: Sendable {
    public let frame: CGRect
    public let cornerRadius: CGFloat
    public let screenName: String
}

/// Yapilandirmadaki kenar/boyut/yerlesim degerlerini ekran koordinatina cevirir.
/// Windows karsiligi: Dock/DockWindow.xaml.cs yerlesim mantigi + Native/MonitorHelper.cs
/// ag-multi-monitor eslestirmesi: NSScreen.screens / .visibleFrame / .localizedName
public enum ScreenPlacement {
    /// Windows'ta monitorDevice bir aygit adi (\\.\DISPLAY2); macOS'ta
    /// NSScreen.localizedName ile eslestirilir. nil veya bulunamazsa birincil ekran.
    public static func screen(named name: String?) -> NSScreen? {
        guard let name, !name.isEmpty else { return NSScreen.main }
        return NSScreen.screens.first { $0.localizedName == name } ?? NSScreen.main
    }

    /// Icerik olculemediginde (ilk kurulum, bos dock) "Fit content" uzunlugu.
    public static let fitExtent: CGFloat = 420

    /// Dock'un yerlesecegi alan.
    ///
    /// OLCULMUS BULGU: sistem Dock'u otomatik gizlemeye alindiginda macOS
    /// `visibleFrame`i BUYUTMUYOR - rezerve alan oldugu gibi kaliyor. Iki ayri
    /// surecte, fare ekranin ortasindayken 10 saniye boyunca olculdu:
    /// autohide 0 iken de 1 iken de visibleFrame.minY = 65.
    /// Bu yuzden Replace modunda visibleFrame'e GUVENILMEZ; ekranin gercek
    /// kenarina yaslaniriz. Menu cubugu yine korunur (ust sinir visibleFrame'den).
    public static func usableArea(for config: AppConfig, on screen: NSScreen) -> CGRect {
        let full = screen.frame
        let visible = screen.visibleFrame
        switch config.taskbarMode {
        case .replace:
            // Yatayda tam genislik, altta gercek kenar, ustte menu cubugunun altI
            return CGRect(x: full.minX, y: full.minY,
                          width: full.width, height: visible.maxY - full.minY)
        case .showBoth:
            // Sistem Dock'u duruyor; onun ustune oturmayalim
            return visible
        }
    }

    /// `contentLength`: widthMode == .fit iken dock icerigin olculen uzunlugu
    /// (yatayda genislik, dikeyde yukseklik). Ekrana sigmazsa ekrana kirpilir,
    /// Windows'ta da Fit genisligi ekrani asamaz.
    public static func geometry(for config: AppConfig, on screen: NSScreen,
                                contentLength: CGFloat? = nil) -> DockGeometry {
        let area = usableArea(for: config, on: screen)
        let thickness = CGFloat(config.size.thickness)
        let margin = config.layout == .floating ? CGFloat(config.clampedEdgeMargin) : 0
        let radius: CGFloat = config.layout == .floating ? 12 : 0

        var frame = CGRect.zero

        if config.edge.isVertical {
            let height = config.widthMode == .full ? area.height - margin * 2
                : min(contentLength ?? fitExtent, area.height - margin * 2)
            let y: CGFloat = switch config.alignment {
            case .center: area.midY - height / 2
            case .start:  area.maxY - margin - height      // dikeyde "bas" = ust
            }
            let x: CGFloat = config.edge == .left
                ? area.minX + margin
                : area.maxX - margin - thickness
            frame = CGRect(x: x, y: y, width: thickness, height: height)
        } else {
            let width = config.widthMode == .full ? area.width - margin * 2
                : min(contentLength ?? fitExtent, area.width - margin * 2)
            let x: CGFloat = switch config.alignment {
            case .center: area.midX - width / 2
            case .start:  area.minX + margin
            }
            let y: CGFloat = config.edge == .bottom
                ? area.minY + margin
                : area.maxY - margin - thickness
            frame = CGRect(x: x, y: y, width: width, height: thickness)
        }

        return DockGeometry(frame: frame.integral, cornerRadius: radius,
                            screenName: screen.localizedName)
    }
}
