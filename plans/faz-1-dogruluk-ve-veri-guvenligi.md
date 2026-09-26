# Faz 1: Doğruluk Hataları ve Veri Güvenliği

> Öncelik: 🔴 Kritik · Boyut: M · Bağımlılık: Faz 0 (tavsiye edilir, zorunlu değil)

## Amaç

İncelemede bulunan mantık hatalarını düzeltmek, kullanıcı verisinin sessizce kaybolmasını önlemek ve dokümantasyonu kodla tutarlı hale getirmek.

## Kapsam dışı

- Genel geri alma (Undo) sistemi Faz 4'te. Bu fazdaki onay diyalogları o zamana kadar güvenlik ağıdır; Faz 4'te bazıları toast + geri al ile değiştirilir.

---

## Görev 1.1: Klasördeki "Add application…" uygulamayı klasöre eklemeli

**Hata:** [Dock/GroupItemView.cs:735](../src/CustomDock/Dock/GroupItemView.cs) yalnızca `App.Instance.ShowAppPicker()` çağırıyor. [Settings/AppPickerWindow.xaml.cs:170](../src/CustomDock/Settings/AppPickerWindow.xaml.cs) seçilen uygulamayı her zaman `DockItemsIndex.EndOfApps()` konumuna, yani dock'a ekliyor.

1. `AppPickerWindow` constructor'ına `string? targetGroupId = null` parametresi ekle.
   - Hedef varsa pencere başlığı: "Add to “<klasör adı>”".
   - Ekleme: `AppServices.ConfigService.AddToGroup(targetGroupId, item)`.
   - Aynı uygulama klasörde zaten varsa (`AppKeys.ForItem` eşitliği) eklenmez ve pencerede bilgi satırı gösterilir.
2. `App.ShowAppPicker(string? targetGroupId = null)`. Açık bir picker farklı bir hedefle tekrar istenirse hedef güncellenir ve başlık yenilenir.
3. `GroupItemView` menüsü `ShowAppPicker(_item.Id)` çağırır.
4. Picker'da çoklu seçim varsa (kontrol et) tüm seçilenler klasöre eklenir.

**Kabul:** Klasör menüsünden eklenen uygulama klasörün içinde görünür, dock'un ana sırasına eklenmez.

---

## Görev 1.2: Klasör silmede veri kaybını önleme

**Hata:** "Remove folder" ([Dock/GroupItemView.cs:744](../src/CustomDock/Dock/GroupItemView.cs)) klasörü içindeki tüm uygulama ve widget'larla birlikte, onay sormadan siler.

1. Menü öğesi yalnızca klasör boşsa doğrudan siler.
2. Doluysa, `RenameFolderDialog` ([Dock/RenameFolderDialog.cs](../src/CustomDock/Dock/RenameFolderDialog.cs)) stilinde yeni `ConfirmDialog` (tema uyumlu, `Dock/ConfirmDialog.cs`) açılır:
   - Başlık: "“<ad>” klasörünü kaldır"
   - Metin: "Klasörde N öğe var."
   - Butonlar: **İçindekileri dock'a taşı** (varsayılan, `UngroupAll` + klasörü sil), **Hepsini sil** (yıkıcı stil, kırmızı), **İptal**.
3. `ConfirmDialog` yeniden kullanılabilir tasarlanır: `ConfirmDialog.Show(owner, title, message, params DialogButton[])` → seçilen butonun kimliğini döner. Görev 1.3 ve Faz 4 de bunu kullanır.
4. Ayarlar → Dock items sayfasındaki silme ([Settings/SettingsWindow.Items.cs:294](../src/CustomDock/Settings/SettingsWindow.Items.cs)) için de aynı kural geçerli.

**Kabul:** Dolu bir klasör tek tıkla silinemez; varsayılan seçenek içindekileri korur.

---

## Görev 1.3: "Hide Windows taskbar" onayı

**Hata:** Dock menüsündeki "Hide Windows taskbar" ([Dock/DockWindow.xaml.cs:459](../src/CustomDock/Dock/DockWindow.xaml.cs)) `TaskbarMode`'u değiştirir, `App.OnConfigChanged` da uygulamayı hemen yeniden başlatır.

1. Menü öğesine tıklanınca `ConfirmDialog`: "DockHub yeniden başlatılacak. Windows görev çubuğu [gizlenecek/geri gelecek]." · **Yeniden başlat** / **İptal**.
2. Ayarlar sayfasındaki segmentli kontrol zaten bir uyarı kartı gösteriyor; orada da değişiklik anında değil, onayla uygulanır (radyo butonu değişince diyalog; iptal edilirse eski değere döner).

---

## Görev 1.4: Silinen öğelerin artık verileri

**Hata:** Not widget'ı silinince `data/notes-<id>.json` diskte kalıyor. `ConfigService.RemoveItem` klasör silinirken çocukların `_itemSettings` cache girdilerini temizlemiyor.

1. `ConfigService.RemoveItem` klasör silerken çocukları da recursive gezip `_itemSettings.Remove(child.Id)` yapar.
2. Yeni `ItemDataStore` (Core): bir öğenin data dosyalarını bulur. Kural: `data/<widgetId>-<itemId>.json` (bkz. `WidgetBase.StateKey`, [Widgets/WidgetBase.cs:83](../src/CustomDock/Widgets/WidgetBase.cs)).
3. Silinen öğenin dosyaları hemen silinmez, `data/trash/<yyyyMMddHHmmss>-<dosya>` konumuna taşınır (Faz 4'teki Undo'nun geri yükleyebilmesi için).
4. Açılışta `data/trash` içindeki 7 günden eski dosyalar silinir.
5. README'deki "Configuration and data" tablosuna `data/trash` satırı eklenir.

