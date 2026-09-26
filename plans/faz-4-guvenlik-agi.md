# Faz 4: Güvenlik Ağı

> Öncelik: 🟠 Yüksek · Boyut: L · Bağımlılık: Faz 1 (ConfirmDialog, data trash), Faz 2 (testler)

## Amaç

Kullanıcının dock'u rahatça kurcalayabilmesi için hataları geri alınabilir yapmak, ayarları yedeklenebilir kılmak ve güncellemelerin kullanıcıya ulaşmasını sağlamak.

---

## Görev 4.1: Geri alma (Undo)

### Model
- `Core/ConfigHistory.cs`: `Items` listesinin JSON snapshot'larından oluşan bir yığın (en fazla 20). Snapshot'ın içeriği:
  - `Items` (klasör çocukları dahil derin kopya, `JsonSerializer` ile)
  - Etkilenen öğelerin `_itemSettings` cache'i (ConfigService zaten `DockItem.Settings`'e yazıyor, bu yüzden Items snapshot'ı yeterli olmalı; doğrula)
  - Trash'e taşınan data dosyalarının listesi (Faz 1.4)
  - Kullanıcıya gösterilecek açıklama: "Not widget'ı kaldırıldı"
- `ConfigService`'teki her mutasyon metodu (`AddItem`, `RemoveItem`, `MoveItem`, `ReplaceItems`, `AddToGroup`, `CreateGroupFromItems`, `UngroupAll`, varyant değişimi, klasör adı/rengi değişimi) başında `History.Push(description)` çağırır.
- Sürükle-bırak sırasında ara adımlar tek bir snapshot'a birleştirilir (`using (History.Batch("Taşındı"))`).
- `Undo()`: son snapshot'ı geri yükler, trash'teki dosyaları geri taşır, `NotifyItemsChanged` çağırır.
- `Redo` bu fazda yok (gereksiz karmaşıklık).

### Arayüz
- `Dock/UndoToast.cs`: dock'un kenarına yapışık, dock temasıyla uyumlu küçük bir popup: "Not widget'ı kaldırıldı · **Geri al**". 6 s sonra kaybolur, üzerine gelince süre durur.
- Toast yalnızca **yıkıcı** işlemlerde (kaldırma, klasör silme, ungroup, preset uygulama) gösterilir; taşıma ve ekleme işlemleri sessizce geçmişe yazılır.
- Klavye: Ayarlar penceresi odaktayken `Ctrl+Z`. Dock menüsüne "Geri al: <açıklama>" öğesi (geçmiş boş değilse).
- Faz 1.2'deki klasör silme diyaloğu sadeleştirilir: "Hepsini sil" seçeneği artık diyalogsuz çalışır ve toast gösterir. Diyalog yalnızca "Hepsini sil" ile "İçindekileri taşı" arasında seçim için kalır.

**Kabul:** Widget sil → Geri al → widget aynı yerde, ayarları ve not metniyle geri gelir. Klasör sil → Geri al → klasör, çocukları ve rengiyle geri gelir.

---

## Görev 4.2: Yedekleme, dışa ve içe aktarma

1. **Dışa aktar:** Ayarlar → General → "Yedekle ve geri yükle" → "Dışa aktar…" → `DockHub-backup-<tarih>.zip`:
   - `config.json`
   - `data/*.json` (`weather-cache.json` hariç, `trash/` hariç)
   - `manifest.json`: `{ "app": "DockHub", "version": "0.7.0", "configVersion": 2, "createdAt": "..." }`
2. **İçe aktar:** zip seçilir → manifest doğrulanır (farklı uygulama, daha yeni `configVersion` → hata mesajı) → `ConfirmDialog`: "Mevcut ayarlarınız yedeklenip değiştirilecek" → mevcut durum otomatik yedeklenir → dosyalar yazılır → uygulama yeniden başlar.
3. **Otomatik yedek:** Her gün ilk kaydetmede `backups/config-<yyyyMMdd>.json`. Son 7 tanesi tutulur. Bozuk config tespit edildiğinde (`ConfigService.ReadConfig` catch bloğu) en son sağlam otomatik yedek önerilir: "Ayarlar okunamadı. <tarih> tarihli yedeği geri yüklemek ister misiniz?"
4. README "Configuration and data" tablosuna `backups/` eklenir.

---

## Görev 4.3: Hazır düzenler (presetler)

1. `Assets/Presets/*.json` (EmbeddedResource): her preset bir `AppConfig` alt kümesi (görünüm alanları + `Items`).
   - **Minimal:** Fit genişlik, Floating, Small, yalnızca pinler + saat.
   - **macOS tarzı:** Fit, ortalı, Medium, Blur, tray gizli, saat ve hava durumu sağda pinli.
   - **Bilgi paneli:** Full, Attached, Large; takvim saati, hava durumu, sistem, medya, hatırlatıcılar.
   - **Dikey:** sol kenar, compact tile'lar.
