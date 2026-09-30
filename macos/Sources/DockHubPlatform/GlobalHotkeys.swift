import AppKit
import Carbon.HIToolbox
import DockHubCore

/// "DKHB": DockHub'in kisayol kimlikleri.
private let hotKeySignature: OSType = 0x444B_4842

/// Carbon kisayol olayi. C islev gostericisi oldugu icin dosya duzeyinde (yalitimsiz);
/// olaylar uygulamanin ana olay dongusunde geldigi icin ana aktore gecilir.
private func hotKeyPressed(_ next: EventHandlerCallRef?, _ event: EventRef?, _ userData: UnsafeMutableRawPointer?) -> OSStatus {
    var hotKey = EventHotKeyID()
    let status = GetEventParameter(event, EventParamName(kEventParamDirectObject), EventParamType(typeEventHotKeyID),
                                   nil, MemoryLayout<EventHotKeyID>.size, nil, &hotKey)
    guard status == noErr, hotKey.signature == hotKeySignature else { return OSStatus(eventNotHandledErr) }
    let id = hotKey.id
    MainActor.assumeIsolated { GlobalHotkeys.shared.fire(id) }
    return noErr
}

/// Genel kisayollar. Windows karsiligi: Services/HotkeyService.cs (RegisterHotKey).
/// Carbon RegisterEventHotKey Erisilebilirlik izni istemez ve tusu yalniz DockHub'a verir.
@MainActor
public final class GlobalHotkeys {
    public static let shared = GlobalHotkeys()

    private var refs: [EventHotKeyRef] = []
    private var actions: [UInt32: () -> Void] = [:]
    private var handler: EventHandlerRef?
    private var nextId: UInt32 = 1

    private init() {}

    /// Kaydeder; kisayol baska bir uygulamada kayitliysa false.
    @discardableResult
    public func register(_ gesture: HotkeyGesture, _ action: @escaping () -> Void) -> Bool {
        installHandler()
        var modifiers: UInt32 = 0
        if gesture.modifiers.contains(.command) { modifiers |= UInt32(cmdKey) }
        if gesture.modifiers.contains(.option) { modifiers |= UInt32(optionKey) }
        if gesture.modifiers.contains(.control) { modifiers |= UInt32(controlKey) }
        if gesture.modifiers.contains(.shift) { modifiers |= UInt32(shiftKey) }
        let id = nextId
        nextId += 1
        var ref: EventHotKeyRef?
        let status = RegisterEventHotKey(gesture.keyCode, modifiers, EventHotKeyID(signature: hotKeySignature, id: id),
                                         GetApplicationEventTarget(), 0, &ref)
        guard status == noErr, let ref else {
            Log.error("Kisayol kaydedilemedi: \(gesture.text) (\(status))")
            return false
        }
        refs.append(ref)
        actions[id] = action
        return true
    }

    public func unregisterAll() {
        refs.forEach { UnregisterEventHotKey($0) }
        refs.removeAll()
        actions.removeAll()
    }

    fileprivate func fire(_ id: UInt32) {
        actions[id]?()
    }

    private func installHandler() {
        guard handler == nil else { return }
        var spec = EventTypeSpec(eventClass: OSType(kEventClassKeyboard), eventKind: UInt32(kEventHotKeyPressed))
        InstallEventHandler(GetApplicationEventTarget(), hotKeyPressed, 1, &spec, nil, &handler)
    }
}
