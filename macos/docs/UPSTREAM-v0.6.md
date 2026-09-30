# DockHub macOS — Upstream v0.4.0–v0.6.1 Envanteri

Bu belge, feature dalının dayandığı upstream `76e0cb9` (v0.3.0) ile `5ee84f7`
(v0.6.1) arasında gelen yetenekleri sınıflandırır. Arada 18 commit, 151 dosya ve
yaklaşık 7.300 eklenen satır var. Upstream, feature dalına `c821072` merge
commit'iyle alındı. Kaynak, `src/CustomDock/` altındaki gerçek kod ve README.

Orvant karşılığı: `T15-UPSTREAM-ENVANTER` görevi, 9 yeni `parity_call` nesnesi.
Sınıflandırma anahtarı ve ölçütler (`pc-kapsam-v1`, `pc-davranis-v1`)
[PARITE-ENVANTERI.md](PARITE-ENVANTERI.md) ile aynı.

> **Modelleme notu.** Bu görev yalnız parite kararlarını üretir. Yeni yeteneklerin
> `api_gap` kayıtları burada **oluşturulmadı**. Her biri, o yeteneği uygulayacak
> görevin sahipliğinde açılacak. Tek bir görevin bütün API boşluklarını üretmesi
> T2-API-HARITA'da bir detay düzeltmesinin 12 görevi birden eskitmesine yol açmıştı.
> Aşağıdaki API notları o görevlere girdi olacak araştırma bulgularıdır.

## Özet

| Yetenek | Kategori | Karar | İzin |
|---|---|---|---|
| `wf-window-preview` — Canlı pencere önizlemeleri | taskbar | **uyarla** | Ekran Kaydı, kapatma için Erişilebilirlik |
| `wf-jumplist` — Jump List (görevler ve son öğeler) | taskbar | **uyarla** | Korumalı klasörler için dosya erişimi (ölçülmedi) |
| `wf-app-badges` — Rozet sayıları | taskbar | **uyarla** | Erişilebilirlik |
| `wf-network-status` — Ağ durumu ikonu | taskbar | **var** | yok |
| `wf-drop-on-app` — Dosyayı uygulamaya bırakıp açma | dock | **var** | yok |
| `wf-dock-groups` — Klasörler (gruplar) | dock | **var** | yok |
| `wf-multi-display` — Her ekranda dock | dock | **var** | yok |
| `wf-widget-pin-end` — Widget'ı sağ kenara sabitleme | widgets | **var** | yok |
| `wf-global-hotkey` — Global kısayol | system | **var** | yok |

Toplam: 6 `var`, 3 `uyarla`, 0 `yok`. Envanterdeki yetenek grubu sayısı 33'ten
42'ye çıktı. Parite dışı bırakılan iki grup (`wf-reserved-space`, `wf-tray`)
değişmedi.

---

## 1. Yeni yetenek grupları

### `wf-window-preview` — Canlı pencere önizlemeleri → **uyarla**
**Windows** (`Dock/WindowPreviewWindow.cs`): Çalışan bir uygulamanın düğmesinin
üzerine gelince DWM, pencerenin canlı küçük resmini çizer (`DwmRegisterThumbnail`,
`DwmUpdateThumbnailProperties`). Önizlemede kapatma düğmesi var. İzin gerekmez.

**macOS:** Başka bir uygulamanın penceresinin görüntüsünü almak için
**ScreenCaptureKit** gerekir: tek kare için `SCScreenshotManager.captureImage`,
canlı görüntü için `SCStream`. İkisi de **Ekran Kaydı** iznine bağlı ve izin
imzasız pakette her derlemede sıfırlanır. Pencereleri bulmak için
`CGWindowListCopyWindowInfo` izinsiz çalışır, ama pencere başlıkları
(`kCGWindowName`) yine Ekran Kaydı ister. Başka uygulamanın penceresini kapatmak
Erişilebilirlik ister (`AXUIElement`, `kAXCloseButtonAttribute` → `AXPress`).

