# DockHub macOS — 1.0 Parite Listesi

Bu belge [PARITE-ENVANTERI.md](PARITE-ENVANTERI.md) (Windows v0.3–v0.6.1, 32 yetenek grubu) ve
[UPSTREAM-v0.6.md](UPSTREAM-v0.6.md) belgelerinin devamıdır: Windows'ta **v0.7.0'dan 1.0'a** gelen yetenekleri
macOS karşılıklarına göre sınıflandırır ve macOS 1.0 kapsamını önceliklendirir. Kaynak: `docs/release-notes/`,
README ve `src/CustomDock/` altındaki gerçek kod (1.0 dalı).

Karar anahtarı öncekiyle aynıdır: `var` (genel API ile aynı gözlenebilir davranış), `uyarla` (kısmen; izin, farklı
etkileşim ya da daraltılmış kapsam), `yok` (genel API ile üretilemez; paritenin dışında, gerekçesi yazılı).

Öncelik: **P1** macOS 1.0'da olmalı · **P2** 1.0 sonrası · **—** karşılığı yok.

Durum (bu belgenin yazıldığı an, `macos/` kodu): **mevcut** · **kısmen** · **eksik**.

## Özet

| Karar | Sayı |
|---|---|
| `var` | 30 |
| `uyarla` | 10 |
| `yok` | 4 |
| **Toplam** | **44** |

