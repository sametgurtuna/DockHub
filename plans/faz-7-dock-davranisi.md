# Faz 7: Dock Davranışı

> Öncelik: 🟡 Orta · Boyut: L · Bağımlılık: Faz 4 (snapshot altyapısı profiller için yeniden kullanılır)

## Amaç

Dock'un farklı çalışma bağlamlarına uyum sağlaması ve çok öğeli kullanımda düzenli kalması.

---

## Görev 7.1: Profiller

1. **Model:** `AppConfig.Profiles: List<DockProfile>`, `ActiveProfileId`. `DockProfile` = ad + ikon + görünüm alanları + `Items`. Mevcut config "Varsayılan" profil olarak migrate edilir (`CurrentVersion` → 3, `ConfigService`'e v2 → v3 migration ve testi).
   - Alternatif (daha basit): profil yalnızca `Items` + birkaç görünüm alanını tutar, geri kalan ayarlar ortak kalır. **Önerilen budur**; hangi alanların profile özel olduğu `ProfileScopedAttribute` ile işaretlenir.
2. **Geçiş:** dock menüsü → "Profil" alt menüsü; Faz 3.2'ye `switch-profile-next` kısayolu; profil geçişi geri alınabilir.
3. **Otomatik kurallar** (Ayarlar → Profiller sayfası):
   - Ekran sayısı değişince (ör. dizüstü tek başına → "Mobil", harici monitör bağlı → "Masa").
   - Belirli bir süreç ön plandayken veya tam ekrandayken (ör. `steam.exe` → "Oyun": auto-hide açık, widget'lar kapalı).
   - Saat aralığı (09:00-18:00 → "İş").
   - Kurallar sırayla değerlendirilir, ilk eşleşen kazanır; elle geçiş kural motorunu bir sonraki tetikleyiciye kadar askıya alır.
4. Widget verileri (notlar, hatırlatıcılar) öğe kimliğine bağlı olduğundan profiller arasında aynı widget kopyalanırsa yeni kimlik alır; "bağlı kopya" özelliği bu fazda yok.

## Görev 7.2: Akıllı otomatik gizleme (intellihide)

1. `AutoHide` bool'dan enum'a: `Off` / `Always` / `Smart`. Config migration: `true` → `Always`.
2. `Smart`: dock yalnızca ön plandaki pencere dock'un dikdörtgeniyle kesiştiğinde gizlenir.
   - `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)` + ön plan penceresi için `EVENT_OBJECT_LOCATIONCHANGE` (yalnızca o hwnd'ye filtreli), 100 ms debounce.
   - Pencere sınırları için `DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)` (gölge payı hariç).
   - Masaüstü, dock'un kendi pencereleri ve tool window'lar yok sayılır.
3. `Smart` modunda `SpaceReserver` AppBar alanı ayırmaz (maximize edilen pencereler tam ekranı kullanır ve dock gizlenir).
4. Ayarlar ve dock menüsündeki "Auto-hide" onay kutusu üç seçenekli alt menüye dönüşür.

## Görev 7.3: Klasörlerde grid görünümü

1. Klasör ayarı: `GroupLayout`: `Auto` (varsayılan: 6 öğeye kadar fan, üstünde grid) / `Fan` / `Grid`.
2. Grid popup: Launchpad tarzı; 4-6 sütun, ikon + ad, üstte klasör adı (yerinde yeniden adlandırılabilir) ve 8 öğeden fazlaysa arama kutusu.
3. Grid içinde sürükle-bırak ile sıralama ve dışarı sürükleyerek dock'a taşıma (mevcut `DockDragHelper` altyapısı).
4. Açılış animasyonu mevcut genie/zoom yardımcılarını kullanır ve Faz 6.3'teki hareket seviyesine uyar.

## Görev 7.4: Çoklu pencere göstergesi

Ayar: Appearance → "Çalışan uygulama göstergesi": `Çizgi` (bugünkü, genişlik pencere sayısına göre) / `Noktalar` (1-3 nokta, macOS tarzı) / `Kapalı`. `AppButton.Refresh`'teki gösterge mantığı bu ayara göre çizer.

## Görev 7.5: Pencere önizlemesi iyileştirmeleri

1. Önizleme kartında pencere başlığı uzun ise kayan (marquee) değil, ortadan kısaltma.
2. Kart üzerinde tekerlek: pencereler arasında vurgu gezdirme; `Enter` öne getirir.
3. Önizleme üzerinde bir karta gelince o pencerenin ekrandaki yeri "peek" ile gösterilir (`DwmpActivateLivePreview` belgelenmemiş API; kullanılmazsa diğer pencerelerin geçici olarak saydamlaştırılması ile basit bir sürüm). Ayar ile açılır, varsayılan kapalı.

## Test planı

1. Profil: iki profil oluştur, elle geçiş, ekran bağlama/çıkarma kuralı, süreç kuralı; geçiş sonrası Undo.
2. Intellihide: maximize, pencereyi dock'un üstüne sürükleme, sanal masaüstü değişimi, tam ekran oyun.
3. Klasör grid: 12 öğeli klasör, arama, sürükle-bırak.
4. v2 config'in v3'e migration testi (Faz 2 test projesine).

## Commit önerisi

- `feat(profiles): switchable dock profiles with automatic rules`
- `feat(dock): smart auto-hide that only hides when a window overlaps`
- `feat(groups): grid layout for large folders`
- `feat(dock): running indicator styles`

## Uygulama notları


### 2026-09-26 — tamamlandı (kapsam notlarıyla)

- **7.1 Profiller:** `ProfileService` eklendi (öğeler ve görünüm). Aktif profil canlı config'te, diğerleri JSON olarak tutuluyor. Ekran sayısı kuralı (`AutoDisplayCount`) var; süreç ve saat kuralları yapılmadı. Yönetim Ayarlar › General › Profiles'ta, geçiş dock menüsünde (2+ profil varken) ve `next-profile` kısayoluyla yapılıyor.
  - Plandan sapma: profil geçişi undo geçmişine yazılmıyor. Geri alındığında aktif profil kimliğiyle öğeler senkron dışı kalıp veri kaybına yol açabiliyordu; geri dönmek için önceki profile tekrar geçmek yeterli. v2 → v3 şema değişikliğine gerek kalmadı (yeni alanlar eklendi, eskileri korundu).
- **7.2 Akıllı otomatik gizleme:** `SmartAutoHide` (bool, AutoHide'a bağlı). Ön plan ve konum WinEvent'leri dinleniyor (yalnızca etkin pencere için, 120 ms debounce) ve `DWMWA_EXTENDED_FRAME_BOUNDS` ile dock alanıyla kesişim hesaplanıyor. Plandaki enum yerine bool kullanıldı, config migration'a gerek kalmadı.
- **7.3 Klasör:** 8'den fazla öğede arama kutusu, 12'den fazlada 6 sütun, kaydırılabilir alan. Klasör içindeki uygulamalar artık "sabitli" sayılıyor, sonda tekrar görünmüyor (Faz 0 bulgusu). Klasör çalışan uygulama göstergesi eklendi; çalışan klasör uygulamasına tıklamak onu öne getiriyor. Boş klasördeki "Add application" düğmesi o klasörü hedefliyor.
- **7.4** Çalışan uygulama göstergesi: Çizgi / Noktalar / Kapalı.
- **7.5 Önizleme iyileştirmeleri:** yapılmadı (peek ve tekerlekle gezinme).
- Testler: 61.
