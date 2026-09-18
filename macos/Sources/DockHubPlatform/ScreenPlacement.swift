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

    /// Icerige gore genislik (widthMode == .fit) icin iskelet degeri.
    public static let fitExtent: CGFloat = 420

    public static func geometry(for config: AppConfig, on screen: NSScreen) -> DockGeometry {
        // visibleFrame kullaniliyor: sistem Dock'u ve menu cubugunun disinda kalan
        // alan. ag-reserved-space karsiliksiz oldugu icin (d-yok-cikarma) burada
        // alan rezerve etmiyoruz, yalnizca mevcut kullanilabilir alani okuyoruz.
        let area = screen.visibleFrame
        let thickness = CGFloat(config.size.thickness)
        let margin = config.layout == .floating ? CGFloat(config.clampedEdgeMargin) : 0
        let radius: CGFloat = config.layout == .floating ? 12 : 0

        var frame = CGRect.zero

        if config.edge.isVertical {
            let height = config.widthMode == .full ? area.height - margin * 2 : fitExtent
            let y: CGFloat = switch config.alignment {
            case .center: area.midY - height / 2
            case .start:  area.maxY - margin - height      // dikeyde "bas" = ust
            }
            let x: CGFloat = config.edge == .left
                ? area.minX + margin
                : area.maxX - margin - thickness
            frame = CGRect(x: x, y: y, width: thickness, height: height)
        } else {
            let width = config.widthMode == .full ? area.width - margin * 2 : fitExtent
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