2. Preset'teki uygulama öğeleri kullanıcının makinesinde olmayabilir: uygulama öğeleri preset'ten alınmaz, kullanıcının mevcut pinleri korunur. Preset yalnızca görünüm ve widget'ları belirler.
3. Ayarlar → Appearance'ın en üstüne "Hazır düzenler" şeridi: her preset için küçük bir önizleme kartı (Faz 5.3'teki mini dock önizlemesi kullanılabilir; o faz yapılmadıysa statik görsel) + "Uygula". Uygulama geri alınabilir (4.1).
4. "Mevcut düzenimi preset olarak kaydet" (yerel, `presets/` klasörüne) opsiyonel; zaman kalırsa.

---

## Görev 4.4: Güncelleme kontrolü

1. `Services/UpdateService.cs`:
   - `GET https://api.github.com/repos/sametgurtuna/DockHub/releases/latest` (User-Agent başlığı zorunlu), günde en fazla bir kez, açılıştan 2 dk sonra.
   - `tag_name` (ör. `v0.7.0`) `Version` ile karşılaştırılır. Taslak (draft) ve pre-release'ler yok sayılır (ayar ile dahil edilebilir).
   - Sonuç `data/update-state.json`'da tutulur (son kontrol zamanı, atlanan sürüm).
2. Yeni sürüm varsa:
   - Tek seferlik bildirim (toast): "DockHub 0.7.0 hazır" · **İndir** · **Bu sürümü atla**.
   - Ayarlar → About'ta kalıcı bir şerit: sürüm, release notlarının ilk 5 satırı, "İndir ve kur".
3. "İndir ve kur": installer asset'i `%TEMP%`'e indirilir (ilerleme göstergesiyle), SHA256 değeri release notlarında yayımlanıyorsa doğrulanır, installer `/SILENT` ile başlatılır. Installer mevcut `--exit` akışıyla DockHub'ı kapatıp görev çubuğunu geri getirir, kurulum bitince DockHub yeniden başlar (`DockHub.iss`'e `[Run]` bölümünde `postinstall` + `skipifnotsilent` bayrağı kontrol edilir).
4. Ayar: General → "Güncellemeleri otomatik kontrol et" (varsayılan açık). README gizlilik bölümüne `api.github.com` isteği eklenir.
5. Faz 2'deki release iş akışı SHA256'yı release notlarına ekleyecek şekilde güncellenir.

## Test planı

1. Undo: tüm yıkıcı işlemler için sil → geri al; 21 işlemden sonra en eskinin düştüğü; uygulama yeniden başlayınca geçmişin sıfırlandığı.
2. Yedek: dışa aktar → config'i boz → içe aktar → her şey geri gelir.
3. Otomatik yedek: `config.json` elle bozulur → açılışta geri yükleme önerisi.
4. Güncelleme: `Version` geçici olarak `0.0.1` yapılarak bildirim tetiklenir; "atla" sonrası tekrar gösterilmez; ağ yokken sessizce geçer.

## Commit önerisi

- `feat(config): undo for destructive dock changes`
- `feat(settings): backup, restore and daily automatic config backups`
- `feat(appearance): built-in layout presets`
- `feat(update): check GitHub releases and offer one-click update`

## Uygulama notları


### 2026-09-26 — tamamlandı

- **Undo:** `ConfigHistory` (20 adım) eklendi. Geri yüklemede mevcut `DockItem` örnekleri id'ye göre yeniden kullanılıyor; view'lar ve widget ayarları bu örneklere referans tuttuğu için bu gerekli. Silinen öğelerin trash'teki verileri de geri geliyor. Yıkıcı işlemlerde dock'un yanında `UndoToast` çıkıyor (6 s, üzerine gelince duruyor). Dock menüsünde "Undo: …" var, Ayarlar'da Ctrl+Z çalışıyor.
- **Yedek:** zip dışa/içe aktarma (manifest ile doğrulanıyor, içe aktarmadan önce mevcut durum `backups/` klasörüne yedekleniyor, yeniden başlatmada config üzerine yazılmasın diye `DisableSaving` kullanılıyor), günlük `config-yyyyMMdd.json` (7 adet). Bozuk config tespit edilirse en yeni sağlam yedek otomatik yükleniyor ve kullanıcıya bildirim gösteriliyor. Plandaki "önerme diyaloğu" yerine otomatik yükleme seçildi, çünkü açılış sırasında UI henüz hazır değil.
- **Presetler:** Minimal, macOS style, Dashboard, Side bar. Uygulamalar, klasörler ve ayraçlar korunuyor; aynı türdeki mevcut widget'lar (ayarları ve verileriyle) yeniden kullanılıyor. Görünüm ayarları dahil tek adımda geri alınabiliyor. "Mevcut düzeni preset olarak kaydet" yapılmadı (opsiyoneldi).
- **Güncelleme:** `UpdateService` (GitHub latest release, günde bir, pre-release seçeneği). Doğrulama için GitHub'ın asset `digest` alanı (sha256) kullanılıyor, yoksa SHA256SUMS'a düşülüyor. Installer `/SILENT` ile çalıştırılıyor; `DockHub.iss` sessiz kurulumdan sonra uygulamayı yeniden başlatıyor. Toast'ta "Details" ve "Skip this version" var; About sayfasına güncelleme kartı eklendi.
- Testler: 56 (undo ve yedekleme testleri dahil).
