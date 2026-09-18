# DockHub macOS — API Haritası

Windows sürümündeki her platform çağrısının macOS karşılığı. Kaynak: `src/CustomDock/`
altındaki gerçek kod ve [PARITE-ENVANTERI.md](PARITE-ENVANTERI.md).
Orvant kaydındaki karşılığı: `T2-API-HARITA` görevi, 23 `api_gap` nesnesi.

**Hedef sürüm:** macOS 27.0, karar `d-macos-hedefi-v2` (önceki `d-macos-hedefi` yanlış sürüm bilgisiyle verilmişti, superseded). Geriye dönük uyum yolu
yazılmaz, bu yüzden `SMAppService`, `CADisplayLink` ve güncel `NSVisualEffectView`
materyalleri koşulsuz kullanılabilir.

## Durum anahtarı

| Durum | Anlamı | Sayı |
|---|---|---|
| `mapped` | Genel API ile birebir karşılık var, ek izin gerekmez. | 12 |
| `partial` | Karşılık var ama izin, farklı etkileşim veya kapsam daralması gerekiyor. | 9 |
| `none` | Genel API ile karşılık yok. | 2 |

> Upstream `76e0cb9` (v0.3.0) üç yeni çağrı grubu getirdi: Core Audio, HID pil
> raporu ve Recycle Bin. Üçü de aşağıda ilgili bölümlerde haritalandı.

---

## `mapped` — birebir karşılığı olanlar (12)

### `ag-window-layer` — Pencere katmanı ve her zaman üstte
- **Windows:** WPF `Window.Topmost` + `WS_EX_TOOLWINDOW` + `SetWindowPos` (`Native/NativeMethods.cs`)
- **macOS:** `AppKit.NSPanel` (`styleMask: .nonactivatingPanel`, `isFloatingPanel = true`),
  `NSWindow.level` (`.statusBar` veya özel `CGWindowLevelForKey`),
  `NSWindow.collectionBehavior = [.canJoinAllSpaces, .stationary]`
- **Not:** `.nonactivatingPanel` dock'a tıklamanın öndeki uygulamayı arkaya
  atmamasını sağlar — Windows'ta `WS_EX_NOACTIVATE` ile yapılan şeyin karşılığı.

### `ag-app-icons` — Uygulama ikonu çözümleme
- **Windows:** `SHGetFileInfo` / `ExtractIconEx` (`Native/ShellIcons.cs`)
- **macOS:** `NSWorkspace.shared.icon(forFile:)`, `NSWorkspace.shared.icon(for: UTType)`
- **Not:** macOS ikonları vektör tabanlı; Windows'taki çoklu çözünürlük seçme derdi yok.

### `ag-app-launch` — Uygulama başlatma ve pencere eşleştirme
- **Windows:** `ShellExecuteEx` + AppUserModelId eşleştirme (`Services/AppLauncher.cs`)
- **macOS:** `NSWorkspace.shared.openApplication(at:configuration:completionHandler:)`,
  `NSWorkspace.OpenConfiguration` (yeni pencere için `createsNewApplicationInstance`),
  eşleştirme `NSRunningApplication.bundleIdentifier` üzerinden
- **Not:** `bundleIdentifier` AppUserModelId'den daha güvenilir ve kararlı.

### `ag-blur-effect` — Arka plan bulanıklaştırma
- **Windows:** `DwmEnableBlurBehindWindow` / `SetWindowCompositionAttribute` (`Native/WindowEffects.cs`)
- **macOS:** `NSVisualEffectView` — `material` (`.hudWindow`, `.sidebar`,
  `.underWindowBackground`, `.popover`), `blendingMode = .behindWindow`,
  `state = .active`
- **Not:** Windows'un Acrylic/Mica ayrımının karşılığı materyal seçimi. Tint için
  üstüne yarı saydam `NSView` katmanı.

