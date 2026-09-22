# DockHub macOS — Parite Envanteri

Bu belge, DockHub'ın Windows sürümündeki (v2.2.0 + upstream v0.3.0 widget'ları)
her yetenek grubunu macOS'taki
karşılığına göre sınıflandırır. Kaynak: `README.md` ve `src/CustomDock/` altındaki
gerçek kod. Orvant kaydındaki karşılığı: `T1-ENVANTER` görevi, 32 `parity_call` nesnesi.

**Upstream v0.6.1 ile yeniden incelendi.** v0.4.0–v0.6.1 arasında gelen 9 yeni
yetenek grubu ayrı belgede: [UPSTREAM-v0.6.md](UPSTREAM-v0.6.md) (`T15-UPSTREAM-ENVANTER`).
Bu belgedeki 32 kararın hiçbiri değişmedi. Kaynak dosyalardaki farkların çoğu
Türkçeden İngilizceye çeviri. Davranış değişiklikleri ilgili bölümlerin altında
**v0.6.1 notu** olarak işaretlendi.

## Ölçütler

Sınıflandırma iki kayıtlı ölçüte dayanır:

- **`pc-kapsam-v1`** — Parite, Windows envanterindeki her yetenek grubu için ya çalışan
  bir macOS karşılığı ya da gerekçesi kayıtlı bir "macOS'ta karşılığı yok" kararı
  bulunması demektir. Bu belge o kararların tamamını üretir.
- **`pc-davranis-v1`** — Bir macOS yeteneği, Windows karşılığının **gözlenebilir
  davranışını** (aynı girdi → aynı sonuç) eşitlediğinde karşılanmış sayılır; görsel
  birebir aynılık şart değildir. Aşağıdaki `var` kararları bu ölçüte göre verilmiştir:
  aynı API'nin bulunması değil, aynı davranışın üretilebilmesi arandı.

## Sınıflandırma anahtarı

| Karar | Anlamı |
|---|---|
| `var` | macOS'ta genel (public) API ile aynı gözlenebilir davranış üretilebilir. |
| `uyarla` | Davranış kısmen üretilebilir; izin, farklı etkileşim veya kapsam daralması gerekir. |
| `yok` | macOS'ta genel API ile karşılığı üretilemez; parite tanımından çıkarılması gerekir. |

## Özet

| Karar | Sayı | Oran |
|---|---|---|
| `var` | 19 | %59 |
| `uyarla` | 11 | %34 |
| `yok` | 2 | %6 |
| **Toplam** | **32** | |

> Upstream `76e0cb9` (v0.3.0) üç yeni widget getirdi: **Ses aygıtı**, **Çöp kutusu**,
> **Aygıt pilleri**. Envanter 29'dan 32 yetenek grubuna çıktı; üçü de aşağıda
> bölüm 3'ün sonunda, gerçek interop kodu okunarak sınıflandırıldı.

---

## 1. Görev çubuğu grubu (9 yetenek)

### `wf-taskbar-replace` — Görev çubuğu değiştirme → **uyarla**
Windows'ta `Shell_TrayWnd` gizlenir ve taskman penceresi Explorer'a geri verilir
(`Shell/TaskbarController.cs`, `Shell/ShellHost.cs`). macOS'ta sistem Dock'unu
devralmanın genel API'si yoktur. Yapılabilen: Dock'u otomatik gizlemeye almak
(`defaults write com.apple.dock autohide -bool true` + `killall Dock`) veya yalnız
kendi uygulaman öndeyken `NSApplication.presentationOptions = .hideDock`.
**Kapsam daralması:** Dock "devralınmaz", yanına/yerine kendi panelimiz konur.
Windows sürümünün güvenlik ağı olan "çıkışta görev çubuğu geri gelir" davranışı
macOS'ta "çıkışta Dock ayarı eski haline döner" olarak karşılanabilir.

### `wf-reserved-space` — Ekran alanı rezervasyonu → **yok**
Windows'ta görünmez bir AppBar (`SHAppBarMessage ABM_SETPOS`) dock kalınlığını
rezerve eder, maksimize pencereler altına kaymaz. macOS'ta `NSScreen.visibleFrame`
tamamen sistem tarafından yönetilir; üçüncü parti uygulamaya alan rezerve etme
API'si **yoktur**. Tek dolaylı yol, sistem Dock'unu gizleyip onun bıraktığı alanı
kullanmaktır — bu rezervasyon değil, yer değiştirmedir. Pencereler dock'umuzun
altına girebilir.

