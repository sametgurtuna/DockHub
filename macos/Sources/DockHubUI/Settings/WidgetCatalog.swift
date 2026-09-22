import Foundation

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
    static let categoryOrder = ["Clocks", "Reminders", "Sticky notes", "Media",
                                "System", "Weather", "AI", "Extras"]

    static func info(_ id: String) -> Info {
        table[id] ?? Info(summary: "", symbol: "square.dashed")
    }

    private static let table: [String: Info] = [
        "clock": Info(summary: "Clock and date. Analog or digital view.", symbol: "clock"),
        "world-clock": Info(summary: "Clocks for different cities.", symbol: "globe"),
        "stopwatch": Info(summary: "Click to start or stop, right-click to reset.", symbol: "stopwatch"),
        "focus": Info(summary: "Pomodoro-style focus and break timer; sends a notification when time is up.", symbol: "timer"),
        "countdown": Info(summary: "Countdown timer; sends a notification when it finishes.", symbol: "hourglass"),
        "alarm": Info(summary: "Notification at a specific time, optionally every day.", symbol: "alarm"),
        "time-progress": Info(summary: "Elapsed progress of the day, week, month or year.", symbol: "calendar"),
        "hydration": Info(summary: "Countdown to the next glass of water and a daily goal. Click to add a glass.", symbol: "drop"),
        "reminders": Info(summary: "Daily reminders; shows a notification at the set time.", symbol: "bell"),
        "notes": Info(summary: "A sticky note that saves automatically.", symbol: "note.text"),
        "media": Info(summary: "Track info and controls for Music and Spotify.", symbol: "music.note"),
        "audio": Info(summary: "Switch the output device, adjust the volume and mute.", symbol: "speaker.wave.2"),
        "system": Info(summary: "Live CPU and memory usage.", symbol: "cpu"),
        "network": Info(summary: "Real-time download and upload speeds.", symbol: "network"),
        "status": Info(summary: "Battery, disk, memory and CPU usage rings.", symbol: "gauge.with.dots.needle.33percent"),
        "recycle-bin": Info(summary: "The Trash on your dock. Drag files onto it to delete them, click to open.", symbol: "trash"),
        "battery-devices": Info(summary: "Battery levels of connected Bluetooth mice, keyboards and headphones.", symbol: "headphones"),
        "weather": Info(summary: "Current weather from Open-Meteo (no API key required).", symbol: "cloud.sun"),
        "ai-usage": Info(summary: "Claude Code usage: 5-hour and weekly limits (via 'claude -p /usage').", symbol: "sparkles"),
        "battery": Info(summary: "Battery level and charging state of this Mac. macOS only.", symbol: "battery.75percent"),
        "shortcut": Info(summary: "Runs one of your Shortcuts with a click. macOS only.", symbol: "square.2.layers.3d"),
        "airdrop": Info(summary: "Drop files onto it to send them with AirDrop. macOS only.", symbol: "dot.radiowaves.up.forward"),
    ]
}