### `ag-notifications` — Eylemli yerel bildirim
- **Windows:** `Microsoft.Toolkit.Uwp.Notifications` toast (`Services/NotificationService.cs`)
- **macOS:** `UserNotifications` framework — `UNUserNotificationCenter`,
  `UNMutableNotificationContent`, `UNNotificationAction` (eylem düğmeleri),
  `UNNotificationCategory`, `UNNotificationSound`, `interruptionLevel = .timeSensitive`
- **Not:** "Su içtim" ve "10 dk ertele" düğmeleri `UNNotificationAction` ile birebir.
  Alarmın kapatılana kadar kalması `.timeSensitive` ile sağlanır. Bildirim izni istenir.

### `ag-system-metrics` — Sistem ölçümleri
- **Windows:** `PerformanceCounter` / WMI (`Services/SystemMonitorService.cs`)
- **macOS:**
  - CPU: `host_statistics64` + `HOST_CPU_LOAD_INFO` (Darwin/Mach)
  - Bellek: `host_statistics64` + `HOST_VM_INFO64` (`vm_statistics64_data_t`)
  - Disk: `URL.resourceValues(forKeys: [.volumeAvailableCapacityForImportantUsageKey])`
  - Pil: IOKit `IOPSCopyPowerSourcesInfo` / `IOPSGetPowerSourceDescription`
  - Ağ hızı: `getifaddrs` + `if_data.ifi_ibytes/ifi_obytes` sayaç farkı
- **Not:** Tamamı genel API, izin gerektirmez. Windows'taki WMI gecikmesi yok.

### `ag-autostart` — Oturum açılışta başlatma
- **Windows:** `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` (`Core/StartupManager.cs`)
- **macOS:** `ServiceManagement.SMAppService.mainApp` — `register()`, `unregister()`,
  `status` (`.enabled` / `.notRegistered` / `.requiresApproval`)
- **Not:** Yönetici izni gerekmez; kullanıcı Sistem Ayarları > Genel > Giriş Öğeleri'nden
  kapatabilir. Windows'taki `InstallerChoice()` mantığı gerekmez.

### `ag-theme-accent` — Sistem teması ve vurgu rengi
- **Windows:** `HKCU ...Themes\Personalize` + `UISettings` (`Core/ThemeManager.cs`)
- **macOS:** `NSApp.effectiveAppearance` (`.darkAqua` / `.aqua`),
  `NSColor.controlAccentColor`; değişim takibi
  `DistributedNotificationCenter.default().addObserver(forName: "AppleInterfaceThemeChangedNotification")`
- **Not:** Sistem teması takibi için registry yoklama gerekmez, bildirim gelir.

### `ag-config-path` — Kullanıcı veri klasörü
- **Windows:** `Environment.GetFolderPath(SpecialFolder.ApplicationData)` (`Core/AppPaths.cs`)
- **macOS:** `FileManager.default.url(for: .applicationSupportDirectory, in: .userDomainMask)`
  → `~/Library/Application Support/DockHub/`
- **Not:** `DOCKHUB_HOME` ortam değişkeni geçersiz kılma davranışı aynen korunur.

### `ag-geolocation` — Konum servisi
- **Windows:** `Windows.Devices.Geolocation` (`Services/WeatherService.cs`)
- **macOS:** `CoreLocation.CLLocationManager` — `requestWhenInUseAuthorization()`,
  `CLLocationManagerDelegate`; `Info.plist` içinde
  `NSLocationWhenInUseUsageDescription` zorunlu
- **Not:** Konum reddedilirse şehir adıyla manuel giriş yolu Windows'ta da var, korunur.

### `ag-multi-monitor` — Monitör ve DPI bilgisi
- **Windows:** `MonitorFromWindow` + PerMonitorV2 DPI manifest (`Native/MonitorHelper.cs`)
- **macOS:** `NSScreen.screens`, `NSScreen.frame`, `NSScreen.visibleFrame`,
  `NSScreen.backingScaleFactor`, `NSScreen.localizedName`