### `wf-start-menu` — Başlat düğmesi → **uyarla**
Windows'ta `IImmersiveLauncher` ile gerçek Başlat menüsü açılır. macOS'ta Launchpad
`open -a Launchpad` ile açılabilir; Spotlight'ı programatik açmanın genel API'si
yoktur, `CGEvent` ile Cmd+Space simülasyonu **Erişilebilirlik izni** gerektirir.
Win+X eşdeğeri yoktur; yerine kendi `NSMenu` menümüz konur.

### `wf-running-apps` — Çalışan uygulamalar → **uyarla**
`NSWorkspace.shared.runningApplications` uygulama listesini izinsiz verir.
Pencere düzeyinde liste, öne getirme ve küçültme için `AXUIElement`
(`kAXWindowsAttribute`) gerekir → **Erişilebilirlik izni** şart.
Windows'taki "ilerleme çubuğu" ve "dikkat istiyor" durumlarının macOS karşılığı
yoktur; o iki alt davranış düşer.

**v0.6.1 notu:** Rozet sayıları, canlı pencere önizlemesi ve Jump List ayrı
yetenek grubu olarak [UPSTREAM-v0.6.md](UPSTREAM-v0.6.md) içinde. Eşleme kodu
`Dock/DockWindow.Items.cs` dosyasına taşındı. Birden çok ekranda her dock yalnız
kendi ekranındaki pencereleri listeleyebiliyor (`wf-multi-display`). Kalan
davranış aynı, karar değişmez.

### `wf-pinned-apps` — Sabitlenmiş uygulamalar → **var**
`NSWorkspace.shared.icon(forFile:)` ikonu, `NSWorkspace.shared.openApplication(at:configuration:)`
başlatmayı verir. `runningApplications` ile eşleştirme `bundleIdentifier` üzerinden
Windows'taki AppUserModelId eşleştirmesinden daha güvenilirdir.
Kaynak: `Dock/AppButton.cs`, `Core/AppConfig.cs`.

### `wf-tray` — Sistem tepsisi devralma → **yok**
Windows'ta `Shell_TrayWnd` tray servisi devralınır ve **başka uygulamaların** tepsi
ikonları dock'a kaydolur (ManagedShell). macOS'ta `NSStatusBar` yalnız **kendi**
status item'ını eklemeye izin verir; diğer uygulamaların menü çubuğu öğelerini
devralmak, listelemek veya onlara tıklama iletmek mümkün değildir. Bu yetenek
macOS'ta karşılıksızdır.

### `wf-clock-notify` — Saat ve bildirim merkezi → **uyarla**
Saat gösterimi ve biçim seçenekleri sorunsuz (`var` düzeyinde). Ancak tıklamayla
**Bildirim Merkezi'ni açmanın** genel API'si yoktur (Control Center private).
Karşılık: kendi bildirim/ajanda panelimizi açmak. Tarih-saat ayarlarına gitmek
`x-apple.systempreferences:` URL şemasıyla mümkündür.

### `wf-explorer-pin` — Dosya yöneticisinden sabitleme → **uyarla**
Windows'ta HKCU shell verb kaydıyla Explorer bağlam menüsüne komut eklenir
(`Core/ExplorerPinMenu.cs`). macOS'ta iki genel yol var: **Finder Sync Extension**
(`FinderSync` framework) veya `Info.plist` içinde `NSServices` tanımı — ikincisi
komutu sağ tık → **Hizmetler** alt menüsünde gösterir. Üst düzey bağlam menüsüne
doğrudan komut eklenemez, bu yüzden `uyarla`.

### `wf-search-taskview` — Arama ve Görev Görünümü → **uyarla**
Mission Control `open -a "Mission Control"` ile açılır (Task View karşılığı).
Spotlight için genel API yok; tuş simülasyonu Erişilebilirlik izni gerektirir.

---

## 2. Dock grubu (6 yetenek)

### `wf-dock-appearance` — Dock görünümü → **var**
`NSVisualEffectView` (`.hudWindow` / `.underWindowBackground` materyalleri) Windows'un
Acrylic/blur karşılığını verir. Tema: `NSApp.effectiveAppearance`; değişim takibi
`AppleInterfaceThemeChangedNotification` (DistributedNotificationCenter).
Vurgu rengi: `NSColor.controlAccentColor`. Tam parite.

