# Faz 9: Performans ve Kod Sağlığı

> Öncelik: 🟢 Düşük · Boyut: M · Bağımlılık: Faz 2 (testler, refactor'ların güvencesi)

## Amaç

"Hafif" ürün ilkesini ölçülebilir kılmak, gereksiz polling'i olay tabanlı yapıya çevirmek ve büyüyen dosyaları bakımı kolay parçalara bölmek.

---

## Görev 9.1: Ölçüm temeli

Refactor'dan **önce** ölçülür ve "Uygulama notları"na yazılır:

| Metrik | Araç | Senaryo |
|---|---|---|
| Boşta CPU | `dotnet-counters monitor --process-id <pid> System.Runtime` | 10 dk, varsayılan widget'lar, hiçbir etkileşim yok |
| Uyanma sayısı | Windows Performance Recorder (CPU Usage, Precise) veya `powercfg /energy` | 60 s boşta |
| Bellek (working set, GC heap) | `dotnet-counters` | açılıştan 1 dk sonra ve 8 saat sonra |
| Açılış süresi | `Log` zaman damgaları (`OnStartup` başı → ilk dock render) | soğuk ve sıcak açılış |
| Pil etkisi | `powercfg /srumutil` | dizüstünde 1 saat |

Hedefler: boşta CPU ortalaması < %0.1, saniyede < 5 uyanma, 8 saatte bellek artışı < %10.

## Görev 9.2: Polling'i olay tabanlıya çevirme

Kodda 37 `DispatcherTimer` var. Öncelikli olanlar:

| Yer | Bugün | Hedef |
|---|---|---|
| `TaskbarController` ([Shell/TaskbarController.cs:39](../src/CustomDock/Shell/TaskbarController.cs)) | 250 ms'de bir Explorer görev çubuğunu kontrol | `EVENT_OBJECT_SHOW` + `EVENT_OBJECT_LOCATIONCHANGE` hook'u yalnızca `Shell_TrayWnd`/`Shell_SecondaryTrayWnd` için; 5 s'lik güvenlik zamanlayıcısı |
| `WindowPreviewWindow` monitor | 50 ms | Yalnızca önizleme açıkken çalışır (zaten öyle); `MouseLeave` + global mouse hook (`GlobalPopupDismissHook` zaten var) ile değiştirilir |
| `DockWindow` topmost timer | 2 s | `EVENT_SYSTEM_FOREGROUND` ile yalnızca ön plan değişiminde topmost yeniden uygulanır |
| `DockWindow` display filter timer | 1 s | Faz 0.5'teki `WindowVisibilityWatcher` + `EVENT_OBJECT_LOCATIONCHANGE` (yalnızca çok monitörlü modda) |
| `NetworkMonitorService` | 1 s | Yalnızca Network widget'ı görünürken çalışır (kontrol et); dock auto-hide ile gizliyken durur |
| `SystemMonitorService` | 2 s | Aynı: görünür değilken durur |

Genel kural: dock gizliyken (auto-hide, tam ekran) görsel güncelleme yapan tüm zamanlayıcılar duraklatılır. `WidgetBase`'e `IsOnScreen` ve `OnScreenChanged` eklenir.

## Görev 9.3: Büyük dosyaların bölünmesi

| Dosya | Satır | Önerilen bölünme |
|---|---|---|
| [Dock/GroupItemView.cs](../src/CustomDock/Dock/GroupItemView.cs) | 818 | `GroupItemView` (dock karosu), `GroupFanPopup` (açılan fan), `GroupContextMenu`, `GroupAccentPalette` |
| [Dock/AppButton.cs](../src/CustomDock/Dock/AppButton.cs) | 629 | `AppButton` (görsel + tıklama), `AppButtonBadge` (rozet ayrıştırma, regex), `AppButtonMenu` (bağlam menüsü + Jump List), `AppButtonDropTarget` (dosya sürükleme) |
| [Dock/WindowPreviewWindow.cs](../src/CustomDock/Dock/WindowPreviewWindow.cs) | 530 | `WindowPreviewWindow`, `PreviewCard`, `DwmThumbnailHost` |
| [Dock/DockWindow.xaml.cs](../src/CustomDock/Dock/DockWindow.xaml.cs) | 531 | Zaten partial; menü oluşturma `DockWindow.Menus.cs`'e, hotkey `HotkeyService`'e (Faz 3.2) |
| [Native/NativeMethods.cs](../src/CustomDock/Native/NativeMethods.cs) | 586 | Alana göre partial: `NativeMethods.Window.cs`, `.Shell.cs`, `.Dwm.cs`, `.Input.cs` |

Kural: bölme commit'leri davranış değiştirmez; yalnızca taşıma yapılır, ardından ayrı commit'lerde iyileştirilir.

## Görev 9.4: Kaynak sızıntıları

1. Olay aboneliklerinin (özellikle `AppGroup.PropertyChanged`, `AppServices.*` olayları, `SystemEvents`) `Detach`/`Unloaded`'da kaldırıldığını denetle. `SystemEvents` statik olduğu için abonelik unutulursa widget örnekleri hiç toplanmaz.
2. 8 saatlik test: 50 kez widget ekle/kaldır, 50 kez ayar değiştir; bellek artışı ölçülür. Artış varsa `dotnet-gcdump` ile kök yolu bulunur.
3. `ShellIcons.IconCache` LRU (Faz 0.2.5'te yapılmadıysa).
4. `WeatherService` statik `HttpClient` doğru; diğer ağ servisleri (Faz 4.4, 8.x) aynı deseni kullanır, `SocketsHttpHandler.PooledConnectionLifetime` ayarlanır.

## Görev 9.5: Açılış süresi

1. ReadyToRun zaten installer build'inde var; `TieredPGO` ve `TieredCompilation` ayarları ölçülerek denenir.
2. Widget'lar görünür olana kadar (kaydırma dışı) servislerini başlatmaz.
3. Hava durumu, AI usage, cihaz pili gibi ilk veri çekimleri açılıştan 3-10 s sonraya yayılır (hepsi aynı anda değil).

## Test planı

Görev 9.1'deki ölçümler refactor sonrası tekrarlanır ve karşılaştırma tablosu notlara yazılır. Faz 2 testleri yeşil kalmalı. Görev çubuğu geri gelme senaryoları (README kuralı 1) tekrar denenir.

## Commit önerisi

- `perf(taskbar): replace 250ms polling with win event hooks`
- `perf(widgets): pause updates while the dock is hidden`
- `refactor(dock): split GroupItemView, AppButton and preview window`
- `fix: unsubscribe static event handlers on widget detach`

## Uygulama notları

_(Önce/sonra ölçüm tablosu buraya.)_
