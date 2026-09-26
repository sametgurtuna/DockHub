# Faz 3: Görev Çubuğu Eşitliği

> Öncelik: 🟠 Yüksek · Boyut: L · Bağımlılık: Faz 0 (sağlam uygulama eşleşmesi), Faz 1.8 (README)

## Amaç

"Görev çubuğunun yerini alır" iddiasını tamamlamak. Windows görev çubuğundan geçen bir kullanıcının ilk fark edeceği eksikleri kapatmak: Win+1..9, ayarlanabilir kısayollar, ses ve pil ikonları.

---

## Görev 3.1: Win+1..9 ile uygulama başlatma/geçiş

### Davranış (Windows ile aynı)
| Kısayol | Davranış |
|---|---|
| Win+N | N. uygulama çalışmıyorsa başlat; çalışıyor ve öndeyse küçült; çalışıyor ve arkadaysa öne getir; birden fazla penceresi varsa pencereler arasında dolaş |
| Win+Shift+N | N. uygulamanın yeni örneğini başlat |
| Win+Ctrl+Shift+N | Yönetici olarak başlat |
| Win+Alt+N | N. uygulamanın Jump List'ini aç |
| Win+0 | 10. uygulama |

Sıralama: dock'ta soldan sağa görünen **uygulama butonları** (pinli + pinlenmemiş çalışan). Widget'lar, ayraçlar ve klasörler sayılmaz. Dikey dock'ta yukarıdan aşağı.

