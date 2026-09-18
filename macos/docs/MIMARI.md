# DockHub macOS — Mimari

Swift proje yerleşimi, modül sınırları, yapılandırma uyumu ve derleme akışı.
Girdiler: [API-HARITASI.md](API-HARITASI.md), ölçütler `pc-kapsam-v1` / `pc-davranis-v1`.
Orvant kaydındaki karşılığı: `T3-MIMARI` görevi.

Kararlar: `d-teknoloji` (native Swift + AppKit/SwiftUI), `d-wpf-yeniden-yazim`
(UI ve platform katmanı sıfırdan), `d-macos-hedefi-v2` (minimum macOS 27),
`d-arac-zinciri` (Xcode kurulacak).

---

## 1. Derleme yolu: SPM birincil, Xcode ikincil

Ölçülmüş bulgu: **Xcode derleme için şart değil.** Command Line Tools + Swift 6.4 ile
Swift Package Manager, AppKit/UserNotifications/ServiceManagement/CoreLocation/IOKit
içeren bir hedefi sorunsuz derliyor.

Ama çıplak executable yetmiyor. Ölçülen davranış:

| API | Çıplak `.build/debug/probe` | Elle kurulmuş `.app` paketi |
|---|---|---|
| `NSPanel`, `NSVisualEffectView` | çalışıyor | çalışıyor |
| `NSWorkspace.runningApplications` | çalışıyor (92 uygulama) | çalışıyor |
| `NSScreen`, `backingScaleFactor` | çalışıyor (2 ekran) | çalışıyor |
| `host_statistics` HOST_CPU_LOAD_INFO | çalışıyor (`kr=0`) | çalışıyor |
| `IOPSCopyPowerSourcesInfo` | çalışıyor | çalışıyor |
| `CLLocationManager` | çalışıyor | çalışıyor |
| `SMAppService.mainApp` | **çöküyor** | çalışıyor (`status=3`) |
| `UNUserNotificationCenter.current()` | **çöküyor** | çalışıyor |

Çıplak executable'da atılan istisna:
`NSInternalInconsistencyException — bundleProxyForCurrentProcess is nil`.

**Karar:** Derleme her zaman bir `.app` paketi üretir. `swift build` ham ikiliyi verir,
`Scripts/bundle.sh` onu `Info.plist` ile birlikte paketler. Xcode geldiğinde aynı
paketleme imzalama (`codesign`) ve notarization için kullanılır — SPM yapısı değişmez.

`SMAppService.mainApp.status = 3` (`notFound`) bekleniyor: uygulama imzasız ve
`/Applications` altında değil. Giriş öğesi kaydının gerçekten çalışması imzalı ve
doğru konumdaki paket gerektirir — bu `wf-startup` için Xcode'a bağlı tek nokta.

## 2. Proje yerleşimi ve modül sınırları

```
DockHub/                        (mevcut Windows deposu, dokunulmaz)
├── src/CustomDock/             Windows WPF kaynagi — yalniz referans
└── macos/                      macOS surumu (d-kod-yerlesimi)
    ├── Package.swift
    ├── Sources/
    │   ├── DockHubCore/        Platformdan bagimsiz
    │   ├── DockHubPlatform/    macOS API sarmalayicilari
    │   ├── DockHubUI/          AppKit + SwiftUI gorunumler
    │   └── DockHubApp/         Executable: main, AppDelegate
    ├── Resources/Info.plist
    ├── Scripts/bundle.sh
    └── docs/                   Bu belgeler
```

Modül sınırları, Windows sürümünün klasör yapısını **kasten** taklit eder ki
eşleştirme izlenebilir kalsın:

| macOS modülü | Windows karşılığı | İçerik | Kural |
|---|---|---|---|
| `DockHubCore` | `Core/`, `Widgets/*Service` | `AppConfig`, `JSONStore`, `Log`, widget veri modelleri, zamanlayıcı mantığı | **AppKit import etmez.** Test edilebilir, saf Swift. |
| `DockHubPlatform` | `Native/`, `Shell/`, `Services/` | `NSWorkspace`, `AXUIElement`, Mach/IOKit ölçümleri, `SMAppService`, bildirimler, Dock gizleme | Her API boşluğu için bir protokol + gerçek uygulama. İzin gerektirenler ayrı. |
| `DockHubUI` | `Dock/`, `Controls/`, `Widgets/*View`, `Settings/` | `DockPanel` (NSPanel), widget görünümleri, ayarlar penceresi | `DockHubCore` ve `DockHubPlatform`'u kullanır, tersi olmaz. |
| `DockHubApp` | `App.xaml.cs` | `main.swift`, `AppDelegate`, tek örnek kontrolü, CLI argümanları | Yalnız bağlama (wiring). |