### `wf-dock-layout` — Boyut, şekil, konum → **var**
`NSPanel` (`.nonactivatingPanel`, `isFloatingPanel`), `window.level`,
`NSScreen.frame` ile alt/üst/sol/sağ kenar yerleşimi; floating/attached ayrımı
köşe yarıçapı ve kenar boşluğu meselesidir. Kaynak: `Dock/DockWindow.xaml.cs`.

**v0.6.1 notu:** Yerleşim kodu `Dock/DockWindow.Positioning.cs` dosyasına taşındı.
v0.6.0'da küçük ve büyük boyut hesabı düzeltildi. Eskiden bütün çubuk 56'nın
0,86 ve 1,18 katıydı (48/56/66). Şimdi yalnız içerik ölçekleniyor (40/46/54), kenar
boşlukları (2×5) sabit kalıyor, yani kalınlık **50/56/64**. Ekran başına boyut ayrı
grup (`wf-multi-display`).

### `wf-dock-behavior` — Dock davranışı → **var**
Otomatik gizleme: `NSEvent.addGlobalMonitorForEvents(matching: .mouseMoved)` veya
`NSTrackingArea`. Tam ekran algılama: `NSWorkspace` bildirimleri ve
`CGWindowListCopyWindowInfo`. Çok monitör: `NSScreen.screens`. DPI: `backingScaleFactor`
otomatik uygulanır — Windows'taki PerMonitorV2 uğraşı macOS'ta gerekmez.

**v0.6.1 notu:** Otomatik gizleme ve tam ekran kodu `Dock/DockWindow.Interaction.cs`
dosyasına taşındı. Birden çok ekranda dock artık ayrı grup (`wf-multi-display`),
global kısayol da öyle (`wf-global-hotkey`).

### `wf-dock-dnd` — Sürükle-bırak düzenleme → **var**
`NSDraggingSource` / `NSDraggingDestination` ve `NSPasteboard` (`.fileURL` tipi).
Finder'dan `.app` bırakma Windows'taki `.exe`/`.lnk` bırakmanın karşılığıdır.

**v0.6.1 notu:** Bırakma konumu hesabı `Dock/DockWindow.Items.cs` dosyasına taşındı.
Öğeyi başka bir öğenin üzerine bırakmak artık klasör oluşturuyor (`wf-dock-groups`),
dosyayı uygulama düğmesine bırakmak o uygulamayla açıyor (`wf-drop-on-app`).

### `wf-dock-motion` — Kare senkron animasyon → **var**
`CADisplayLink` (macOS 14+) veya `CVDisplayLink`, `NSAnimationContext`, Core Animation.
Yüksek yenileme hızı (ProMotion 120 Hz) desteklenir. Windows'taki
`CompositionTarget.Rendering` yaklaşımının doğrudan karşılığı.

**v0.6.1 notu:** Açılır pencerelere genie, zoom ve küçülme, klasörlere kademeli
yelpaze animasyonu geldi (`Dock/GenieEffectHelper.cs`). Genie, anlık görüntünün
dokulu 3B mesh ile bükülmesi. macOS'ta mesh bükme API'si (`CAMeshTransform`)
private, genel yol Metal ya da şerit tabanlı yaklaşık çözüm. Ayrıntı
[UPSTREAM-v0.6.md](UPSTREAM-v0.6.md) bölüm 2'de. Karar `var` kalır.

### `wf-dock-menu` — Dock bağlam menüsü → **var**
`NSMenu` / `NSMenuItem`. "Görev Yöneticisi" öğesi macOS'ta Activity Monitor'ü açar
(`open -a "Activity Monitor"`); "Görev çubuğunu gizle" öğesi Dock ayarına dönüşür.

**v0.6.1 notu:** Menüye "Create group" eklendi (`wf-dock-groups` ile birlikte gelir).

---

## 3. Widget grubu (10 yetenek)

### `wf-widget-host` — Widget mimarisi → **var**
Çok örnekli widget, örnek başına ayar, layout seçimi ve panel açma tamamen kendi
mimarimiz; platform API'si gerektirmez. `Widgets/WidgetRegistry.cs` birebir
SwiftUI karşılığına çevrilebilir. Dikey dock'ta kompakt karo da salt düzen işi.

**v0.6.1 notu:** Kayıt defterindeki fark yalnız çeviri. Widget ve varyant
kimlikleri aynı kaldı, yalnız görünen adlar İngilizceye geçti. Widget'a
tıklayınca açılan paneller (saat, dünya saati, medya) ve widget başına sağ tık
menüsü (eylemler, layout, ayarlar) eklendi. Widget'ı sağ uca sabitleme ayrı grup
(`wf-widget-pin-end`).