**Neden `uyarla`:** Davranış üretilebilir, ama iki ayrı izne bağlı. İzin yoksa
önizleme yerine yalnız pencere listesi gösterilmeli.

### `wf-jumplist` — Jump List → **uyarla**
**Windows** (`Shell/JumpListService.cs`): Adı Jump List olsa da Windows Jump List
API'si okunmuyor. İki parçası var:

1. **Görevler:** Bilinen uygulamalar için elle yazılmış tablo. Tarayıcılarda yeni
   pencere ve gizli pencere, VS Code'da yeni boş pencere, Explorer'da İndirilenler,
   Belgeler, Masaüstü ve Resimler, terminallerde yönetici olarak aç, Discord'da
   ses ayarları.
2. **Son öğeler:** `%AppData%\Microsoft\Windows\Recent` klasöründeki `.lnk`
   dosyaları çözülüp **uzantıya göre** uygulamayla eşleniyor. Explorer için
   `AutomaticDestinations` dosyası regex ile taranıyor, VS Code için
   `storage.json` okunuyor.

**macOS:**
- Görevler aynı şekilde bundle kimliğine göre tablo olur: Chrome, Brave ve Edge
  için `open -na <uygulama> --args --new-window` ile `--incognito`, Firefox için
  `-private-window`, VS Code için `-n`, Finder için klasör açma. **Safari'nin gizli
  penceresi** komut satırı argümanıyla açılamıyor, bunun için Otomasyon izni
  gerekir. "Yönetici olarak çalıştır" görevinin macOS'ta karşılığı yok.
- Son öğeler için macOS'ta daha doğru bir kaynak var: Spotlight
  (`NSMetadataQuery`, `kMDItemLastUsedDate`), uygulamanın `Info.plist` içinde
  bildirdiği belge türleriyle (`CFBundleDocumentTypes` → `LSItemContentTypes`)
  süzülür. Uzantı tablosu elle tutulmaz. Finder'ın son klasörleri
  `com.apple.finder` tercihlerindeki `FXRecentFolders` değerinde tutuluyor.
- **Ölçülmedi:** Masaüstü, Belgeler ve İndirilenler TCC ile korunuyor. Bu
  klasörlerdeki sonuçların izin istemeden dönüp dönmediği, uygulamayı yazan görev
  tarafından imzalı pakette ölçülmeli.

**Neden `uyarla`:** Görev tablosunun bir kısmı Otomasyon izni istiyor, son
öğelerin bir kısmı dosya erişim iznine takılabilir.

### `wf-app-badges` — Rozet sayıları → **uyarla**
**Windows** (`Dock/AppButton.cs`, `UpdateBadge`): Uygulamaların bir rozet API'si
yok. Sayı **pencere başlığından** regex ile ayıklanıyor: "Gelen Kutusu (3)" → 3.
Başlık `•` veya `*` içeriyorsa sayısız nokta konuyor. Görev çubuğu bindirme
ikonu da noktaya çevriliyor.

**macOS:** Uygulamalar rozeti kendileri yayınlıyor (`NSDockTile.badgeLabel`), yani
tahmin yerine gerçek değer var. Ama başka uygulamanın rozetini okumanın genel
API'si yok:
- Erişilebilirlik ile sistem Dock sürecinin ağacı okunur: her `AXDockItem`
  öğesinin `AXStatusLabel` özelliği rozet metnidir. Sistem Dock'u otomatik
  gizlemede de çalışmaya devam ettiği için Replace modunda da okunabilir.
- `lsappinfo info -only StatusLabel <uygulama>` aynı değeri veriyor, ama çıktı
  biçimi belgelenmemiş bir sistem aracına ait. **Ölçüm:** oturumda rozet koyan
  bir uygulama açık değildi, bu yüzden bu yol doğrulanmadı.
