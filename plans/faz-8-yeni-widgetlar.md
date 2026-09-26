# Faz 8: Yeni Widget'lar

> Öncelik: 🟢 Düşük · Boyut: XL (widget başına S-M) · Bağımlılık: Faz 5.4 (genişlik standartları), Faz 6.6 (dizeler baştan resx'e)

## Amaç

Mevcut widget altyapısıyla (`WidgetBase`, `WidgetRegistry`, `CompactTile`, ayar şablonları) kullanıcı değeri yüksek yeni widget'lar eklemek. Her widget bağımsız bir alt görevdir, sırası değiştirilebilir.

## Her widget için ortak kontrol listesi

- [ ] `Widgets/<Ad>/<Ad>Widget.xaml(.cs)`, `Layout_<variant>` düzenleri
- [ ] `WidgetRegistry`'de descriptor: kategori, ikon yolu (24×24), accent, varyantlar ve genişlik sınıfları (Faz 5.4)
- [ ] Ayar sınıfı + `WidgetSettingsTemplates.xaml` şablonu veya özel ayar görünümü
- [ ] Dikey dock için `CompactTile` karşılığı
- [ ] Galeri önizlemesi (`IsPreview` iken sahte veri)
- [ ] Boş durum (`IsIdle`, Faz 5.5) ve hata durumu
- [ ] Erişilebilir özet (`AccessibleSummary`, Faz 6.1)
- [ ] Ağ kullanıyorsa: README gizlilik bölümü, önbellek (`data/<ad>-cache.json`), hizalı yenileme, çevrimdışı davranış
- [ ] README widget tablosu ve sayısı ("19 widgets") güncellenir
- [ ] Servis mantığı için unit test (Faz 2 projesi)

---

## Görev 8.1: Takvim / sıradaki toplantı

- **Kaynak:** ICS abonelik URL'leri (Google Takvim "gizli iCal adresi", Outlook "takvimi yayımla"). OAuth yok, API anahtarı yok.
- **Varyantlar:** `Next` (sıradaki etkinlik + kalan süre), `Agenda` (bugünün listesi), `Compact` (yalnızca "14:30'da").
- **Özellikler:** Etkinlik açıklamasında Teams/Meet/Zoom bağlantısı varsa "Katıl" butonu; 5 dk kala bildirim (ayarlanabilir); tüm gün etkinlikleri ayrı.
- **Teknik:** ICS ayrıştırma için `Ical.Net` paketi (MIT); tekrarlayan etkinlikler (RRULE) ve saat dilimleri bu paketle çözülür. 15 dk'da bir yenileme.
- Mevcut Clock "Calendar" varyantı bu servisten de beslenebilir (sıradaki hatırlatıcının yanında sıradaki etkinlik).

## Görev 8.2: Pano geçmişi

- **Kaynak:** `AddClipboardFormatListener`. Metin, bağlantı ve görsel (küçük önizleme).
- **Gizlilik:** Varsayılan olarak yalnızca bellekte (son 25 öğe). Diske yazma ayarla açılır. `ExcludeClipboardContentFromMonitorProcessing` ve `CanIncludeInClipboardHistory = 0` formatlarını taşıyan içerik (parola yöneticileri) asla kaydedilmez.
- **Etkileşim:** Tık = panoya geri kopyala; sabitleme; arama; Faz 3.2'ye `clipboard-history` kısayolu.
- Windows'un Win+V geçmişiyle çakışmaz; README'de farkı açıklanır.

## Görev 8.3: Döviz ve kripto kuru

- **Kaynak:** Döviz için `api.frankfurter.app` (ECB, anahtarsız, günlük); kripto için CoinGecko public API (anahtarsız, hız sınırlı).
- **Varyantlar:** `Single` (USD/TRY 41,23 ▲0,4 %), `List` (3-5 çift), `Sparkline` (7 günlük).
- Ondalık ve para birimi biçimleri `CurrentCulture`'a göre.

## Görev 8.4: GPU kullanımı

- **Kaynak:** Performans sayaçları: `GPU Engine` kategorisi (`Utilization Percentage`, engtype_3D), `GPU Adapter Memory` (`Dedicated Usage`). Yönetici yetkisi gerekmez.
- **Sıcaklık:** Yönetici yetkisi veya sürücüye özel API gerektirir; bu fazda **yok**. README'de not edilir.
- Mevcut `SystemWidget`'a varyant olarak eklemek mi, ayrı widget mı? Önerilen: `SystemWidget`'a "GPU" satırı seçeneği (servis `SystemMonitorService`'e eklenir, aynı zamanlayıcıyı kullanır).

## Görev 8.5: Klasör yığını (Stack)

- **Kaynak:** Seçilen klasör (varsayılan İndirilenler), `FileSystemWatcher`.
- **Dock görünümü:** En son 3 dosyanın üst üste binmiş küçük ikonları.
- **Popup:** Fan (≤ 8) veya grid; sıralama (tarih/ad/tür); dosyaları dock'tan başka uygulamalara sürükleyebilme (`DataObject` + `FileDrop`); "Klasörü aç".
- Ağ sürücülerinde watcher hataları sessizce yakalanır, 60 s'de bir yeniden bağlanılır.

## Görev 8.6: AI Usage çoklu sağlayıcı

- **Önce araştırma:** Codex CLI, Gemini CLI ve Cursor'un kullanım/limit bilgisini güvenilir biçimde veren bir komut veya yerel dosya var mı? Sonuçlar bu dosyanın notlarına yazılır. Kararlı bir kaynak yoksa sağlayıcı eklenmez (Claude için yaşanan regex kırılganlığı tekrar edilmez).
- **Mimari:** `IAIUsageProvider` arayüzü (`Id`, `DisplayName`, `IsAvailable()`, `FetchAsync()`), mevcut Claude mantığı `ClaudeCodeUsageProvider`'a taşınır. Widget ayarında sağlayıcı seçimi; `Rings` varyantı birden fazla sağlayıcıyı yan yana gösterebilir.

## Test planı

Her widget için ortak kontrol listesinin tamamı + Small/Medium/Large ve dikey dock ekran görüntüleri.

## Commit önerisi

Widget başına bir commit: `feat(widgets): add <ad> widget`.

## Uygulama notları

_(Faz uygulanırken doldurulacak; hangi widget'ların yapıldığı işaretlenir.)_

| Widget | Durum |
|---|---|
| 8.1 Takvim | ⬜ |
| 8.2 Pano geçmişi | ⬜ |
| 8.3 Döviz/kripto | ⬜ |
| 8.4 GPU | ⬜ |
| 8.5 Klasör yığını | ⬜ |
| 8.6 AI Usage çoklu sağlayıcı | ⬜ |