### Teknik yaklaşım
- `RegisterHotKey(MOD_WIN, '1')` çalışmaz: Explorer bu kısayolları görev çubuğu gizliyken bile tutar.
- Bunun yerine `Native/KeyboardHook.cs`: `SetWindowsHookEx(WH_KEYBOARD_LL)`. Yalnızca `TaskbarMode.Replace` modunda ve ayar açıkken kurulur.
- Win basılıyken rakam tuşu `KeyDown` olunca olay yutulur (`return 1`) ve eylem `Dispatcher.BeginInvoke` ile UI thread'inde çalıştırılır. Hook callback'i asla bloklanmaz (Windows 300 ms'de hook'u düşürür).
- **Başlat menüsünün açılmasını önleme:** Win bırakıldığında Başlat açılmasın diye, rakam yutulduktan sonra Win bırakılmadan önce atanmamış bir tuş (`VK 0xE8`) `SendInput` ile gönderilir (PowerToys'un kullandığı teknik).
- Hook yalnızca `Win` modifier'ı basılıyken rakam tuşlarına bakar; diğer tüm tuşlar doğrudan `CallNextHookEx`'e gider.
- Hook delegate'i alanda tutulur (GC), `Dispose`'da kaldırılır. Güvenlik yazılımlarıyla çakışma riski README'de not edilir.

### Görsel geri bildirim
Win tuşu 800 ms basılı tutulursa uygulama butonlarının köşesinde 1..9, 0 numara rozetleri belirir (Windows 11 davranışı). Win bırakılınca kaybolur.

### Ayar
Ayarlar → Taskbar → "Win + sayı tuşlarıyla dock uygulamalarını aç" (varsayılan: açık, yalnızca Replace modunda etkin).

**Kabul:** Replace modunda Win+1 ilk dock uygulamasını açar, Başlat menüsü açılmaz; ShowBoth modunda Windows'un kendi davranışı bozulmaz.

---

## Görev 3.2: Ayarlanabilir global kısayollar

1. `AppConfig.Hotkeys: Dictionary<string, string>` (eylem kimliği → `"Win+Alt+D"` gibi gesture metni). Eksik anahtarlar varsayılanla doldurulur.
2. Eylemler (`Core/HotkeyActions.cs`):
   - `toggle-dock` (varsayılan Win+Alt+D)
   - `open-start` (varsayılan yok)
   - `open-settings` (varsayılan yok)
   - `focus-timer-toggle` (ilk Focus widget'ı; varsayılan yok)
   - `add-reminder` (hızlı hatırlatıcı girişi; varsayılan yok)
   - `toggle-mute` (varsayılan yok)
3. `Core/HotkeyService.cs`: `RegisterHotKey`'i `DockWindow`'dan ([Dock/DockWindow.xaml.cs:234](../src/CustomDock/Dock/DockWindow.xaml.cs)) alır. Ana dock penceresinin hwnd'si üzerinden kaydeder, config değişince yeniden kaydeder, başarısız kayıtları durum olarak tutar.
4. Yeni kontrol `Controls/HotkeyBox.cs`: odaklanınca "Kısayola basın…" yazar, tuş kombinasyonunu yakalar, `Esc` iptal, `Backspace` temizler. Yalnızca modifier'lı kombinasyonları kabul eder.
5. Ayarlar → General → "Klavye kısayolları" bölümü: her eylem için `HotkeyBox`. Kayıt başarısızsa satırda turuncu "Başka bir uygulama kullanıyor" uyarısı.
6. Mevcut sessiz Ctrl+Alt+D fallback'i kaldırılır; çakışma artık kullanıcıya gösterilir.
7. README'deki kısayol metni güncellenir (Faz 1.8'deki geçici metin kaldırılır).

---

## Görev 3.3: Ses tray ikonu

`NetworkStatusIconView` ([Dock/NetworkStatusIconView.cs](../src/CustomDock/Dock/NetworkStatusIconView.cs)) desenini izler.

1. Önce ortak taban: `Dock/StatusIconViewBase.cs` (hover arka planı, glyph, tooltip, boyut). `NetworkStatusIconView` bundan türetilir.
2. `Dock/VolumeStatusIconView.cs`:
   - Glyph seviyeye göre: sessiz `E74F`, 0 `E992`, 1-33 `E993`, 34-66 `E994`, 67-100 `E995` (Segoe Fluent Icons / MDL2 Volume serisi).
   - Tooltip: "Hoparlör (Realtek…): %45".
   - Sol tık: Audio widget'ının panelini (çıkış cihazı seçimi + seviye) açan küçük bir flyout. Kod `Widgets/Audio/AudioWidget` içinden ortak bir `AudioPanel` kontrolüne çıkarılır.
   - Tekerlek: ±2 %, Shift+tekerlek ±10 %.
   - Orta tık: sessize al / aç.
   - Sağ tık: "Ses ayarları" (`ms-settings:sound`), "Ses karıştırıcı" (`ms-settings:apps-volume`).
   - Veri: mevcut `AudioService` olayları; polling eklenmez.
3. Ayarlar → Taskbar → Right side → "Ses ikonu" (varsayılan açık, Replace modunda).

## Görev 3.4: Pil tray ikonu

1. `Dock/BatteryStatusIconView.cs`: yalnızca sistemde pil varsa görünür (`GetSystemPowerStatus`, `BatteryFlag != 128`).
2. Glyph: Segoe Fluent Icons'taki pil seviye serisi (`Battery0`..`Battery10`) ve şarj serisi (`BatteryCharging0`..`10`). Kod noktaları uygulama sırasında font üzerinden doğrulanır. %20 altında turuncu, %10 altında kırmızı.
3. Tooltip: "%64 · yaklaşık 3 sa 12 dk kaldı" veya "%64 · şarj oluyor".
4. Tık: `ms-settings:batterysaver`. Sağ tık: güç modu (varsa), "Güç ve pil ayarları".
5. Veri: `SystemEvents.PowerModeChanged` + `RegisterPowerSettingNotification(GUID_BATTERY_PERCENTAGE_REMAINING)`. Polling yok.
6. Ayar: "Pil ikonu" (varsayılan: pil varsa açık).

## Görev 3.5: README ve bilinen sınırlamalar

- "Windows 11's network, volume and battery icons live inside Explorer…" notu güncellenir: artık ağ, ses ve pil ikonlarının DockHub eşdeğerleri var; Windows'un Quick Settings paneli yine Win+A ile açılır.
- Özellik listesine Win+1..9 ve kısayollar eklenir.

## Test planı

1. Win+1..9: pinli/çalışan/çok pencereli uygulamalarla tüm davranış tablosu. Oyun gibi tam ekran uygulamada Win+1'in beklendiği gibi çalıştığı ve takılma yapmadığı kontrol edilir.
2. Kısayollar: çakışan kısayol (ör. başka bir uygulamanın kullandığı) uyarı gösterir; değiştirilen kısayol yeniden başlatmadan çalışır.
3. Ses: tekerlek, orta tık, cihaz değişimi (kulaklık tak/çıkar) ikonu günceller.
4. Pil: masaüstü PC'de ikon görünmez; dizüstünde şarj tak/çıkar anında güncellenir.
5. ShowBoth modunda hiçbir hook kurulmadığı log'dan doğrulanır.

## Commit önerisi

- `feat(shell): Win+1..9 launches and switches dock apps`
- `feat(hotkeys): configurable global shortcuts with conflict detection`
- `feat(tray): volume and battery status icons`

## Uygulama notları


### 2026-09-26 — tamamlandı

- **Win+1..9 / Win+0:** `Shell/WinNumberHotkeys.cs` (WH_KEYBOARD_LL; yalnızca Replace modunda ve ayar açıkken). Başlat menüsünün açılmasını önlemek için VK 0xE8 gönderiliyor. Sıra ana dock'taki `AppButton`'lar (pinli + çalışan). Win 800 ms basılı tutulunca numaralar görünüyor.
  - **Otomatik test edilemedi:** hook enjekte edilmiş (`LLKHF_INJECTED`) tuşları bilerek yok sayıyor, bu yüzden SendInput ile denenemiyor. Hook'un kurulduğu log'da doğrulandı ("Win+number shortcuts enabled").
- **Kısayollar:** `HotkeyService` (message-only pencere), `AppConfig.Hotkeys` (eksik = varsayılan, boş = kapalı), `HotkeyBox` kontrolü, Ayarlar › General › Keyboard shortcuts. Başka uygulamanın kullandığı kısayol uyarıyla gösteriliyor.
  - Varsayılan "dock'u göster" artık **Ctrl+Alt+D**, çünkü Win+Alt+D Windows 11'de kayıt edilemiyordu (log'da görülmüştü). Sessiz fallback kaldırıldı.
  - Ayarlarda yalnızca işleyicisi olan eylemler listeleniyor. `focus-dock`, `next-profile` ve `clipboard-history` sonraki fazlarda ekleniyor.
- **Durum ikonları:** `StatusIconViewBase` + ağ / ses / pil. Windows'un klasik ses, ağ ve güç tray ikonları (GUID `7820ae73/74/75`) DockHub'ınkiler açıkken gizleniyor. Canlı test: güç ve ses kopyaları gizlendi, EarTrumpet (kullanıcının uygulaması) doğru şekilde kaldı.
- **Plandan sapma:** ses ikonuna tıklamak Audio widget paneli yerine Windows hızlı ayarlarını açıyor (ağ ikonuyla tutarlı). Cihaz seçimi, mixer ve ayarlar sağ tık menüsünde.
- `Log.DebugEnabled` açıkken açılıştan 10 s sonra tanılama raporu log'a yazılıyor. Rapora tray ikonları da eklendi.