### `wf-widgets-clocks` — Saat widget'ları → **var**
`Date`, `Calendar`, `TimeZone`, `DateFormatter`; analog kadran SwiftUI `Canvas` veya
Core Graphics. Dünya saati için `TimeZone.knownTimeZoneIdentifiers`.

### `wf-widgets-timers` — Zamanlayıcı widget'ları → **var**
`Timer` / `DispatchSourceTimer`; bitişte bildirim `UNUserNotificationCenter`.
Stopwatch, Focus (pomodoro), Countdown, Alarm, Time progress — hepsi saf mantık.

### `wf-widgets-reminders` — Hatırlatıcı ve not widget'ları → **var**
Hydration, Reminders, Sticky note: `Codable` ile JSON kalıcılığı ve bildirim eylemleri.
Windows'taki "Su içtim" / "10 dk ertele" düğmelerinin karşılığı `UNNotificationAction`.

### `wf-widget-media` — Now playing widget'ı → **uyarla**
En zorlu widget. Windows'ta `Windows.Media.Control` (SMTC) **her** uygulamanın çalan
medyasını verir (`Services/MediaService.cs`). macOS'ta:
- `MPNowPlayingInfoCenter` yalnız **kendi** uygulamanın bilgisini **yayınlar**, başkasını okumaz.
- Başka uygulamaların medyasını okumak `MediaRemote` **private framework** gerektirir:
  App Store'a uygun değil, macOS sürümleri arasında kırılgan, yeni sürümlerde daha da kısıtlı.
- Genel ve kalıcı yol: **AppleScript/Apple Events** ile Music ve Spotify denetimi
  (Otomasyon izni gerekir). Tarayıcıda çalan medya bu yolla **kapsanmaz**.

**Sonuç:** kapsam Music + Spotify'a daralır; "tarayıcı, VLC ve dahası" paritesi düşer.

**v0.6.1 notu:** Medya paneline ileri/geri sarma eklendi (`MediaService.SeekAsync`,
SMTC `TryChangePlaybackPositionAsync`). Music ve Spotify'da `player position`
AppleScript özelliği yazılabilir, yani sarma Music ve Spotify kapsamında
karşılanabilir. Albüm kapağı 128'den 512 piksele büyütüldü.

### `wf-widgets-system` — Sistem widget'ları → **var**
- CPU: `host_statistics64` / `HOST_CPU_LOAD_INFO`
- Bellek: `vm_statistics64`
- Disk: `URLResourceKey.volumeAvailableCapacity`
- Pil: IOKit `IOPSCopyPowerSourcesInfo`
- Ağ hızı: `getifaddrs` + `if_data` sayaç farkı
Hepsi genel API, izin gerektirmez. Windows'un `PerformanceCounter` yaklaşımının karşılığı.

### `wf-widget-weather` — Hava durumu widget'ı → **var**
Open-Meteo HTTP API platformdan bağımsız (`URLSession`). Konum:
`CoreLocation` / `CLLocationManager` (konum izni). Windows'taki
`Windows.Devices.Geolocation` karşılığı birebir.


### `wf-widget-audio` — Ses aygıtı widget'ı → **var**
Windows'ta Core Audio COM: `IMMDeviceEnumerator` ile aygıt sayımı, `IMMDevice` ile
varsayılan aygıt, `IAudioEndpointVolume` ile ses ve sessize alma
(`Native/AudioInterop.cs`). macOS'ta **CoreAudio** birebir karşılık veriyor:
`AudioObjectGetPropertyData` ile `kAudioHardwarePropertyDevices` aygıt listesi,
`kAudioHardwarePropertyDefaultOutputDevice` **yazılabilir** (kulaklık/hoparlör
geçişi budur), `kAudioDevicePropertyVolumeScalar` ve `kAudioDevicePropertyMute`.
Hepsi genel C API, izin gerektirmez. Kompakt ve çubuklu layout salt UI işi.

### `wf-widget-recyclebin` — Çöp kutusu widget'ı → **uyarla**
Windows'ta `SHQueryRecycleBin` (öğe sayısı ve boyut) ve `SHEmptyRecycleBin`
(`Services/RecycleBinService.cs`). macOS'ta parça parça:

| Alt davranış | macOS karşılığı | Durum |
|---|---|---|
| Çöp kutusunu sorgula | `FileManager.contentsOfDirectory(at: ~/.Trash)` + boyut | var |
| Sürükle-bırak ile sil | `FileManager.trashItem(at:resultingItemURL:)` | var |
| Tıklayınca aç | `NSWorkspace.shared.open(~/.Trash)` | var |
| **Sağ tıkla boşalt** | **genel API yok** | eksik |

Boşaltmanın genel karşılığı yok. İki yol var: `FileManager.removeItem` ile içeriği
elle silmek (Finder'ın semantiğini taklit eder ama birimler arası `.Trash`
klasörlerini kaçırır) veya AppleScript `tell application "Finder" to empty trash`
(Otomasyon izni ister). Bu yüzden `uyarla`.

Not: Windows sürümündeki widget açıklaması bu özelliği zaten **"macOS tarzı çöp
kutusu"** diye tanımlıyor — yani davranış macOS'tan ilham almış, geri taşınması doğal.

**v0.6.1 notu:** Boşaltma artık onay soruyor (`SHEmptyRecycleBin` bayrağı
`SHERB_NOCONFIRMATION` yerine `0`). macOS'ta boşaltma zaten Finder'a bırakıldığı
için etkisi yok.

### `wf-widget-devicebattery` — Aygıt pilleri widget'ı → **uyarla**
Windows'ta `hid.dll` (`HidD_GetHidGuid`, rapor okuma) ve `setupapi.dll`
(`SetupDiGetClassDevs`, `SetupDiEnumDeviceInterfaces`) ile HID aygıtları sayılıp
pil raporu okunuyor (`Native/HidInterop.cs`). macOS'ta iki ayrı yol gerekiyor:

- **Bluetooth çevre birimleri** (fare, klavye, kulaklık): IORegistry üzerinden
  `AppleDeviceManagementHIDEventService` girdisindeki `BatteryPercent` özelliği
  okunur. İzin gerektirmez, güvenilir çalışır.
- **Satıcıya özel USB dongle'lar** (HyperX Cloud II Wireless gibi): `IOHIDManager`
  ile aygıt açılıp satıcıya özel HID raporu okunur. Bu **Girdi İzleme (Input
  Monitoring) izni** ister ve her aygıt için ayrı protokol çalışması gerektirir —
  Windows tarafında da öyle, ama izin zorunluluğu macOS'a özgü.

Bluetooth tarafı `var` düzeyinde, dongle tarafı ek izin ve aygıt başına iş
gerektirdiği için grup bütünüyle `uyarla`.

---

## 4. Sistem grubu (4 yetenek)

### `wf-notifications` — Eylemli bildirimler → **var**
`UNUserNotificationCenter` + `UNNotificationAction` eylem düğmelerini,
`UNNotificationSound` sesi verir. Alarmın "kapatılana kadar kalması" için
`.timeSensitive` interruption level kullanılır. Windows toast paritesi sağlanır.

### `wf-settings-ui` — Ayarlar penceresi ve widget galerisi → **var**
SwiftUI `Settings` scene veya ayrı `NSWindow`; önizlemeli galeri salt UI işi.
Windows'taki Mica pencere efektinin karşılığı `NSVisualEffectView`.

**v0.6.1 notu:** Sayfalar: General, Taskbar, Appearance, Dock items, Widget gallery,
About. General sayfasına ana ekran seçimi, "tüm ekranlarda göster", "çalışan
uygulamalar kendi ekranında" ve ekran başına boyut satırları eklendi. Dock items
ve galeri ayrı dosyalara taşındı (`Settings/SettingsWindow.Items.cs`,
`SettingsWindow.Gallery.cs`). Widget ayar formları
`Settings/WidgetSettingsTemplates.xaml` içinde. Global kısayol için ayar satırı
**yok**, README'nin aksine kısayol kodda sabit.

### `wf-config` — Yapılandırma ve veri deposu → **var**
`%AppData%\DockHub` → `~/Library/Application Support/DockHub/`.
`config.json` ve `session.json` aynı adlarla kullanılabilir; `Codable` ile
Windows şemasının alan adları korunabilir (`Core/AppConfig.cs` referans şema).
`DOCKHUB_HOME` ortam değişkeni taşınabilir kullanım için aynen desteklenir.