macOS 1.0 kapsamı (P1): 22 madde. En büyük açık **ayar şeması**: macOS `AppConfig` v0.6.1'de kaldı (Windows'taki
yaklaşık 50 alan eksik) ve bilinmeyen alanları yazarken düşürüyor. Windows'ta dışa aktarılan bir ayar dosyasının Mac'te
açılabilmesi (ve Mac'in onu bozmadan geri yazması) 1.0'ın ön koşulu; bu yüzden Faz 23'ün ilk işi o.

## 1. Ayarlar ve güvenlik ağı

| Yetenek | Windows | Karar | Öncelik | Durum |
|---|---|---|---|---|
| Ayar şeması 0.7–1.0 alanları | `Core/AppConfig.cs` | `var` | P1 | mevcut (Faz 23) |
| Bilinmeyen alanları koruyarak okuma/yazma | `JsonStore` | `var` | P1 | mevcut (Faz 23) |
| Geri al (Undo) | `ConfigHistory`, `UndoToast` | `var` | P1 | mevcut (Faz 23: ayarlar ve dock menüsü, ⌘Z) |
| Yedekleme ve geri yükleme (.zip), günlük yedek | `BackupService` | `var` | P2 | eksik |
| Profiller (elle, ekran sayısı, uygulama, saat kuralları) | `ProfileService` | `var` (uygulama kuralı `NSWorkspace` bildirimleriyle) | P1 | mevcut (Faz 23) |
| Ayar eşitleme (klasör) | `SyncService`, `SyncMerge` | `var` (iCloud Drive klasörü de olur) | P2 | eksik |
| Güncelleme denetimi | `UpdateService` | `var` (DMG varlığı; Faz 25) | P1 | eksik |
| Çökme sonrası yeniden başlatma ve bildirim | `CrashRecovery`, Windows Olay Günlüğü | `uyarla`: `launchd` KeepAlive ile yeniden başlatma; çökme kaydı `~/Library/Logs/DiagnosticReports` (izinsiz okunur) | P2 | eksik |
| Sorun bildir (GitHub formu) | `IssueReport` | `var` | P2 | eksik |
| Dört dil (en/tr/de/es) | `Strings_*.json`, `L.T` | `var`: aynı JSON dosyaları paket kaynağı olarak, anahtar İngilizce metin | P1 | mevcut (Faz 23; `Localizer.swift`, CI'da `Scripts/check-translations.py`) |

## 2. Dock ve görünüm

| Yetenek | Windows | Karar | Öncelik | Durum |
|---|---|---|---|---|
| Düzenleme modu (taşı, kaldır, boyutla, ekle) | `DockEditMode`, `EditAdorner` | `var` | P1 | mevcut (Faz 23; boyut yerine sıradaki düzen) |
| Yeni widget galerisi (kart başına widget, arama, sürükle) | `GalleryCard`, `GalleryFilter` | `var` | P1 | mevcut (Faz 23) |
| Kutusuz widget stili + ayraçlar | `WidgetStyle.Seamless` | `var` | P1 | mevcut (Faz 23) |
| Taşmada öğeye oturan kaydırma | `ScrollSnap` | `var` | P2 | eksik |
| Ortak widget paneli şablonu | `WidgetFlyout` | `var` (SwiftUI `popover` + ortak görünüm) | P1 | mevcut (Faz 23; `WidgetPanel`) |
| Gruplu ayarlar menüsü, Genel bakış | `SettingsPages` | `var` | P2 | kısmen |
| Üst bar | `BarDockSurface`, `TopBarSettings` | `uyarla`: macOS menü çubuğu zaten üstte; bar ancak menü çubuğunun altında ya da başka kenarda ve alan ayıramadan | P2 | eksik |
| Başlıklı görev çubuğu modu (Never combine) | `TaskbarButtons` | `uyarla`: pencere listesi Erişilebilirlik izniyle | P2 | eksik |
| Saydam arka plan | `BackdropKind.Transparent` | `var` | P1 | mevcut (Faz 23) |
| Katman düzeni önayarları, tema dosyası | `LayoutPresets`, `ThemeFile` | `var` | P2 | eksik |

## 3. Görev çubuğu ve sistem göstergeleri

| Yetenek | Windows | Karar | Öncelik | Durum |
|---|---|---|---|---|
| Win+1…9 | `WinNumberHotkeys` | `uyarla`: Cmd+Opt+1…9 gibi kendi kısayolu (Carbon `RegisterEventHotKey`, izinsiz) | P2 | eksik |
| Genel kısayollar | `HotkeyService` | `var` (Carbon hot key) | P1 | mevcut (Faz 23; Windows metin biçimi, Win = ⌘) |
| Hızlı başlatıcı (uygulama, ayar, komut, hesap) | `LauncherWindow` | `uyarla`: Spotlight'ın yerini almaz, kendi paneli | P2 | eksik |
| Başlatıcıda dosya arama | Windows Search (OLE DB) | `var`: `NSMetadataQuery` (Spotlight dizini) | P2 | eksik |
| Birim, para birimi, emoji | `LauncherUnits`, `EmojiIndex` | `var` (aynı veri) | P2 | eksik |
| Klavye dili, mikrofon, bildirim sayısı/Rahatsız Etmeyin | tepsi göstergeleri | `yok`: menü çubuğu bunları zaten gösteriyor; bildirim veritabanı ve Odak durumu genel API'de yok | — | — |
| Sanal masaüstleri | `VirtualDesktopService` | `yok`: Spaces için genel API yok | — | — |
| Önizleme: Aero Peek, medya düğmesi | `WindowPreviewWindow` | `uyarla`: `ScreenCaptureKit` (Ekran Kaydı izni) | P2 | eksik |
| Tepsi devralma, alan ayırma | `ShellHost`, `SpaceReserver` | `yok` (PARITE-ENVANTERI.md `wf-reserved-space`) | — | — |

## 4. Widget'lar (0.7–1.0)

| Widget | Karar | Öncelik | Durum |
|---|---|---|---|
| Takvim (iCal bağlantısı) | `var` (aynı iCal ayrıştırma; `EventKit` isteğe bağlı) | P1 | mevcut (Faz 23) |
| Pano geçmişi | `var` (`NSPasteboard.changeCount` yoklaması; parola yöneticileri `org.nspasteboard.ConcealedType` ile atlanır) | P1 | mevcut (Faz 23) |
| Klasör yığını | `var` | P1 | mevcut (Faz 23) |
| Döviz ve kripto | `var` (aynı Frankfurter/CoinGecko) | P1 | mevcut (Faz 23) |
| Hisseler (Stooq) | `var` | P1 | mevcut (Faz 23) |
| Yapılacaklar (yerel, Todoist) | `var` (anahtar Keychain'de) | P1 | mevcut (Faz 23) |
| Ekran görüntüsü | `var` (`screencapture` / `ScreenCaptureKit`) | P1 | mevcut (Faz 23) |
| Parlaklık | `uyarla`: dahili ekran için genel API yok (DisplayServices özel); harici DDC/CI `IOAVService` özel → yalnızca Sistem Ayarları'nı açar | P2 | eksik |
| Wi-Fi ve Bluetooth | `uyarla`: Wi-Fi `CoreWLAN` (var), Bluetooth açma/kapatma genel API'de yok | P2 | eksik |
| GPU | `uyarla`: yük `IOAccelerator` istatistiklerinden (izinsiz, ama sürücüye göre değişir) | P2 | eksik |
| Ping | `var` (ICMP yerine `NWConnection`/sistem `ping`) | P1 | mevcut (Faz 23) |
| Rahatsız etmeyin / Odak | `yok`: Odak durumu genel API'de yok (yalnız kendi uygulamasına `INFocusStatusCenter`) | — | — |
| Güç modu | `uyarla`: Düşük Güç Modu durumu `ProcessInfo.isLowPowerModeEnabled` (okunur), değiştirmek genel API'de yok | P2 | eksik |
| Web widget'ları (aynı `.dockwidget`) | `var` (`WKWebView`; Faz 24) | P1 | eksik |
| Widget SDK 1.0 güncellemeleri | `var` | P1 | eksik |

## 5. 1.0 kapsamı (P1, Faz 23–25)

1. Ayar şeması: 0.7–1.0 alanları + bilinmeyen alanların korunması; CI'daki şema karşılaştırması 0 fark.
2. Dört dil: aynı `Strings_*.json`.
3. Geri al, profiller, genel kısayollar, güncelleme denetimi.
4. Düzenleme modu, galeri araması/sürüklemesi, kutusuz stil, saydam arka plan, ortak panel görünümü.
5. Widget'lar: takvim, pano geçmişi, klasör yığını, döviz/kripto, hisseler, yapılacaklar, ekran görüntüsü, ping.
6. Web widget'ları ve SDK 1.0 (Faz 24).
7. Paketleme, imza/notarization, DMG (Faz 25; Apple Developer hesabı kullanıcıda).

`yok` kararları (4 satır): tepsi devralma ve alan ayırma, sanal masaüstleri, tepsi göstergeleri (klavye dili, mikrofon,
bildirim sayısı), Odak/Rahatsız Etmeyin widget'ı. Hepsinde gerekçe aynı: macOS'ta genel API yok ya da menü çubuğu bu
işi zaten yapıyor.
