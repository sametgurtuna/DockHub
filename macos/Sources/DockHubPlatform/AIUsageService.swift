import Foundation
import DockHubCore

public struct AIUsage: Sendable, Equatable {
    public let sessionPercent: Double
    public let sessionResets: String
    public let weekPercent: Double
    public let weekResets: String
}

/// Claude CLI kullanim bilgisi. Windows karsiligi: Services/AIUsageService.cs
/// (cmd.exe /c claude -p "/usage" + regex). macOS'ta cmd.exe sarmalayicisi
/// gerekmez, CLI dogrudan calistirilir.
/// Eslestirme ag-ai-usage.
public enum AIUsageService {
    public enum Sonuc: Sendable {
        case veri(AIUsage)
        case cliYok
        case okunamadi(String)
    }

    /// PATH'te olmayabilir; bilinen kurulum yerlerine de bakilir.
    public static func cliPath() -> String? {
        let adaylar = [
            "\(NSHomeDirectory())/.claude/local/claude",
            "\(NSHomeDirectory())/.local/bin/claude",
            "/opt/homebrew/bin/claude",
            "/usr/local/bin/claude",
        ]
        for p in adaylar where FileManager.default.isExecutableFile(atPath: p) { return p }
        // PATH'te ara
        let which = Process()
        which.executableURL = URL(fileURLWithPath: "/usr/bin/env")
        which.arguments = ["which", "claude"]
        let pipe = Pipe(); which.standardOutput = pipe; which.standardError = Pipe()
        guard (try? which.run()) != nil else { return nil }
        let d = pipe.fileHandleForReading.readDataToEndOfFile()
        which.waitUntilExit()
        let yol = String(data: d, encoding: .utf8)?.trimmingCharacters(in: .whitespacesAndNewlines)
        return (yol?.isEmpty == false) ? yol : nil
    }

    public static func read(timeout: TimeInterval = 20) -> Sonuc {
        guard let yol = cliPath() else { return .cliYok }

        let p = Process()
        p.executableURL = URL(fileURLWithPath: yol)
        p.arguments = ["-p", "/usage"]
        let pipe = Pipe(); p.standardOutput = pipe; p.standardError = Pipe()
        do { try p.run() } catch { return .okunamadi(error.localizedDescription) }

        let bitis = Date().addingTimeInterval(timeout)
        while p.isRunning && Date() < bitis { usleep(100_000) }
        if p.isRunning { p.terminate(); return .okunamadi("zaman asimi") }

        let metin = String(data: pipe.fileHandleForReading.readDataToEndOfFile(),
                           encoding: .utf8) ?? ""
        guard let u = parse(metin) else { return .okunamadi("cikti ayristirilamadi") }
        return .veri(u)
    }

    /// Windows surumundeki regex'lerin birebir karsiligi.
    public static func parse(_ text: String) -> AIUsage? {
        func bul(_ anahtar: String) -> (Double, String)? {
            let kalip = "\(anahtar)[^\\r\\n%]*?(\\d{1,3})%\\s*used[^\\r\\n(]*?resets\\s+([^\\r\\n(]+?)\\s*(?:\\(|$)"
            guard let rx = try? NSRegularExpression(pattern: kalip, options: [.caseInsensitive]),
                  let m = rx.firstMatch(in: text, range: NSRange(text.startIndex..., in: text)),
                  let r1 = Range(m.range(at: 1), in: text),
                  let r2 = Range(m.range(at: 2), in: text),
                  let v = Double(text[r1]) else { return nil }
            return (v, String(text[r2]).trimmingCharacters(in: .whitespaces))
        }
        let s = bul("current\\s+session")
        let w = bul("current\\s+week")
        guard s != nil || w != nil else { return nil }
        return AIUsage(sessionPercent: s?.0 ?? 0, sessionResets: s?.1 ?? "",
                       weekPercent: w?.0 ?? 0, weekResets: w?.1 ?? "")
    }
}