- Windows'taki başlık ayrıştırma yolu macOS'ta daha pahalı: başlık için Ekran
  Kaydı ya da Erişilebilirlik izni gerekir.

**Neden `uyarla`:** Gözlenebilir davranış (düğmede sayı) üretilebilir, ama
Erişilebilirlik iznine bağlı.

### `wf-network-status` — Ağ durumu ikonu → **var**
**Windows** (`Services/NetworkStatusService.cs`, `Dock/NetworkStatusIconView.cs`):
Kablolu, kablosuz ya da bağlantı yok durumunu gösteren canlı ikon. Windows 11'in
ağ simgesi Explorer'a ait olduğu ve tepsiden okunamadığı için ayrıca yazılmış.

**macOS:** `Network.framework` içindeki `NWPathMonitor`: `status` ve
`usesInterfaceType(.wifi / .wiredEthernet)`. İzin gerekmez. (Wi-Fi ağ adı konum
izni ister, ama Windows sürümü de ad göstermiyor.)

### `wf-drop-on-app` — Dosyayı uygulamaya bırakıp açma → **var**
**Windows** (`Dock/AppButton.cs`, `OnFileDrop`): Dosyayı bir uygulama düğmesine
bırakınca dosya o uygulamayla açılır.

**macOS:** `NSDraggingDestination` ile `NSWorkspace.open(_:withApplicationAt:configuration:)`.
Sistem Dock'unun kendi davranışıyla aynı. İzin gerekmez.

### `wf-dock-groups` — Klasörler (gruplar) → **var**
**Windows** (`Dock/GroupItemView.cs`, `Dock/RenameFolderDialog.cs`): Bir öğeyi
başka bir öğenin üzerine sürükleyince klasör oluşur. İçinde uygulama da widget da
olabilir. Kapalıyken 2×2 küçük ikon gösterir, açılınca ızgara halinde ve kademeli
yelpaze animasyonuyla açılır. Adı değiştirilebilir, vurgu rengi seçilebilir. Dock
menüsünde "Create group" var. Kod açıklamasında "macOS Stacks tarzı" yazıyor.

**macOS:** Tamamen arayüz ve veri modeli işi, platform API'si gerekmez.
Config uyumu için dikkat edilecekler:
- `groupAccent`, WPF kaynak anahtarı olarak yazılıyor (`AccentBlueBrush` vb.).
  Değerleri macOS sistem renklerinin koyu tema karşılıkları: `#0A84FF` systemBlue,
  `#32D74B` systemGreen, `#FF9F0A` systemOrange, `#FF453A` systemRed,
  `#BF5AF2` systemPurple, `#FFD60A` systemYellow. Anahtar adları korunmalı,
  `NSColor.system*` ile eşlenmeli.
- `children` iç içe `DockItem` listesi. Swift modeli kendini içeren bir yapıya
  dönüşmeli (`[DockItem]?`).

### `wf-multi-display` — Her ekranda dock → **var**
**Windows** (`Dock/DockWindow.Positioning.cs`, `Core/AppConfig.cs`):
- `showOnAllDisplays`: diğer ekranlara da dock konur. Widget'lar ve tepsi
  yalnız ana ekranda (`monitorDevice`) kalır, diğer dock'larda sabitlenmiş
  uygulamalar, gruplar, Başlat ve saat bulunur.
- `runningAppsOnOwnDisplay`: her dock yalnız penceresi o ekranda olan açık
  uygulamaları listeler.
- `displaySizes`: ekran başına dock boyutu (anahtar aygıt adı, değer
  Small/Medium/Large). Anahtar yoksa ana boyut kullanılır.