- **Not:** DPI ölçekleme AppKit tarafından otomatik uygulanır. Windows'taki
  `app.manifest` PerMonitorV2 uğraşının macOS'ta karşılığı **gerekmiyor** — bu bir
  sadeleşme, kayıp değil.

### `ag-audio-devices` — Ses aygıtı sayımı ve ses denetimi
- **Windows:** Core Audio COM — `IMMDeviceEnumerator`, `IMMDevice`,
  `IAudioEndpointVolume` (`Native/AudioInterop.cs`)
- **macOS:** `CoreAudio` — `AudioObjectGetPropertyData` ile
  `kAudioHardwarePropertyDevices` (aygıt listesi),
  `kAudioHardwarePropertyDefaultOutputDevice` (**yazılabilir** — kulaklık/hoparlör
  geçişi), `kAudioDevicePropertyVolumeScalar`, `kAudioDevicePropertyMute`,
  `AudioObjectAddPropertyListener` (değişim bildirimi)
- **Not:** Genel C API, izin gerektirmez. Varsayılan çıkış aygıtını değiştirmek
  Windows'taki `IPolicyConfig` gibi belgelenmemiş bir arayüz gerektirmiyor —
  macOS'ta bu desteklenen bir işlem.

---

## `partial` — kısmi karşılığı olanlar (9)

### `ag-taskbar-takeover` — Kabuk görev çubuğunu devralma
- **Windows:** `Shell_TrayWnd` gizleme + taskman penceresi devri (ManagedShell, `Shell/TaskbarController.cs`)
- **macOS:** `NSApplication.presentationOptions = .hideDock` (yalnız kendi uygulamanız
  **aktifken** etkili), veya kullanıcı düzeyinde
  `defaults write com.apple.dock autohide -bool true` + `killall Dock`
