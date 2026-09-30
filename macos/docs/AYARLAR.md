# DockHub macOS — Ayarlar Penceresi

Windows karşılığı: `Settings/SettingsWindow.xaml`, `SettingsWindow.Items.cs`,
`SettingsWindow.Gallery.cs`. Orvant karşılığı: `T18-AYARLAR`, `mf-settings-ui`.
Arayüz İngilizce (`d-arayuz-dili`).

## Açma

- Dock'a sağ tık → **Settings…** ya da **Add Widget…** (galeri sayfası).
- Komut satırı: `DockHub --settings`.

## Sayfalar

| Sayfa | İçerik |
|---|---|
| General | Replace the Dock (DockHub / Show both), ana ekran, macOS Dock'u geri yükle, veri klasörünü aç, yeniden başlat, çık |
| Appearance | Tema, arka plan (Blur/Acrylic/Solid), ton, hover; boyut, şekil, kenar boşluğu, genişlik (Full/Fit), hizalama; ekran kenarı |
| Dock Items | Uygulama ekle (dosya seçici), widget ekle (galeriye gider), ayırıcı ekle, macOS Dock'undan içe aktar; sürükleyerek sıralama, silme; uygulama adı ve başlatma argümanı, widget varyantı, klasör adı |
| Widget Gallery | Kategorilere göre bütün widget'lar, Windows'taki ad ve açıklamalarla; varyant seçip **+** ile ekleme |
| About | Sürüm, proje bağlantısı, ayar dosyasının yeri |

## Yalnız çalışan ayarlar gösteriliyor

macOS dock'u henüz uygulamadığı bir ayarı göstermek, kullanıcıya işe yaramayan
bir anahtar vermek olurdu. Aşağıdaki Windows ayarları **bilerek yok**. Her biri
ilgili özellik yazılınca eklenecek. Değerleri config'te duruyor ve korunuyor.

| Windows ayarı | Config alanı | Neden yok | Bağlı yetenek |
|---|---|---|---|
| Start with Windows | `startWithWindows` | Girişte başlatma (`SMAppService`) henüz yazılmadı; imzalamaya bağlı | `wf-startup` |
| Explorer "Pin to DockHub" | `explorerPinMenu` | Finder'dan sabitleme yok | `wf-explorer-pin` |
| Hide in full screen apps | `hideOnFullscreen` | Tam ekranda çekilme yok | `wf-dock-behavior` |
| Auto-hide | `autoHide` | Otomatik gizleme yok | `wf-dock-behavior` |
| Show on all displays, running apps on own display, ekran başına boyut | `showOnAllDisplays`, `runningAppsOnOwnDisplay`, `displaySizes` | Tek dock var | `wf-multi-display` |
| Taskbar sayfası: Start, Search, Task view, running apps, tray, clock, show desktop | `showStartButton` … `showDesktopButton` | Bu düğmelerin hiçbiri macOS dock'unda yok; sabitlenmemiş çalışan uygulamalar da listelenmiyor | `wf-start-menu`, `wf-search-taskview`, `wf-running-apps`, `wf-clock-notify` |
| Import taskbar pins | — | Karşılığı var: **Import from Dock** (`com.apple.dock` → `persistent-apps`) | `wf-pinned-apps` |
| Widget ayar formları | `items[].settings` | Ayar anahtarları Windows'la henüz eşit değil ([WIDGET-SEMASI.md](WIDGET-SEMASI.md) bölüm 4); form yanlış anahtar yazardı | `wf-widget-host` |
| Galeride canlı önizleme | — | Kartlarda simge ve açıklama var, canlı widget çizilmiyor | `wf-settings-ui` |

## Değişiklik dock'a nasıl yansıyor

1. Her kontrol `SettingsStore` üzerinden `ConfigService.update` çağırır. Dosya
   hemen yazılır, alan adları Windows'la aynıdır.
2. `ConfigEvents.didChange` bildirimi gider.
3. `AppDelegate` config'in **yerleşim imzasını** (`AppConfig.layoutSignature`)
   son kurulan dock'unkiyle karşılaştırır. Farklıysa eski panel kapanır, yenisi
   kurulur. Replace/Show both değiştiyse sistem Dock'u gizlenir ya da geri yüklenir.

Yerleşim imzası, widget öğelerinin kendi ayarları (`settings`) **hariç** bütün
config'tir. Böylece su sayacına tıklamak gibi widget içi değişiklikler dock'u
yeniden kurmaz, titreme olmaz. Kenar, tema, sıra, varyant ve ad değişiklikleri
kurar. Ayrım `SettingsLogicTests` ile sınanıyor.

Kaydırıcılar (ton, kenar boşluğu) yalnız bırakıldığında yazar. Her adımda
yazmak her adımda dock'u yeniden kurmak demek olurdu.

**"Fit content" düzeltildi.** Eskiden sabit 420 pt'ydi. Artık dock içeriği
kurulmadan önce ölçülüyor (`NSHostingView.fittingSize`), ekrana sığmazsa ekran
genişliğiyle sınırlanıyor.

**Launch arguments artık uygulanıyor.** `DockItem.arguments` tıklamada hiç
kullanılmıyordu. Artık kabuk gibi bölünüp (`LaunchArguments.split`: boşluk,
tırnak, ters bölü) uygulama başlatılırken veriliyor. Windows'ta olduğu gibi
çalışan uygulamada etkisi yok.

## Doğrulama

```sh
# Ayar yollarını çalıştırıp dock'a yansımayı ölçer, sonunda config'i geri alır
DOCKHUB_HOME=/tmp/dh .build/DockHub.app/Contents/MacOS/DockHub --test-settings
# Her sayfanın görüntüsünü yazar
DOCKHUB_HOME=/tmp/dh .build/DockHub.app/Contents/MacOS/DockHub --snapshot-settings /tmp/dh/shots
```

`--snapshot-settings` pencereyi `cacheDisplay` ile çizer. Kenar çubuğunun
malzemesini ve metnini pencere sunucusu çizdiği için görüntüde kenar çubuğu
**boş** çıkar. Ekranda normal görünür. Pencere sunucusu görüntüsü
(`CGWindowListCreateImage`) SDK 27'de kaldırıldı, ScreenCaptureKit de Ekran
Kaydı izni istiyor.