**v0.6.1 notu:** Şemaya yeni alanlar geldi: `showOnAllDisplays`,
`runningAppsOnOwnDisplay`, `displaySizes`, ayrıca `DockItem` içinde `Group` türü,
`pinnedEnd`, `groupName`, `groupAccent`, `children`. Tam liste ve yazma kuralları
[UPSTREAM-v0.6.md](UPSTREAM-v0.6.md) bölüm 3'te. Karar `var` kalır. Swift modeli
güncellenene kadar uyum fiilen kırık (`T4-ISKELET`).

### `wf-single-instance` — Tek örnek ve tema takibi → **var**
Tek örnek: `Info.plist` içinde `LSMultipleInstancesProhibited` veya
`NSRunningApplication` kontrolü. Tema/vurgu rengi değişimi izleme:
DistributedNotificationCenter bildirimleri. `Core/SingleInstance.cs` karşılığı.

---

## 5. Paketleme grubu (3 yetenek)

### `wf-startup` — Oturum açılışta başlatma → **var**
Windows'ta `HKCU\...\Run` (`Core/StartupManager.cs`). macOS'ta
`SMAppService.loginItem(identifier:)` (macOS 13+) — kullanıcı onayıyla açılıp
kapatılabilir, yönetici izni gerekmez. Eski yol LaunchAgent plist'i. Tam parite.

### `wf-cli` — Komut satırı arayüzü → **uyarla**
`--exit`, `--pin <file>`, `--restore-taskbar`, `--startup` argümanları.
macOS'ta `.app` paketi doğrudan terminalden argümanla çağrılmaz; yollar:
`open -a DockHub --args --exit`, bir URL şeması (`dockhub://exit`), veya paket içine
ayrı bir CLI ikilisi koyup `/usr/local/bin`'e symlink. Davranış korunur ama
çağrı biçimi değişir. `--restore-taskbar` yerine `--restore-dock` anlamlı olur.

### `wf-installer` — Kurulum paketi → **uyarla**
Inno Setup yerine `.dmg` (sürükle-bırak) veya `.pkg` (`productbuild`).
Kritik fark: macOS'ta dağıtım için **Developer ID imzalama + notarization**
(`codesign` + `notarytool`) gerekir, yoksa Gatekeeper açılışı engeller —
Windows'ta böyle bir zorunluluk yok. İmzasız yerel kullanım mümkün ama
başkasına dağıtım için Apple Developer hesabı (yıllık ücretli) şart.

---

## macOS'ta karşılığı olmayan yetenekler (`yok`)

`pc-kapsam-v1` gereği bu iki yetenek **parite tanımından çıkarıldı**. Karar
kullanıcı tarafından verildi ve `d-yok-cikarma` olarak kayıtlı:

| Yetenek | Neden karşılıksız |
|---|---|
| `wf-reserved-space` | macOS'ta `NSScreen.visibleFrame`'i sistem yönetir; üçüncü parti uygulamaya ekran kenarı rezerve etme API'si yok. Pencereler dock'un altına girebilir. |
| `wf-tray` | `NSStatusBar` yalnız kendi status item'ını verir; başka uygulamaların menü çubuğu öğelerini devralmak/listelemek mümkün değil. |

### İzin gerektiren kararlar

`uyarla` kararlarının üçü **kullanıcı izni** gerektiriyor ve bu izinler
reddedilirse ilgili davranış çalışmaz. v0.6.1 ile gelen yeni izinler
[UPSTREAM-v0.6.md](UPSTREAM-v0.6.md) bölüm 5'te.

| Yetenek | Gerekli izin |
|---|---|
| `wf-running-apps` | Erişilebilirlik (pencere denetimi) |
| `wf-start-menu`, `wf-search-taskview` | Erişilebilirlik (tuş simülasyonu) |
| `wf-widget-media` | Otomasyon (Music/Spotify Apple Events) |

## Arayüz dili

Upstream v0.4.x'te (`68193d8`) bütün arayüzü İngilizceye çevirdi. README'de artık
"yalnız İngilizce, tarih ve sayılar sistem yerel ayarını izler" yazıyor. macOS
sürümünün arayüzü şu an Türkçe. `pc-davranis-v1` görsel birebir aynılık
istemediği için bu parite kırılması değil. Dil sorusu Orvant kaydında açık
soru olarak duruyor.

## API eşleştirmesi

`T2-API-HARITA` görevi bu belgedeki eşleştirmeleri `api_gap` kayıtlarına işler
([API-HARITASI.md](API-HARITASI.md)).
