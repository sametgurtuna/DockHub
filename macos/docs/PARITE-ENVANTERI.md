# DockHub macOS — Parite Envanteri

Bu belge, DockHub'ın Windows sürümündeki (v2.2.0) her yetenek grubunu macOS'taki
karşılığına göre sınıflandırır. Kaynak: `README.md` ve `src/CustomDock/` altındaki
gerçek kod. Orvant kaydındaki karşılığı: `T1-ENVANTER` görevi, 29 `parity_call` nesnesi.

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
| `var` | 18 | %62 |
| `uyarla` | 9 | %31 |
| `yok` | 2 | %7 |
| **Toplam** | **29** | |

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

### `wf-dock-behavior` — Dock davranışı → **var**
Otomatik gizleme: `NSEvent.addGlobalMonitorForEvents(matching: .mouseMoved)` veya
`NSTrackingArea`. Tam ekran algılama: `NSWorkspace` bildirimleri ve
`CGWindowListCopyWindowInfo`. Çok monitör: `NSScreen.screens`. DPI: `backingScaleFactor`
otomatik uygulanır — Windows'taki PerMonitorV2 uğraşı macOS'ta gerekmez.

### `wf-dock-dnd` — Sürükle-bırak düzenleme → **var**
`NSDraggingSource` / `NSDraggingDestination` ve `NSPasteboard` (`.fileURL` tipi).
Finder'dan `.app` bırakma Windows'taki `.exe`/`.lnk` bırakmanın karşılığıdır.

### `wf-dock-motion` — Kare senkron animasyon → **var**
`CADisplayLink` (macOS 14+) veya `CVDisplayLink`, `NSAnimationContext`, Core Animation.
Yüksek yenileme hızı (ProMotion 120 Hz) desteklenir. Windows'taki
`CompositionTarget.Rendering` yaklaşımının doğrudan karşılığı.

### `wf-dock-menu` — Dock bağlam menüsü → **var**
`NSMenu` / `NSMenuItem`. "Görev Yöneticisi" öğesi macOS'ta Activity Monitor'ü açar
(`open -a "Activity Monitor"`); "Görev çubuğunu gizle" öğesi Dock ayarına dönüşür.

---

## 3. Widget grubu (7 yetenek)

### `wf-widget-host` — Widget mimarisi → **var**
Çok örnekli widget, örnek başına ayar, layout seçimi ve panel açma tamamen kendi
mimarimiz; platform API'si gerektirmez. `Widgets/WidgetRegistry.cs` birebir
SwiftUI karşılığına çevrilebilir. Dikey dock'ta kompakt karo da salt düzen işi.

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

---

## 4. Sistem grubu (4 yetenek)

### `wf-notifications` — Eylemli bildirimler → **var**
`UNUserNotificationCenter` + `UNNotificationAction` eylem düğmelerini,
`UNNotificationSound` sesi verir. Alarmın "kapatılana kadar kalması" için
`.timeSensitive` interruption level kullanılır. Windows toast paritesi sağlanır.

### `wf-settings-ui` — Ayarlar penceresi ve widget galerisi → **var**
SwiftUI `Settings` scene veya ayrı `NSWindow`; önizlemeli galeri salt UI işi.
Windows'taki Mica pencere efektinin karşılığı `NSVisualEffectView`.

### `wf-config` — Yapılandırma ve veri deposu → **var**
`%AppData%\DockHub` → `~/Library/Application Support/DockHub/`.
`config.json` ve `session.json` aynı adlarla kullanılabilir; `Codable` ile
Windows şemasının alan adları korunabilir (`Core/AppConfig.cs` referans şema).
`DOCKHUB_HOME` ortam değişkeni taşınabilir kullanım için aynen desteklenir.

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

`pc-kapsam-v1` gereği bu iki yetenek için karar **parite tanımından çıkarma**
önerisidir; onay kullanıcıya aittir:

| Yetenek | Neden karşılıksız |
|---|---|
| `wf-reserved-space` | macOS'ta `NSScreen.visibleFrame`'i sistem yönetir; üçüncü parti uygulamaya ekran kenarı rezerve etme API'si yok. Pencereler dock'un altına girebilir. |
| `wf-tray` | `NSStatusBar` yalnız kendi status item'ını verir; başka uygulamaların menü çubuğu öğelerini devralmak/listelemek mümkün değil. |

### Kullanıcıya sorulacak (açık soru)

Bu iki yetenek parite tanımından **çıkarılsın mı**, yoksa aşağıdaki daraltılmış
karşılıklarla **uyarla** sayılsın mı?

- `wf-reserved-space` → "sistem Dock'u gizlenir, onun alanı kullanılır" (rezervasyon yok)
- `wf-tray` → "yalnız DockHub'ın kendi menü çubuğu öğesi" (devralma yok)

Ayrıca `uyarla` kararlarının üçü **kullanıcı izni** gerektiriyor ve bu izinler
reddedilirse ilgili davranış çalışmaz:

| Yetenek | Gerekli izin |
|---|---|
| `wf-running-apps` | Erişilebilirlik (pencere denetimi) |
| `wf-start-menu`, `wf-search-taskview` | Erişilebilirlik (tuş simülasyonu) |
| `wf-widget-media` | Otomasyon (Music/Spotify Apple Events) |

## Sonraki adım

`T2-API-HARITA` görevi bu belgedeki eşleştirmeleri 20 `api_gap` kaydına işler ve
her birinin `status` değerini `mapped` / `partial` / `none` olarak belirler.