**Neden protokol katmanı:** API haritasındaki 7 `partial` eşleştirmenin hepsi izin
veya kapsam kısıtı taşıyor. Her birini protokol arkasına almak, izin reddedildiğinde
devreye girecek "yok sayan" uygulamayı (null object) mümkün kılar ve `DockHubUI`'nin
izin durumundan habersiz kalmasını sağlar. Örnek:

```swift
protocol WindowController {                    // ag-running-apps
    func windows(of app: NSRunningApplication) -> [WindowHandle]
    func raise(_ window: WindowHandle)
}
struct AXWindowController: WindowController { /* Erisilebilirlik izni var */ }
struct AppOnlyWindowController: WindowController { /* izin yok: yalniz activate */ }
```

## 3. Yapılandırma uyumu: aynı şema, aynı alan adları

Windows'un serileştirme sözleşmesi `Core/JsonStore.cs`'den okundu:

```csharp
PropertyNamingPolicy = JsonNamingPolicy.CamelCase
Converters = { new JsonStringEnumConverter() }
WriteIndented = true
[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]   // null yazilmaz
AppConfig.CurrentVersion = 2
```

**Karar: dönüştürücü yazılmaz, aynı alan adları kullanılır.** Gerekçe: Swift
property'leri zaten camelCase olduğundan `Codable`'ın varsayılan anahtar üretimi
C#'ın `JsonNamingPolicy.CamelCase` çıktısıyla birebir örtüşüyor. Aynı `config.json`
iki platformda okunabilir kalır; bu ileride ayarları taşımayı bedava yapar.

Dikkat edilecek üç nokta:

1. **Enum'lar PascalCase string.** `JsonStringEnumConverter` C# enum üye adını olduğu
   gibi yazar: `"Bottom"`, `"Replace"`, `"Blur"`, `"Small"`, `"Floating"`, `"Full"`,
   `"Center"`, `"App"`. Swift tarafında ham değer açıkça verilir:

   ```swift
   enum DockEdge: String, Codable { case bottom = "Bottom", top = "Top", left = "Left", right = "Right" }
   enum BackdropKind: String, Codable { case blur = "Blur", acrylic = "Acrylic", solid = "Solid" }
   ```

2. **Null yazılmaz.** Swift'in sentezlediği `Codable` kodlayıcısı Optional alanlar için
   `encodeIfPresent` kullanır, yani nil atlanır — beklenen davranış bu. Gerçekten
   böyle olduğu `T4-ISKELET` görevinin dördüncü kabul ölçütünde iki dosya
   karşılaştırılarak sınanacak; şimdiden doğrulanmış sayılmıyor.

3. **Windows'a özgü alanlar korunur, yorumlanmaz.** `taskbarMode`, `startWithWindows`,
   `monitorDevice` ve v1 uyum alanları (`widgets`, `widgetSettings`, `reserveSpace`)
   okunup **aynen geri yazılır**. `reserveSpace` `d-yok-cikarma` ile parite dışı
   olduğundan macOS'ta hiçbir etkisi olmaz ama silinmez — aynı dosya Windows'a
   dönerse bilgi kaybolmasın.

Yollar: `~/Library/Application Support/DockHub/config.json` ve `session.json`
(`ag-config-path`). `DOCKHUB_HOME` geçersiz kılma davranışı `Core/AppPaths.cs`'deki
gibi korunur. Yazma `Data.write(to:options:.atomic)` ile — C#'taki `.tmp` + taşıma
deseninin karşılığı.

## 4. Derleme ve çalıştırma komutları

`Package.swift` iskeleti:

```swift
// swift-tools-version:6.0
import PackageDescription
let package = Package(
    name: "DockHub",
    platforms: [.macOS(.v27)],
    targets: [
        .target(name: "DockHubCore"),
        .target(name: "DockHubPlatform", dependencies: ["DockHubCore"]),
        .target(name: "DockHubUI", dependencies: ["DockHubCore", "DockHubPlatform"]),
        .executableTarget(name: "DockHubApp", dependencies: ["DockHubUI"]),
        .testTarget(name: "DockHubCoreTests", dependencies: ["DockHubCore"]),
    ]
)
```

Komutlar:

```sh
cd DockHub/macos
swift build                      # ham ikili: .build/debug/DockHubApp
Scripts/bundle.sh debug          # .build/DockHub.app olusturur (Info.plist + ikili)
open .build/DockHub.app          # calistir
swift test                       # DockHubCore testleri (AppKit gerektirmez)
```

`Scripts/bundle.sh` yapacağı iş (sondada bire bir çalıştırıldı):

```sh
APP=".build/DockHub.app"
rm -rf "$APP" && mkdir -p "$APP/Contents/MacOS"
cp ".build/$1/DockHubApp" "$APP/Contents/MacOS/DockHub"
cp Resources/Info.plist "$APP/Contents/Info.plist"
```

`Info.plist` zorunlu anahtarlar (sondada doğrulandı):

| Anahtar | Değer | Neden |
|---|---|---|
| `CFBundleIdentifier` | `com.dockhub.mac` | Olmadan `SMAppService` ve `UNUserNotificationCenter` çöküyor |
| `CFBundleExecutable` | `DockHub` | Paket içindeki ikili adı |
| `LSUIElement` | `true` | Dock'ta ikon ve menü çubuğu göstermez — bir dock uygulaması için doğru davranış |
| `LSMinimumSystemVersion` | `27.0` | `d-macos-hedefi-v2` |
| `NSLocationWhenInUseUsageDescription` | metin | `ag-geolocation`, olmadan konum isteği reddedilir |

## 4b. Upstream v0.3.0 modül sınırlarını bozuyor mu?

Hayır. `76e0cb9` ile gelen üç widget mevcut dört modüle sorunsuz yerleşiyor:

| Yeni yetenek | Windows kaynağı | macOS modülü | Neden |
|---|---|---|---|
| Ses aygıtı | `Native/AudioInterop.cs`, `Services/AudioService.cs` | `DockHubPlatform` | CoreAudio sarmalayıcısı, `AudioController` protokolü |
| Çöp kutusu | `Services/RecycleBinService.cs` | `DockHubPlatform` | `~/.Trash` erişimi; boşaltma için `TrashEmptier` protokolü — Otomasyon izni yoksa "yok sayan" uygulama devreye girer |
| Aygıt pilleri | `Native/HidInterop.cs`, `Services/DeviceBatteryService.cs` | `DockHubPlatform` | IORegistry ve IOHIDManager iki ayrı uygulama, tek protokol arkasında |

Üçü de bölüm 2'deki **protokol katmanı** kuralını doğruluyor: `ag-trash` ve
`ag-hid-battery` `partial` olduğu için izin reddedildiğinde devreye girecek
daraltılmış uygulamaya ihtiyaç var; `DockHubUI` bu ayrımdan habersiz kalıyor.

Widget veri modelleri (`AudioDevice`, `TrashState`, `DeviceBattery`)
`DockHubCore`'a, görünümleri `DockHubUI`'ye gider. Yeni modül gerekmiyor.

## 5. T2'den devreden dört konu

| Konu | Karar | Gerekçe |
|---|---|---|
| Erişilebilirlik izni | **Özellik kullanılınca istenecek**, açılışta değil | Açılışta izin istemek kullanıcıyı kaçırır; dock'un çekirdeği (görünüm, uygulama başlatma) izinsiz çalışıyor. İzin yoksa `AppOnlyWindowController` devrede kalır. |
| Otomasyon izni / media | **Music + Spotify ile sunulacak**, kapsam arayüzde açıkça yazılacak | Widget'ı hiç sunmamak `wf-widget-media` paritesini tamamen kaybettirir. Private framework kullanılmaz (`d-teknoloji` ile uyumsuz kırılganlık). |
| Dock gizleme geri alma | **Zorunlu**, `session.json`'a yazılacak | Windows'taki `--restore-taskbar` güvenlik ağının karşılığı. Kullanıcının Dock ayarını değiştirip geri almamak kabul edilemez. |
| Apple Developer hesabı | **Kullanıcıya açık soru** | Para kararı. Alınmazsa yalnız yerel kullanım; `wf-installer` ve imzalı `wf-startup` eksik kalır. |

İlk üçü yetki kapsamındaki uygulama kararıdır (`accepted_by: agent`). Dördüncüsü
kullanıcıya aittir ve projenin `open_questions` kaydında durur.

## 6. Sonraki iş

`T4-ISKELET`: bu yerleşimi gerçekten kurar, ekran kenarında cam efektli dock
penceresini çıkarır ve `config.json` uyumunu iki dosya karşılaştırarak gösterir.
Önkoşulları: `T0-ORTAM` (Xcode), `T2-API-HARITA` (bitti), `T3-MIMARI` (bu belge).
