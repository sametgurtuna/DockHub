import Foundation
import DockHubCore

/// Galeride widget'larin aciklamasi ve simgesi.
/// Kayit defterinden (DockHubCore/WidgetRegistry) ayri tutuluyor: defter
/// config sozlesmesidir (kimlik, varyant), bu dosya yalniz sunum.
///
/// Aciklamalar Windows v0.6.1 WidgetRegistry.cs metinlerinden; macOS'ta
/// farkli calisan kisimlar durustce uyarlandi (medya yalniz Music ve
/// Spotify, cop kutusu Finder'dan bosaltilir, hatirlaticilar gunluk).
enum WidgetCatalog {
    struct Info {
        let summary: String
        let symbol: String
    }

    /// Windows'taki kategori sirasi; macOS'a ozgu ekler en sonda.
    static let categoryOrder = ["Clocks", "Reminders", "Productivity", "Sticky notes", "Media",
                                "System", "Weather", "AI", "Extras"]

    /// Aciklama arayuz dilinde (tablodaki Ingilizce metin ceviri anahtari).
    static func info(_ id: String) -> Info {
        guard let info = table[id] else { return Info(summary: "", symbol: "square.dashed") }
        return Info(summary: L.t(info.summary), symbol: info.symbol)
    }

    /// Aciklamanin Ingilizcesi (arama iki dilde de bulsun).
    static func englishSummary(_ id: String) -> String { table[id]?.summary ?? "" }

    private static let table: [String: Info] = [
        "clock": Info(summary: "Clock and date. Analog or digital view.", symbol: "clock"),
        "world-clock": Info(summary: "Clocks for different cities.", symbol: "globe"),
        "stopwatch": Info(summary: "Click to start/stop, right-click to reset.", symbol: "stopwatch"),
        "focus": Info(summary: "Pomodoro-style focus and break timer; sends notifications when time expires.", symbol: "timer"),
        "countdown": Info(summary: "Countdown timer; sends a notification when it finishes.", symbol: "hourglass"),
        "alarm": Info(summary: "Notification at a specific time (optional daily repeat).", symbol: "alarm"),
        "time-progress": Info(summary: "Elapsed progress of the day, week, month, or year.", symbol: "calendar"),
        "hydration": Info(summary: "Countdown to next water reminder and daily goal. Click to add a glass.", symbol: "drop"),
        "reminders": Info(summary: "Daily reminders; shows a notification at the set time.", symbol: "bell"),
        "notes": Info(summary: "A sticky note that saves automatically.", symbol: "note.text"),
        "media": Info(summary: "Track info and controls for Music and Spotify.", symbol: "music.note"),
        "audio": Info(summary: "Switch the output device, adjust the volume and mute.", symbol: "speaker.wave.2"),
        "system": Info(summary: "Live CPU and memory usage.", symbol: "cpu"),
        "network": Info(summary: "Real-time download and upload speeds.", symbol: "network"),
        "status": Info(summary: "Battery, disk, memory, and CPU usage rings.", symbol: "gauge.with.dots.needle.33percent"),
        "recycle-bin": Info(summary: "The Trash on your dock. Drag files onto it to delete them, click to open.", symbol: "trash"),
        "battery-devices": Info(summary: "Battery levels of connected Bluetooth mice, keyboards and headphones.", symbol: "headphones"),
        "weather": Info(summary: "Current weather from Open-Meteo (no API key required).", symbol: "cloud.sun"),
        "ai-usage": Info(summary: "Claude Code usage: 5-hour and weekly limits (via 'claude -p /usage').", symbol: "sparkles"),
        "calendar": Info(summary: "Your next meeting from Google, Outlook or any iCal (.ics) calendar link, with a Join button for Teams, Meet and Zoom and a reminder before it starts.", symbol: "calendar"),
        "clipboard": Info(summary: "The last 25 things you copied, text and images. Click to copy again, pin the ones you need. Kept only in memory; password managers are never recorded.", symbol: "doc.on.clipboard"),
        "stack": Info(summary: "The newest files of your Downloads (or any) folder. Click to browse, drag files straight into other apps.", symbol: "folder"),
        "currency": Info(summary: "Daily exchange rates from the European Central Bank and crypto prices (BTC, ETH and more) with the change since the previous day and a two-week trend.", symbol: "dollarsign.arrow.circlepath"),
        "stocks": Info(summary: "Stock and index prices with the change since the previous close and a month's trend. Delayed prices from Stooq, no API key needed.", symbol: "chart.line.uptrend.xyaxis"),
        "todo": Info(summary: "Today's tasks: a simple list on this Mac or today's and overdue tasks from Todoist. Tick to complete, add new ones from the panel.", symbol: "checklist"),
        "screenshot": Info(summary: "One click takes a screenshot of an area or a window; the other button saves every screen to Pictures/Screenshots and copies it.", symbol: "camera.viewfinder"),
        "ping": Info(summary: "How long a reply from a server takes (1.1.1.1 or one you choose), every five seconds, with packet loss and a trend line.", symbol: "wifi"),
        "battery": Info(summary: "Battery level and charging state of this Mac. macOS only.", symbol: "battery.75percent"),
        "shortcut": Info(summary: "Runs one of your Shortcuts with a click. macOS only.", symbol: "square.2.layers.3d"),
        "airdrop": Info(summary: "Drop files onto it to send them with AirDrop. macOS only.", symbol: "dot.radiowaves.up.forward"),
    ]
}