**macOS:** `NSScreen.screens`, `NSApplication.didChangeScreenParametersNotification`.
Pencerenin hangi ekranda olduğu `CGWindowListCopyWindowInfo` içindeki
`kCGWindowBounds` ve `kCGWindowOwnerPID` ile izinsiz bulunur (yalnız pencere adı
izin ister). Ekran anahtarı: mevcut `monitorDevice` gibi `NSScreen.localizedName`.
Windows aygıt adları (`\\.\DISPLAY2`) macOS'ta anlamsız olduğu için config dosyası
platformlar arasında taşınırsa bu anahtarlar eşleşmez. Anahtar bulunamazsa ana
boyuta düşülür (Windows'un da yaptığı).

### `wf-widget-pin-end` — Widget'ı sağ kenara sabitleme → **var**
**Windows** (`Core/AppConfig.cs` → `DockItem.PinnedEnd`): Widget, kayan öğe
listesinden çıkıp dock'un sabit sağ ucuna (tepsi ve saatten sonra) yerleşir.
Yalnız widget'lar için geçerli. Değer `false` iken yazılmaz (`WhenWritingDefault`).

**macOS:** Salt yerleşim işi.

### `wf-global-hotkey` — Global kısayol → **var**
**Windows** (`Dock/DockWindow.xaml.cs`, `RegisterGlobalHotkey`): Win+Alt+D, alınamazsa
Ctrl+Alt+D. Bütün dock'ları gösterip gizler. README ayarlanabilir diyor, ama kod
kısayolu sabit yazıyor.

**macOS:** Carbon `RegisterEventHotKey` hâlâ destekleniyor ve izin istemiyor
(`NSEvent.addGlobalMonitorForEvents` ise Erişilebilirlik ister). **Çakışma:**
⌥⌘D macOS'un kendi "Dock'u gizle/göster" kısayolu. Windows'taki yedek kısayolun
karşılığı ⌃⌥D çakışmıyor ve varsayılan olarak o seçilmeli.

---

## 2. Mevcut gruplara gelen genişlemeler

Bunlar yeni yetenek grubu değil. Parite kararları T1-ENVANTER'in yeniden
incelemesinde [PARITE-ENVANTERI.md](PARITE-ENVANTERI.md) içinde güncellenir.

| Mevcut grup | v0.6.1'de gelen | macOS etkisi |
|---|---|---|
| `wf-dock-motion` | Açılır pencerelerde genie, zoom ve küçülme animasyonları, klasörlerde yelpaze (`Dock/GenieEffectHelper.cs`, `PopupAnimationHelper.cs`) | WPF sürümü açılır pencerenin anlık görüntüsünü (`RenderTargetBitmap`) dokulu bir 3B mesh'e (`Viewport3D` + `MeshGeometry3D`) koyup köşe noktalarını her karede büküyor. macOS'ta Core Animation'ın mesh bükme API'si (`CAMeshTransform`) private. Genel yol: aynı mesh'i Metal ile çizmek (`MTKView`, doku + köşe tamponu), ya da görüntüyü yatay şeritlere bölüp her şeride `CATransform3D` uygulayarak yaklaşık üretmek. Zoom ve yelpaze için Core Animation yeterli. Karar `var` kalır. |
| `wf-widget-host` | Widget'a tıklayınca açılan paneller (saat, dünya saati, medya), widget başına sağ tık menüsü (eylemler, layout, ayarlar) | Platform işi değil. Bizde paneller ve widget menüsü **henüz yok**. |
| `wf-dock-menu` | Menüye "Create group" eklendi | Klasörlerle birlikte gelir. |
| `wf-settings-ui` | Sayfalar: General, Taskbar, Appearance, Dock items, Widget gallery, About. General sayfasına ana ekran, tüm ekranlar, ekran başına boyut satırları eklendi. Dock items sayfası ayrı dosyaya taşındı (`SettingsWindow.Items.cs`). | Ayarlar penceresi görevinin kapsamı bunlara göre çizilmeli. |
| `wf-config` | Yeni alanlar (aşağıda) | Swift modeli kırık, düzeltilmeli. |
| `wf-running-apps` | Rozet ve önizleme ayrı gruplara çıktı. Kalan davranış (listeleme, gruplama, öne getirme) aynı. | Karar değişmez. |

## 3. Yapılandırma şeması farkı

`Scripts/compare-config-schema.py` merge sonrası şu farkı raporladı:

```
HATA - her zaman yazilmasi gereken ama macOS ciktisinda OLMAYAN:
   - displaySizes
   - runningAppsOnOwnDisplay
   - showOnAllDisplays
```

Betik yalnız üst düzey `AppConfig` alanlarına bakıyor. `DockItem` tarafında da
yeni alanlar var:

| Alan | Tür | Yazma kuralı | Swift durumu |
|---|---|---|---|
| `AppConfig.showOnAllDisplays` | bool, varsayılan false | her zaman | eksik |
| `AppConfig.runningAppsOnOwnDisplay` | bool, varsayılan **true** | her zaman | eksik |
| `AppConfig.displaySizes` | aygıt adı → `DockSize` sözlüğü, büyük/küçük harf duyarsız | her zaman (boşsa `{}`) | eksik |
| `DockItem.kind = "Group"` | yeni enum değeri | — | **decode hatası**: Swift enum yalnız App/Widget/Separator biliyor, `Group` içeren bir config hiç açılamaz |
| `DockItem.pinnedEnd` | bool | `WhenWritingDefault`: false ise yazılmaz | eksik |
| `DockItem.groupName` | string? | nil ise yazılmaz | eksik |
| `DockItem.groupAccent` | string? (WPF fırça anahtarı) | nil ise yazılmaz | eksik |
| `DockItem.children` | `[DockItem]?` | nil ise yazılmaz | eksik |

`Group` decode hatası T4-ISKELET'in 3 numaralı kabul ölçütünü ("config alan
adlarıyla uyum") doğrudan bozuyor. T4 yeniden incelenirken düzeltilmeli.

## 4. Arayüz dili

v0.4.x'te (`68193d8`) bütün kod, arayüz, widget'lar ve ayarlar İngilizceye
çevrildi. README'deki "arayüz Türkçe" notu kaldırıldı, yerine "yalnız İngilizce,
tarih ve sayılar sistem yerel ayarını izler" yazıldı. Kurulum programı İngilizce
ve Türkçe.

macOS sürümünün arayüzü şu an Türkçe. `pc-davranis-v1` görsel birebir aynılığı
şart koşmadığı için bu bir parite kırılması değil. Ama Orvant kaydında açık soru
olarak duruyor ("Arayüz dili Türkçe mi İngilizce mi olacak?") ve ayarlar
penceresi yazılmadan önce karara bağlanmalı. Çok sayıda yeni metin ekleyecek.

## 5. Yeni izinler

| Yetenek | İzin | İzin yoksa |
|---|---|---|
| `wf-window-preview` | Ekran Kaydı (görüntü), Erişilebilirlik (kapatma) | Yalnız pencere listesi |
| `wf-app-badges` | Erişilebilirlik | Rozet gösterilmez |
| `wf-jumplist` | Otomasyon (Safari gizli pencere), korumalı klasörlerde dosya erişimi (ölçülmedi) | O görev ya da o öğeler gösterilmez |

Hepsi `d-izin-zamani` kararına uyar: izin açılışta değil, özellik ilk
kullanıldığında istenir. İmzalama kurulmadan bu izinler her derlemede sıfırlanır
(bkz. [IMZALAMA.md](IMZALAMA.md)).

## 6. Küçük düzeltmeler (parite etkisi yok)

- v0.3.1: görev sonlandırma, alt menü tıklaması, özel ikon algılama düzeltmeleri.
- HyperX kablosuz kulaklık pil okumasında zaman aşımı ve overlapped I/O düzeltmesi.
  macOS'ta bu aygıt yolu (satıcıya özel HID) uygulanmadı, etkisi yok.
- Performans iyileştirmeleri, ekran görüntüleri, sürüm artırımları.
