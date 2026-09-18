import AppKit
import Foundation
import DockHubCore

public struct TrashState: Sendable {
    public let itemCount: Int
    public let totalBytes: UInt64
    /// ~/.Trash OKUNABILDI mi? macOS'ta bu klasor Tam Disk Erisimi ister;
    /// izin yoksa "bos" ile "okunamadi" AYRI seylerdir ve karistirilmamalidir.
    public let accessible: Bool

    public var isEmpty: Bool { accessible && itemCount == 0 }

    public init(itemCount: Int, totalBytes: UInt64, accessible: Bool = true) {
        self.itemCount = itemCount; self.totalBytes = totalBytes; self.accessible = accessible
    }
}

/// Cop kutusu. Windows karsiligi: Services/RecycleBinService.cs
/// (SHQueryRecycleBin / SHEmptyRecycleBin).
/// Eslestirme ag-trash (partial): sorgulama, acma ve tasima genel API ile var;
/// BOSALTMANIN genel karsiligi YOK.
public enum TrashService {
    public static var trashURL: URL {
        (try? FileManager.default.url(for: .trashDirectory, in: .userDomainMask,
                                      appropriateFor: nil, create: false))
            ?? URL(fileURLWithPath: NSHomeDirectory()).appendingPathComponent(".Trash")
    }

    /// Yalniz kullanicinin ~/.Trash klasoru sayilir. Birim basina cop kutulari
    /// (/Volumes/X/.Trashes/uid) KAPSANMAZ; Windows surumu tum surucileri sayar.
    public static func read() -> TrashState {
        let fm = FileManager.default
        let icerik: [URL]
        do {
            icerik = try fm.contentsOfDirectory(
                at: trashURL,
                includingPropertiesForKeys: [.fileSizeKey, .isDirectoryKey],
                options: [.skipsHiddenFiles])
        } catch {
            // OLCULMUS: izin yoksa burasi hata verir. Eskiden 0 oge donuyordu,
            // yani "okuyamadim" durumu "bos" gibi gorunuyordu. Artik ayriliyor.
            Log.error("Cop kutusu okunamadi (Tam Disk Erisimi gerekebilir)", error)
            return TrashState(itemCount: 0, totalBytes: 0, accessible: false)
        }
        var toplam: UInt64 = 0
        for u in icerik {
            let v = try? u.resourceValues(forKeys: [.fileSizeKey])
            toplam &+= UInt64(v?.fileSize ?? 0)
        }
        return TrashState(itemCount: icerik.count, totalBytes: toplam)
    }

    public static func open() {
        NSWorkspace.shared.open(trashURL)
    }

    /// Dosyayi cop kutusuna tasir. Windows'taki surukle-birak silmenin karsiligi.
    @discardableResult
    public static func moveToTrash(_ url: URL) -> Bool {
        do {
            try FileManager.default.trashItem(at: url, resultingItemURL: nil)
            return true
        } catch {
            Log.error("Cop kutusuna tasinamadi: \(url.path)", error)
            return false
        }
    }
}
