import AppKit
import SwiftUI
import DockHubCore

/// Windows karsiligi: SettingsWindow.xaml "Keyboard shortcuts" sayfasi.
/// Kisayollar config.json'da Windows bicimiyle durur ("Ctrl+Alt+D"; Win = Command).
struct KeyboardPage: View {
    @ObservedObject var store: SettingsStore

    var body: some View {
        Form {
            Section(L.t("DockHub shortcuts")) {
                ForEach(HotkeyActions.all) { action in
                    LabeledContent {
                        ShortcutRecorder(gesture: HotkeyActions.gesture(action.id, in: store.config),
                                         isCustom: store.config.hotkeys[action.id] != nil,
                                         onChange: { store.setHotkey(action.id, $0) },
                                         onReset: { store.resetHotkey(action.id) })
                    } label: {
                        Text(L.t(action.name))
                        if !action.description.isEmpty { RowNote(L.t(action.description)) }
                    }
                }
            }
            Section {
                RowNote(L.t("Click a shortcut and press the new keys. It needs Command, Option or Control. Delete turns it off, Escape cancels."))
            }
        }
        .formStyle(.grouped)
    }
}

/// Kaydedicinin tus dinleyicisi. Pencere icindeki tuslari (yerel izleyici) yakalar ve yutar.
@MainActor
private final class KeyCapture: ObservableObject {
    @Published private(set) var active = false
    private var monitor: Any?
    var onKey: ((UInt16, NSEvent.ModifierFlags) -> Void)?

    func start() {
        stop()
        active = true
        monitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { [weak self] event in
            let code = event.keyCode
            let flags = event.modifierFlags
            MainActor.assumeIsolated { self?.onKey?(code, flags) }
            return nil
        }
    }

    func stop() {
        if let monitor { NSEvent.removeMonitor(monitor) }
        monitor = nil
        active = false
    }
}

/// Kisayol kaydedici: tiklayinca bir sonraki tus birlesimini alir.
private struct ShortcutRecorder: View {
    let gesture: HotkeyGesture?
    let isCustom: Bool
    let onChange: (HotkeyGesture?) -> Void
    let onReset: () -> Void
    @StateObject private var capture = KeyCapture()

    var body: some View {
        HStack(spacing: 6) {
            Button {
                if capture.active {
                    capture.stop()
                } else {
                    capture.onKey = { code, flags in handle(code, flags) }
                    capture.start()
                }
            } label: {
                Text(capture.active ? L.t("Press a shortcut…") : (gesture?.symbols ?? L.t("Off")))
                    .monospacedDigit()
                    .frame(minWidth: 110)
            }
            if isCustom {
                Button(L.t("Reset"), action: onReset)
            }
        }
        .onDisappear { capture.stop() }
    }

    private func handle(_ code: UInt16, _ flags: NSEvent.ModifierFlags) {
        switch code {
        case 0x35: capture.stop()                           // Escape: vazgec
        case 0x33, 0x75: onChange(nil); capture.stop()      // Delete: kapat
        default:
            var mods: HotkeyGesture.Modifiers = []
            if flags.contains(.command) { mods.insert(.command) }
            if flags.contains(.option) { mods.insert(.option) }
            if flags.contains(.control) { mods.insert(.control) }
            if flags.contains(.shift) { mods.insert(.shift) }
            // Command, Option ya da Control olmadan kisayol olmaz; beklemeye devam.
            guard let g = HotkeyGesture.from(keyCode: code, modifiers: mods) else { return }
            onChange(g)
            capture.stop()
        }
    }
}
