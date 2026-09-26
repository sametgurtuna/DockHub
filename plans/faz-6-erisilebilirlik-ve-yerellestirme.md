# Faz 6: Erişilebilirlik ve Türkçe Arayüz

> Öncelik: 🟡 Orta · Boyut: L · Bağımlılık: Faz 5 (yeni metinler oluştuktan sonra dizeleri tek seferde çıkarmak için)

## Amaç

- Ekran okuyucu, klavye ve hareket hassasiyeti olan kullanıcılar için dock'u kullanılabilir hale getirmek.
- Uygulamayı Türkçe'ye çevirmek. Installer zaten Türkçe, uygulama tamamen sabit kodlanmış İngilizce.

Mevcut durum: kod tabanında yalnızca 10 `AutomationProperties` kullanımı var; "animasyonları azalt" ve yüksek karşıtlık desteği hiç yok.

---

## Görev 6.1: UI Automation

1. `AppButton`: `AutomationProperties.Name` = başlık; `HelpText` = "Çalışıyor, 2 pencere" / "Dikkat istiyor" / "İndirme %45". Durum değiştikçe güncellenir.
2. `WidgetItemView`: Name = widget adı + ana değer ("Hava durumu, 24 derece, İstanbul"). Her widget `WidgetBase.AccessibleSummary` (virtual) sağlar.
3. `GroupItemView`: "Klasör: AI, 4 öğe". Açılan fan içindeki öğeler de adlandırılır.
4. Tray ikonları, saat, Başlat, arama, masaüstünü göster butonları.
5. Özel kontroller (`RingGauge`, `Sparkline`, `TickBar`, `AnalogClock`) için `AutomationPeer` sınıfları: `RingGauge` → `RangeValue` pattern.
6. Ayarlar penceresi: segmentli kontroller `RadioButton` olduğu için sorun yok; `SettingRow` içindeki kontrole `LabeledBy` ile başlık bağlanır.
7. Doğrulama aracı: Windows SDK'daki **Accessibility Insights for Windows** ile dock ve ayarlar taranır; kritik hata kalmamalı.

## Görev 6.2: Klavye ile kullanım