- **Neden kısmi:** Sistem Dock'u devralınmaz, yalnız gizlenir. `presentationOptions`
  uygulama arkaya düşünce etkisini yitirir; `defaults` yolu Dock'u yeniden başlatır
  ve kullanıcının kalıcı ayarını değiştirir — çıkışta geri alınması gerekir
  (Windows'taki `session.json` + `--restore-taskbar` mantığının karşılığı).

### `ag-running-apps` — Açık pencere listesi ve pencere denetimi
- **Windows:** ManagedShell task service + `SetForegroundWindow` / `ShowWindow`
- **macOS:** `NSWorkspace.shared.runningApplications` (izinsiz, uygulama düzeyi);
  pencere düzeyi için `ApplicationServices` — `AXUIElementCreateApplication`,
  `kAXWindowsAttribute`, `kAXMinimizedAttribute`, `AXUIElementPerformAction(kAXRaiseAction)`
- **Neden kısmi:** Pencere denetimi **Erişilebilirlik izni** ister (Sistem Ayarları >
  Gizlilik ve Güvenlik > Erişilebilirlik). İzin verilmezse yalnız uygulama düzeyinde
  başlatma/öne getirme kalır. Windows'taki "ilerleme çubuğu" ve "dikkat istiyor"
  durumlarının karşılığı **hiç yok**.

### `ag-launcher` — Sistem uygulama başlatıcısını açma
- **Windows:** `IImmersiveLauncher` (Başlat menüsü), Win+X menüsü
- **macOS:** Launchpad `NSWorkspace.shared.openApplication` ile
  `/System/Applications/Launchpad.app`; Mission Control benzer şekilde
- **Neden kısmi:** Spotlight'ı programatik açmanın genel API'si yok;
  `CGEvent(keyboardEventSource:)` ile Cmd+Space simülasyonu Erişilebilirlik izni
  gerektirir. Win+X karşılığı yok — yerine kendi `NSMenu` menümüz.

### `ag-media-control` — Çalan medya bilgisi ve denetimi
- **Windows:** `Windows.Media.Control` (SMTC) — **her** uygulamanın medyası (`Services/MediaService.cs`)
- **macOS:**
  - `MediaPlayer.MPNowPlayingInfoCenter` → yalnız **kendi** uygulamanızın bilgisini *yayınlar*, başkasını okumaz
  - `MediaRemote` → **private framework**; App Store'a uygun değil, macOS sürümleri arasında kırılgan, yeni sürümlerde daha kısıtlı
  - Genel ve kalıcı yol: `ScriptingBridge` / `NSAppleScript` ile Music ve Spotify denetimi (**Otomasyon izni**)
- **Neden kısmi:** Kapsam Music + Spotify'a daralır. Tarayıcıda (YouTube) ve VLC'de
  çalan medya bu yolla **görülemez**. Bu, envanterde kabul edilmiş tek büyük
  işlevsel daralma.

### `ag-file-manager-verb` — Dosya yöneticisi bağlam menüsü
- **Windows:** HKCU shell verb kaydı (`Core/ExplorerPinMenu.cs`)
- **macOS:** `FinderSync` framework (`FIFinderSyncController`, ayrı app extension target)
  veya `Info.plist` içinde `NSServices` tanımı (`NSMessage`, `NSSendTypes`)
- **Neden kısmi:** `NSServices` komutu sağ tık → **Hizmetler** alt menüsünde görünür,
  üst düzeyde değil. `FinderSync` üst düzeye çıkar ama ayrı extension target ve
  klasör izleme kurulumu gerektirir. Windows 11'in "Show more options" durumuna benzer.

### `ag-fullscreen-detect` — Tam ekran uygulama algılama
- **Windows:** ManagedShell `FullScreenHelper`
- **macOS:** `NSWorkspace.shared.notificationCenter` (`activeSpaceDidChangeNotification`),
  `CGWindowListCopyWindowInfo` ile `kCGWindowBounds` / `kCGWindowLayer` karşılaştırması
- **Neden kısmi:** Doğrudan "bir uygulama tam ekranda mı" sorgusu yok; pencere
  listesinden çıkarım gerekir. `CGWindowListCopyWindowInfo` başlık ve sahip bilgisi
  için **Ekran Kaydı izni** isteyebilir (yalnız geometri için genelde gerekmez).

### `ag-packaging` — Kurulum paketi ve imzalama
- **Windows:** Inno Setup + per-user install, .NET runtime gömülü (`installer/DockHub.iss`)
- **macOS:** `.dmg` (sürükle-bırak) veya `.pkg` (`productbuild`); `codesign` ile
  Developer ID imzalama + `notarytool` ile notarization; `stapler` ile bilet ekleme
- **Neden kısmi:** Kendi makinenizde imzasız çalıştırmak mümkün. Başkasına dağıtım
  için **ücretli Apple Developer hesabı** şart, yoksa Gatekeeper açılışı engeller.
  Windows'ta böyle bir zorunluluk yoktu — bu yeni bir maliyet kalemi.

### `ag-trash` — Çöp kutusu sorgulama ve boşaltma
- **Windows:** `SHQueryRecycleBin` (öğe sayısı, boyut), `SHEmptyRecycleBin`
  (`Services/RecycleBinService.cs`)
- **macOS:**
  - Sorgula: `FileManager.contentsOfDirectory(at:)` ile `~/.Trash`
  - Öğe taşı: `FileManager.trashItem(at:resultingItemURL:)` — sürükle-bırak silmenin karşılığı
  - Aç: `NSWorkspace.shared.open(~/.Trash)`
  - **Boşalt: genel API yok.** `FileManager.removeItem` ile elle silinebilir
    (birimler arası `.Trash` klasörlerini kaçırır) veya AppleScript
    `tell application "Finder" to empty trash` (**Otomasyon izni**)
- **Neden kısmi:** Dört alt davranıştan üçü genel API ile karşılanıyor, boşaltma
  karşılanmıyor. Ayrıca `~/.Trash` dışındaki birim çöp kutuları
  (`/Volumes/X/.Trashes/uid`) ayrıca taranmalı.

### `ag-hid-battery` — HID aygıt pil seviyesi
- **Windows:** `hid.dll` (`HidD_GetHidGuid`, rapor okuma) + `setupapi.dll`
  (`SetupDiGetClassDevs`, `SetupDiEnumDeviceInterfaces`) (`Native/HidInterop.cs`)
- **macOS:** iki ayrı yol
  - Bluetooth çevre birimleri: IORegistry'de `AppleDeviceManagementHIDEventService`
    girdisinin `BatteryPercent` özelliği (`IORegistryEntrySearchCFProperty`) — izinsiz
  - Satıcıya özel USB dongle: `IOHIDManager` (`IOHIDManagerCreate`,
    `IOHIDDeviceGetProperty`, `IOHIDDeviceGetReport`) — **Girdi İzleme izni** ister
- **Neden kısmi:** Bluetooth tarafı izinsiz ve güvenilir; HyperX gibi satıcıya özel
  dongle'lar izin ve aygıt başına protokol çalışması gerektiriyor. Windows'ta da
  aygıt başına iş var, ama izin zorunluluğu macOS'a özgü.

---

## `none` — genel API ile karşılığı olmayanlar (2)

Bu ikisi `d-yok-cikarma` kararıyla parite tanımından çıkarılmıştır. Yine de neden
ve alternatif kayda geçiyor:

### `ag-reserved-space` — Ekran kenarı rezervasyonu
- **Windows:** `SHAppBarMessage` `ABM_NEW` / `ABM_SETPOS` — görünmez AppBar dock
  kalınlığını rezerve eder, maksimize pencereler altına kaymaz.
- **macOS karşılığı:** **yok.**
- **Neden:** `NSScreen.visibleFrame` yalnız sistem tarafından hesaplanır (menü çubuğu
  ve Dock'a göre). Üçüncü parti uygulamaya bu alanı daraltma API'si açılmamıştır;
  özel bir "AppBar" kavramı yoktur.
- **Önerilen alternatif:** Sistem Dock'unu gizleyip onun boşalttığı kenarı kullanmak.
  Bu **rezervasyon değil yer değiştirmedir**: maksimize pencereler dock'umuzun
  altına girebilir. Kabul edilen davranış budur.

### `ag-tray-host` — Bildirim alanı devralma
- **Windows:** `Shell_TrayWnd` tray servisi — **başka uygulamaların** tepsi ikonları
  dock'a kaydolur, tık/sağ tık/hover onlara iletilir (ManagedShell NotifyIcon protokolü).
- **macOS karşılığı:** **yok.**
- **Neden:** `NSStatusBar.system.statusItem(withLength:)` yalnız **çağıran uygulamanın
  kendi** öğesini oluşturur. Diğer uygulamaların menü çubuğu öğelerini listelemek,
  taşımak veya onlara olay iletmek için genel API yoktur; menü çubuğu `WindowServer`
  tarafından korunur.
- **Önerilen alternatif:** DockHub yalnız kendi `NSStatusItem`'ını sunar. Diğer
  uygulamaların öğeleri menü çubuğunda kendi yerlerinde kalır. "Gizli ikonlar taşma
  menüsü" ve "hangi ikonlar her zaman görünsün" özellikleri düşer.

---

## Sonuç ve T3'e devreden kararlar

| Konu | Karar gerektiren |
|---|---|
| Erişilebilirlik izni | Uygulama ilk açılışta mı isteyecek, ilgili özellik kullanılınca mı? |
| Otomasyon izni | Media widget'ı yalnız Music/Spotify ile mi sunulacak, hiç sunulmayacak mı? |
| Dock gizleme | Çıkışta kullanıcının Dock ayarı geri yüklenecek (`session.json` karşılığı) — zorunlu. |
| Dağıtım | Apple Developer hesabı alınacak mı? Alınmazsa yalnız yerel kullanım. |

Bu dört konu `T3-MIMARI` görevinin girdisidir.
