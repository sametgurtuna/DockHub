# DockHub for macOS

A native macOS dock engineered in Swift 6, AppKit and SwiftUI for macOS 15 (Sequoia).

DockHub brings Windows-parity features to macOS: floating glass dock, 30+ native and web widgets, window and app management, profiles, undo support, and 4 languages (English, Turkish, German, Spanish).

## Highlights

- **Native performance:** Written in pure Swift 6 with strict concurrency (`SWIFT_STRICT_CONCURRENCY=complete`), AppKit panels, and SwiftUI views.
- **Dock replacement or companion:** Can hide the system Dock while running and restore your original setting on exit (*Settings › General*), or run alongside it.
- **Widget parity:** Full parity with Windows DockHub:
  - 8 new widgets in 1.0: Calendar (iCal), Clipboard History, Downloads Stack, Exchange & Crypto, Stocks (Stooq), Todo (local + Todoist), Screenshot, Ping.
  - Plus system monitor, weather, clocks, timers, sticky notes, media player, and macOS-specific battery, shortcuts, and AirDrop widgets.
- **Web Widgets SDK 1.0:** Run HTML/JavaScript widgets in sandboxed `WKWebView` with `window.dockhub` API (storage, notifications, system theme, native HTTP proxy, context menus).
- **Single configuration:** Reads and writes the same cross-platform `config.json` schema as Windows, preserving platform-specific fields.
- **Localization:** Shared translations for English, Turkish, German, and Spanish.

## Requirements

- macOS 15.0 (Sequoia) or newer
- Apple Silicon (M1/M2/M3/M4) or Intel Mac

## Installation

1. Download `DockHub.dmg` from the [Releases](https://github.com/sametgurtuna/DockHub/releases/latest) page.
2. Open the `.dmg` file and drag `DockHub.app` to your `/Applications` folder.
3. Launch DockHub.

## Building from source

Prerequisites: Xcode 16+ or Command Line Tools on macOS 15+.

```bash
cd macos
# Build debug binary
swift build

# Run unit tests
swift test

# Package .app bundle
./Scripts/bundle.sh

# Package distributable DMG
./Scripts/package-dmg.sh release
```

## Structure

- `Sources/DockHubCore`: Business logic, configuration models, widget registry, web widget catalog, update service, localizer.
- `Sources/DockHubPlatform`: AppKit windowing, macOS Dock lifecycle, notifications (`UserNotifications`), appearance, global hotkeys (Carbon).
- `Sources/DockHubUI`: SwiftUI dock view, widget renderers, settings pages, and WKWebView web widget host.
- `Sources/DockHubApp`: Main entry point and lifecycle delegate.
- `Tests/DockHubCoreTests`: Parity tests, config schema verification against Windows, web widget tests, update tests.
- `Scripts`: Build and packaging utilities (`bundle.sh`, `package-dmg.sh`, `check-translations.py`, `compare-config-schema.py`).