1. Kısayol (Faz 3.2 eylem listesine `focus-dock`, varsayılan Win+Alt+T): dock'u "giriş moduna" alır (`DockWindow` zaten `_inputMode` desteğine sahip) ve ilk öğeye odaklanır.
2. Ok tuşları öğeler arası gezinir (dikey dock'ta yukarı/aşağı), `Home`/`End` uçlara gider.
3. `Enter`/`Space` = sol tık, `Shift+Enter` = yeni örnek, `Menu` tuşu veya `Shift+F10` = bağlam menüsü, `Esc` = odağı bırak ve önceki pencereye dön.
4. Klasörde `Enter` fan'ı açar; fan içinde oklarla gezilir.
5. Odak görseli: öğenin hover arka planı + 2 px accent çerçeve (`FocusVisualStyle`).
6. Ayarlar penceresinde tüm kontroller Tab sırasına uygun; kenar çubuğu ok tuşlarıyla gezilir.

## Görev 6.3: Hareketi azalt

1. `AppConfig.Motion`: `System` (varsayılan) / `Full` / `Reduced` / `Off`.
2. `System` → `SystemParameters.ClientAreaAnimation` (Windows → Erişilebilirlik → Görsel efektler → Animasyon efektleri) ve `UISettings.AnimationsEnabled`.
3. `Controls/Motion.cs`'teki tüm yardımcılar tek bir `Motion.Level` kontrolünden geçer:
   - `Reduced`: genie, fan, spring/back easing kapatılır; yerine 120 ms fade. Hover büyütme kapanır.
   - `Off`: süre 0.
4. `GenieEffectHelper` ve `PopupAnimationHelper` da aynı kontrolü kullanır.
5. Ayarlar → Appearance → "Animasyonlar".

## Görev 6.4: Yüksek karşıtlık

1. `SystemParameters.HighContrast` true ise `Themes/HighContrast.xaml` yüklenir: tüm fırçalar `SystemColors` dinamik kaynaklarına bağlanır, cam/blur kapatılır, kenarlıklar 1 px düz.
2. `SystemParameters.StaticPropertyChanged` dinlenir; mod değişince tema anında değişir.
3. Widget'ların sabit renk kullanan yerleri (`AccentOrangeBrush` vb.) HC temasında sistem renklerine eşlenir. Rozetler ve göstergeler renk dışında şekille de ayırt edilebilir olmalı.

## Görev 6.5: Metin boyutu

Ayarlar ve flyout'lar Windows'un "Metin boyutu" ayarına (`UISettings.TextScaleFactor`) uyar. Dock'un kendisi yükseklik sınırı yüzünden ölçeklenmez; bunun yerine dock boyutu önerisi gösterilir.

## Görev 6.6: Yerelleştirme (Türkçe)

### Altyapı
1. `Resources/Strings.resx` (İngilizce, nötr) + `Resources/Strings.tr.resx`.
2. XAML için `Core/Loc.cs` markup extension: `Text="{loc:Str Settings_General_Title}"`.
3. Kod için strongly-typed `Strings.Settings_General_Title` (resx designer veya source generator).
4. Anahtar adlandırma: `<Alan>_<Bağlam>_<Öğe>` (ör. `DockMenu_AddWidget`, `Widget_Weather_Name`).
5. `AppConfig.Language`: `System` / `en` / `tr`. `App.OnStartup` başında `CultureInfo.CurrentUICulture` ayarlanır. Değişiklik yeniden başlatma gerektirir (Faz 5.9 InfoBar).

### Dizelerin çıkarılması
1. Tarama: `Header = "`, `Text="`, `Content="`, `Description="`, `ToolTip = "`, `DockMenu.Item("`, `MessageBox.Show(`, `WidgetDescriptor` `Name`/`Description`/`WidgetVariant` isimleri. Bir PowerShell betiği (`tools/find-strings.ps1`) adayları listeler.
2. Log mesajları çevrilmez.
3. Dizeler parçalanmadan, format yer tutucularıyla taşınır: `"{0} applications added."` → `Strings.Items_AppsAdded(count)`. Türkçe çoğul kuralı basit (sayıdan sonra tekil) olduğundan tek dize yeterli.
4. Tarih/saat formatları zaten `CurrentCulture`'a göre; kontrol edilir.

### Çeviri
- Türkçe çeviri doğal, kısa ve Windows 11 Türkçe terminolojisiyle uyumlu olmalı: "Görev çubuğu", "Bildirim merkezi", "Hızlı ayarlar", "Sabitle", "Sabitlemeyi kaldır", "Masaüstünü göster".
- Uzun tire kullanılmaz (marka kuralı).
- Taşan metinler için tüm ayarlar sayfaları ve menüler Türkçe'de gözle kontrol edilir (Türkçe metinler genelde %20-30 daha uzun).

## Test planı

1. Narrator ile: dock'a Win+Alt+T ile gir, oklarla gez, her öğenin anlamlı okunduğunu doğrula.
2. Windows'ta "Animasyon efektleri" kapatılır → genie ve fan animasyonları kapanır.
3. Yüksek karşıtlık teması (Aquatic, Desert) açılır/kapanır → DockHub anında uyum sağlar.
4. Dil Türkçe → tüm menüler, ayarlar, widget adları, bildirimler Türkçe; hiçbir metin taşmıyor (ekran görüntüleri notlara).
5. Dil İngilizce → regresyon yok.

## Commit önerisi

- `feat(a11y): automation names, keyboard navigation for the dock`
- `feat(a11y): reduced motion and high contrast support`
- `feat(i18n): resource-based strings and Turkish translation`

## Uygulama notları

_(Faz uygulanırken doldurulacak.)_