---

## Görev 1.5: Bilinmeyen widget'ların sessizce silinmesi

**Hata:** [Core/ConfigService.cs:44](../src/CustomDock/Core/ConfigService.cs) kayıtlı olmayan widget'ları config'den kalıcı olarak siliyor (yalnızca üst seviyede; klasör içindekiler kalıyor). Eski bir sürüme dönen kullanıcı, yeni sürümde eklediği widget'ları ve ayarlarını kaybediyor.

1. `RemoveAll` satırı kaldırılır. Bilinmeyen widget'lar config'de kalır.
2. Görünüm tarafı zaten atlıyor (`DockWindow.Items.cs:122`, `WidgetRegistry.Find(...) is { } descriptor`). Klasör içi gösterimde (`GroupItemView.cs:621`) de atlandığından emin olunur.
3. Ayarlar → Dock items listesinde bu öğeler soluk renkte, "Bu sürümde desteklenmiyor" etiketiyle ve yalnızca "Kaldır" seçeneğiyle gösterilir.

---

## Görev 1.6: Arayüz düzeltmeleri

1. **Glyph:** Dock menüsündeki "Windows Settings" öğesinin ikonu ham karakter olarak yazılmış ([Dock/DockWindow.xaml.cs:456](../src/CustomDock/Dock/DockWindow.xaml.cs)). Diğerleri gibi `` (System) escape'iyle yazılır.
2. **Edge margin:** `Layout == Attached` iken "Edge margin" satırı ([Settings/SettingsWindow.xaml:306](../src/CustomDock/Settings/SettingsWindow.xaml)) devre dışı kalır ve açıklamasına "Yalnızca Floating şeklinde geçerli" yazılır. `EnumEq` converter'ının tersini veren bir `EnumNotEq` converter'ı eklenir ya da `DataTrigger` kullanılır. Aynı kontrol diğer bağımlı ayarlarda da yapılır (ör. `ShowTray` → "Always visible icons" kartı, `Replace` modu dışında).
3. Dock menüsündeki iki "Quick settings" kaynağı (saat menüsü ve dock menüsü) aynı davranmalı: dock menüsündeki `UpdateTrayHost()` çağrısı saat menüsünde de yapılır.

---

## Görev 1.7: AI Usage servisinin sağlamlaştırılması

Dosya: [Services/AIUsageService.cs](../src/CustomDock/Services/AIUsageService.cs)

1. **Hata türleri:** `Error` string yerine `AIUsageStatus` enum'u: `Ok`, `CliNotFound`, `NotLoggedIn` (çıktıda "login"/"authenticate" geçiyorsa), `Timeout`, `ParseFailed`, `Unknown`. Widget her durum için anlaşılır bir metin gösterir (UI kısmı Faz 5'te; burada veri modeli).
2. **Ayarlanabilir aralık:** Widget ayarlarına `RefreshMinutes` (5/15/30/60, varsayılan 15). Servis tek örnek olduğundan, abone olan widget'ların en küçük değeri kullanılır.
3. **Elle yenileme:** Widget bağlam menüsüne "Şimdi yenile".
4. **Parse sağlamlığı:** `double.Parse` → `double.TryParse(..., CultureInfo.InvariantCulture)`. `Parse` metodu `internal static` yapılır (Faz 2'de testlenecek). Bilinen örnek çıktılar `tests/fixtures/ai-usage/*.txt` olarak saklanmak üzere bu fazda toplanır.
5. **Süreç kapanışı:** Timeout'ta `Kill(true)` sonrası stdout/stderr task'larının da beklenmesi, `Dispose` edilmesi.

---

## Görev 1.8: Dokümantasyon tutarlılığı

1. **README › The dock › Global hotkey:** "configurable in Settings › General" ifadesi Faz 3 bitene kadar "Win+Alt+D (falls back to Ctrl+Alt+D)" olarak düzeltilir.
2. **README › Permissions and privacy › Network:** AI Usage widget'ı eklendiğinde yerel `claude` CLI'ının her N dakikada bir çalıştırıldığı ve bu CLI'ın Anthropic sunucularına bağlandığı eklenir.
3. **PRODUCT.md › Known constraints:** "dock is displayed on a single monitor" ifadesi kaldırılır; "Network access only used for weather" ifadesi AI Usage ile güncellenir. (PRODUCT.md git'e dahil değil, yalnızca yerelde güncellenir.)
4. **README › Known limitations:** Faz 0'daki tanılama betiği ve "Tanılama bilgisini kopyala" komutu anlatılır.

---

## Test planı

1. Görev 1.1-1.7'nin kabul kriterleri.
2. Eski bir `config.json`'a bilinmeyen `"widget": "future-widget"` öğesi elle eklenir: uygulama açılır, öğe config'de kalır, ayarlarda "desteklenmiyor" olarak görünür.
3. Not widget'ı silinir: `data/notes-<id>.json` dosyası `data/trash` altına taşınır.
4. Dolu klasör silme: üç seçeneğin üçü de denenir.

## Commit önerisi

- `fix(groups): add apps from a folder's menu into that folder`
- `fix(groups): confirm before deleting a non-empty folder`
- `fix(dock): confirm before restarting to switch taskbar mode`
- `fix(config): keep unknown widgets and move removed item data to trash`
- `fix(settings): disable edge margin for attached docks, fix menu glyph`
- `refactor(ai-usage): typed status, configurable interval, manual refresh`
- `docs: align README with actual hotkey, privacy and multi-monitor behavior`

## Uygulama notları

_(Faz uygulanırken doldurulacak.)_
