# Faz 5: UX Cilası

> Öncelik: 🟡 Orta · Boyut: L · Bağımlılık: Faz 1 (ConfirmDialog), Faz 4.3 (presetler, onboarding'de kullanılır)

## Amaç

Dock'u daha düzenli ve okunaklı hale getirmek, ayarlar penceresini keşfedilebilir kılmak ve ilk kullanım deneyimini "görev çubuğum nereye gitti?" sorusunu yanıtlayacak şekilde tasarlamak.

Tasarım yönergeleri: DockHub'ın mevcut Fluent/cam dili korunur. Yeni öğeler `Themes/Dark.xaml` ve `Light.xaml`'daki token'ları kullanır; yeni renk eklenecekse iki temaya birden eklenir.

---

## Görev 5.1: İlk açılış sihirbazı

`IsFirstRun` durumunda galeri yerine `Settings/WelcomeWindow` (Mica, 640×520, 4 adım):

1. **Hoş geldin:** "DockHub görev çubuğunuzun yerini alır. Kapatınca görev çubuğunuz her zaman geri gelir." Bir sonraki adıma geçmeden hiçbir şey değişmez.
2. **Mod:** iki büyük kart: "Görev çubuğunun yerini al" (önerilen) / "Görev çubuğuyla birlikte çalış". Kartlarda küçük şematik çizimler.
3. **Görünüm:** kenar (dört küçük ekran simgesi) + Faz 4.3 presetleri. Seçim anında gerçek dock'a uygulanır.
4. **Hazır:** üç ipucu kartı (sağ tık menüsü, widget ekleme, Win+1..9 / Win+Alt+D) + "Widget galerisini aç" / "Bitir".

- Sihirbaz kapatılırsa (X) varsayılanlarla devam edilir ve bir daha gösterilmez.
- Ayarlar → About → "Karşılama ekranını yeniden göster".

## Görev 5.2: Ayarlarda arama

1. Kenar çubuğunun üstüne arama kutusu (`Ctrl+F` ile odaklanır).
2. Arama dizini: her `SettingRow`'un `Header` + `Description` + bulunduğu sayfa + sayfa içindeki bölüm. Ek olarak her widget'ın adı ve açıklaması (galeri).
3. Sonuçlar kenar çubuğunun yerine liste olarak gösterilir: "Tint opacity · Appearance". Tıklanınca ilgili sayfaya gidilir, satır ekrana kaydırılır ve 1.2 s boyunca accent renkte vurgulanır.
4. `SettingRow`'a opsiyonel `Keywords` özelliği eklenir (ör. "Auto-hide" → "gizle, otomatik, hide").

## Görev 5.3: Canlı dock önizlemesi

Appearance sayfasının üst kısmında sabit (sticky) bir önizleme alanı: masaüstü duvar kağıdının bulanık küçük kopyası üzerinde ölçekli bir mini dock (gerçek `DockWindow` değil, aynı token'larla çizilmiş basit bir `MiniDockPreview` kontrolü). Tema, backdrop, tint, boyut, şekil, genişlik, hizalama ve kenar değiştikçe anında güncellenir. Duvar kağıdı `SystemParametersInfo(SPI_GETDESKWALLPAPER)` ile alınır; alınamazsa gradyan kullanılır.

## Görev 5.4: Dock düzeni ve widget genişlikleri

**Sorun:** Widget kartlarının genişlikleri ve iç düzenleri birbirinden çok farklı; dock kalabalık ve ritimsiz görünüyor (bkz. `docs/images/dock-overview.jpg`).

1. `Controls/WidgetCard.cs`'e standart genişlik sınıfları: `Compact` (1× yükseklik, kare), `Standard` (≈ 2.5×), `Wide` (≈ 4×). Değerler dock boyutuna (Small/Medium/Large) göre ölçeklenir.
2. Her widget'ın her varyantı bu sınıflardan birine eşlenir; `WidgetVariant` kaydına `Width` alanı eklenir. Denetim tablosu bu dosyanın "Uygulama notları"na yazılır (19 widget × varyant).
3. İç düzen kuralı: birincil değer (saat, sıcaklık, yüzde) solda ve büyük, ikincil etiket altında ve küçük; ikon veya halka en solda. Tüm widget'lar bu şablona göre gözden geçirilir.
4. Widget'lar arası boşluk tek bir token'dan (`DockItemSpacing`) okunur.

## Görev 5.5: Boş durumlar

1. `WidgetBase`'e `virtual bool IsIdle` ve `IdleChanged` olayı eklenir. `IsIdle` iken, ayar açıksa widget `Compact` genişliğe küçülür (yalnızca ikon), üzerine gelince tooltip gösterir.
   - Media: oturum yoksa (mevcut `HideWhenIdle` davranışı korunur; **yeni eklenen** Media widget'larında varsayılan açık).
   - Notes: not boşsa.
   - Reminders: bekleyen hatırlatıcı yoksa.
   - Countdown/Focus/Stopwatch: çalışmıyorsa (ayarla, varsayılan kapalı).
2. Widget bağlam menüsüne "Boştayken küçült" onay kutusu (tüm widget'lar için ortak, `DockItem` seviyesinde `CollapseWhenIdle`).

## Görev 5.6: Kaydırma okları

**Sorun:** Taşan dock'ta `< >` okları tray'in hemen yanında, dock'un ortasında duruyor ve hangi alanı kaydırdıkları anlaşılmıyor.

1. Oklar kaydırılabilir alanın iki ucuna, alanın kendi içine yerleştirilir: sol ok ilk öğenin solunda, sağ ok son görünen öğenin sağında.
2. Oklar yalnızca o yöne kaydırılacak içerik varsa görünür (bugün her ikisi de her zaman görünüyor olabilir; kontrol et).
3. Oklar yarı saydam, üzerine gelince belirginleşir. Faz 0.6'daki "yeni uygulama" noktası bu oklara taşınır.

## Görev 5.7: AI Usage arayüzü

Faz 1.7'deki `AIUsageStatus` kullanılarak:
1. Halkaların altındaki etiketler "Claude · 5 sa" / "Claude · Hafta".
2. Tooltip: yüzde, sıfırlanma zamanı ("Pazartesi 09:00'da sıfırlanır"), son güncelleme ("3 dk önce").
3. Hata durumları: `CliNotFound` → gri halka + "Claude CLI bulunamadı" + tık: kurulum sayfası; `NotLoggedIn` → "Giriş yapın: `claude` komutunu çalıştırın"; `Timeout/Unknown` → son başarılı veri soluk renkte + uyarı noktası.
4. Kullanım %80'i geçince halka turuncu, %95'te kırmızı; opsiyonel bildirim ("5 saatlik limitin %90'ı kullanıldı").

## Görev 5.8: Dock items sayfası

1. Liste üstünde filtre kutusu ve tür filtresi (Uygulamalar / Widget'lar / Klasörler).
2. Çoklu seçim (Ctrl/Shift) + "Kaldır" ve "Klasöre taşı".
3. Listede klasörler ağaç olarak (açılır/kapanır) gösterilir; bugün klasör içleri görünmüyorsa eklenir.
4. Yolu bulunamayan uygulamalar kırmızı uyarı ikonu + "Konumu bul…" butonuyla gösterilir (Faz 0.3'teki `Repair` başarısız olduysa).

## Görev 5.9: Tutarlı geri bildirim

1. Tüm `MessageBox.Show` kullanımları (ör. [Settings/SettingsWindow.Items.cs:290](../src/CustomDock/Settings/SettingsWindow.Items.cs)) tema uyumlu `ConfirmDialog` veya ayarlar penceresi içi bilgi şeridine (InfoBar) çevrilir.
2. Yeniden başlatma gerektiren ayarlar için ortak bir InfoBar: "Bu değişiklik yeniden başlatmadan sonra uygulanır · Şimdi yeniden başlat".

## Test planı

1. Yeni bir `DOCKHUB_HOME` ile ilk açılış sihirbazı baştan sona; X ile kapatma.
2. Arama: "blur", "saat", "hide" gibi sorgular doğru satırları bulur ve vurgular.
3. Önizleme: her görünüm ayarı değişimi mini dock'a yansır; açık/koyu tema.
4. Widget genişlikleri: tüm widget'lar Small/Medium/Large dock'ta eklenip ekran görüntüsü alınır, "Uygulama notları"na eklenir.
5. README ekran görüntüleri gerekiyorsa yenilenir.

## Commit önerisi

- `feat(onboarding): first-run welcome flow`
- `feat(settings): search across settings and widgets`
- `feat(appearance): live mini dock preview`
- `refactor(widgets): standard card widths and idle collapse`
- `fix(dock): scroll arrows at the edges of the scrollable area`
- `feat(ai-usage): labels, reset times and clear error states`

## Uygulama notları

_(Faz uygulanırken doldurulacak.)_
