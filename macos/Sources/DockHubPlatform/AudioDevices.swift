import CoreAudio
import Foundation
import DockHubCore

public struct AudioDevice: Sendable, Identifiable, Equatable {
    public let id: AudioDeviceID
    public let name: String
    public let isDefault: Bool
}

/// Ses cikis aygitlari ve ses seviyesi.
/// Windows karsiligi: Native/AudioInterop.cs (Core Audio COM: IMMDeviceEnumerator,
/// IMMDevice, IAudioEndpointVolume).
/// Eslestirme ag-audio-devices (mapped): CoreAudio AudioObjectGetPropertyData.
/// Izin gerektirmez. Varsayilan cikis aygitini DEGISTIRMEK macOS'ta desteklenen
/// bir islem; Windows'taki gibi belgelenmemis bir arayuze gerek yok.
public enum AudioDevices {

    private static func adres(_ selector: AudioObjectPropertySelector,
                             _ scope: AudioObjectPropertyScope = kAudioObjectPropertyScopeGlobal)
        -> AudioObjectPropertyAddress {
        AudioObjectPropertyAddress(mSelector: selector, mScope: scope,
                                   mElement: kAudioObjectPropertyElementMain)
    }

    public static func defaultOutputID() -> AudioDeviceID? {
        var addr = adres(kAudioHardwarePropertyDefaultOutputDevice)
        var id = AudioDeviceID(0)
        var size = UInt32(MemoryLayout<AudioDeviceID>.size)
        let st = AudioObjectGetPropertyData(AudioObjectID(kAudioObjectSystemObject),
                                            &addr, 0, nil, &size, &id)
        return st == noErr && id != 0 ? id : nil
    }

    public static func name(of id: AudioDeviceID) -> String? {
        var addr = adres(kAudioObjectPropertyName)
        var size = UInt32(MemoryLayout<CFString?>.size)
        var cf: CFString?
        let st = withUnsafeMutablePointer(to: &cf) {
            AudioObjectGetPropertyData(id, &addr, 0, nil, &size, $0)
        }
        return st == noErr ? (cf as String?) : nil
    }

    /// Yalniz CIKIS akisi olan aygitlar (mikrofonlar listeye girmesin).
    public static func outputDevices() -> [AudioDevice] {
        var addr = adres(kAudioHardwarePropertyDevices)
        var size = UInt32(0)
        guard AudioObjectGetPropertyDataSize(AudioObjectID(kAudioObjectSystemObject),
                                             &addr, 0, nil, &size) == noErr else { return [] }
        let adet = Int(size) / MemoryLayout<AudioDeviceID>.size
        var ids = [AudioDeviceID](repeating: 0, count: adet)
        guard AudioObjectGetPropertyData(AudioObjectID(kAudioObjectSystemObject),
                                         &addr, 0, nil, &size, &ids) == noErr else { return [] }

        let varsayilan = defaultOutputID()
        return ids.compactMap { id in
            guard cikisVarMi(id), let ad = name(of: id) else { return nil }
            return AudioDevice(id: id, name: ad, isDefault: id == varsayilan)
        }
    }

    private static func cikisVarMi(_ id: AudioDeviceID) -> Bool {
        var addr = adres(kAudioDevicePropertyStreamConfiguration, kAudioDevicePropertyScopeOutput)
        var size = UInt32(0)
        guard AudioObjectGetPropertyDataSize(id, &addr, 0, nil, &size) == noErr, size > 0
        else { return false }
        let buf = UnsafeMutableRawPointer.allocate(byteCount: Int(size),
                                                   alignment: MemoryLayout<AudioBufferList>.alignment)
        defer { buf.deallocate() }
        guard AudioObjectGetPropertyData(id, &addr, 0, nil, &size, buf) == noErr else { return false }
        let list = buf.assumingMemoryBound(to: AudioBufferList.self)
        return UnsafeMutableAudioBufferListPointer(list).contains { $0.mNumberChannels > 0 }
    }

    /// 0...1 arasi ses seviyesi; aygit desteklemiyorsa nil.
    public static func volume(of id: AudioDeviceID) -> Float? {
        var addr = adres(kAudioDevicePropertyVolumeScalar, kAudioDevicePropertyScopeOutput)
        var v: Float32 = 0
        var size = UInt32(MemoryLayout<Float32>.size)
        let st = AudioObjectGetPropertyData(id, &addr, 0, nil, &size, &v)
        return st == noErr ? v : nil
    }

    public static func setVolume(_ v: Float, of id: AudioDeviceID) {
        var addr = adres(kAudioDevicePropertyVolumeScalar, kAudioDevicePropertyScopeOutput)
        var deger = Float32(min(max(v, 0), 1))
        AudioObjectSetPropertyData(id, &addr, 0, nil,
                                   UInt32(MemoryLayout<Float32>.size), &deger)
    }

    public static func isMuted(_ id: AudioDeviceID) -> Bool? {
        var addr = adres(kAudioDevicePropertyMute, kAudioDevicePropertyScopeOutput)
        var m: UInt32 = 0
        var size = UInt32(MemoryLayout<UInt32>.size)
        let st = AudioObjectGetPropertyData(id, &addr, 0, nil, &size, &m)
        return st == noErr ? m != 0 : nil
    }

    public static func setMuted(_ muted: Bool, of id: AudioDeviceID) {
        var addr = adres(kAudioDevicePropertyMute, kAudioDevicePropertyScopeOutput)
        var m: UInt32 = muted ? 1 : 0
        AudioObjectSetPropertyData(id, &addr, 0, nil, UInt32(MemoryLayout<UInt32>.size), &m)
    }

    /// Varsayilan cikis aygitini degistirir (kulaklik/hoparlor gecisi).
    public static func setDefaultOutput(_ id: AudioDeviceID) {
        var addr = adres(kAudioHardwarePropertyDefaultOutputDevice)
        var deger = id
        AudioObjectSetPropertyData(AudioObjectID(kAudioObjectSystemObject), &addr, 0, nil,
                                   UInt32(MemoryLayout<AudioDeviceID>.size), &deger)
    }
}
