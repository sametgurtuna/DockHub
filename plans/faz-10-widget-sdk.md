# Faz 10: Üçüncü Taraf Widget SDK'sı

> Öncelik: 🟢 Düşük · Boyut: XL · Bağımlılık: Faz 5.4, 6.1, 6.6, 9 (widget sözleşmesi olgunlaştıktan sonra dışa açılmalı)

## Amaç

Kaynak koda dokunmadan widget eklenebilmesini sağlamak ve topluluk katkısına kapı açmak.

## Karar: hangi model?

| | A) .NET eklenti DLL'i | B) Web widget (WebView2) |
|---|---|---|
| Yazma kolaylığı | C#/WPF bilgisi gerekir | HTML/CSS/JS yeterli |
| Güvenlik | DLL tam güvenle çalışır (dosya, ağ, süreç); izole edilemez | Sandbox; izinler manifest ile |
| Performans | Doğal, hafif | Her widget bir WebView2 görünümü: paylaşılan ortamla ~20-40 MB |
| Görsel uyum | Tam (aynı token'lar) | CSS değişkenleriyle tema aktarımı gerekir |
| Kararlılık | Hatalı eklenti dock'u çökertebilir | Renderer süreci ayrı; çökme izole |

**Öneri: B (web widget'ları), A ileride "güvenilir eklentiler" için.** Gerekçe: DockHub'ın "Windows'u asla bozma" ilkesi, rastgele DLL yüklemeyle bağdaşmıyor.

---

## Görev 10.1: Manifest ve paket biçimi

`%AppData%\DockHub\widgets\<id>\manifest.json`:

```json
{
  "id": "com.example.pomodoro-plus",
  "name": "Pomodoro+",
  "version": "1.0.0",
  "author": "…",
  "description": "…",
  "entry": "index.html",
  "icon": "icon.svg",
  "sizes": ["compact", "standard"],
  "variants": [{ "id": "default", "name": "Default" }],
  "settings": [{ "key": "minutes", "type": "number", "label": "Minutes", "default": 25, "min": 5, "max": 90 }],
  "permissions": { "network": ["api.example.com"], "notifications": true },
  "minDockHubVersion": "1.0.0"
}
```

Paket: aynı içerikli `.dockwidget` (zip). Çift tıklama ile kurulum (installer'da dosya ilişkilendirmesi, HKCU).

## Görev 10.2: Çalışma zamanı

1. Tek bir paylaşılan `CoreWebView2Environment` (`%LocalAppData%\DockHub\WebView2`). Her widget örneği bir `WebView2` kontrolü; `WidgetBase`'ten türeyen `WebWidgetHost`.
2. Widget dosyaları sanal host adıyla sunulur: `SetVirtualHostNameToFolderMapping("<id>.widget.dockhub", klasör, Deny)`. `file://` erişimi yok.
3. Ağ: `WebResourceRequested` ile manifestteki alan adları dışındaki istekler engellenir.
4. Yeni pencere, indirme, dosya seçici, bağlam menüsü, geliştirici araçları (geliştirici modu hariç) kapalı.
5. Şeffaf arka plan (`DefaultBackgroundColor = Transparent`) ve dock temasının CSS değişkenleri: `--dh-text-primary`, `--dh-accent`, `--dh-card-bg`, `--dh-font` vb. Tema değişince `themechange` olayı.
6. Görünür değilken (Faz 9.2 `IsOnScreen`) `TrySuspendAsync`.

## Görev 10.3: Köprü API'si (`window.dockhub`)

```ts
dockhub.settings.get(): Promise<Record<string, unknown>>
dockhub.settings.onChange(cb)
dockhub.storage.get(key) / set(key, value)       // data/web-<id>-<itemId>.json, 256 KB sınırı
dockhub.notify({ title, body })                  // permissions.notifications gerekir
dockhub.openPanel() / closePanel()               // tıklayınca açılan büyük görünüm
dockhub.contextMenu.set([{ id, label }]) + onSelect(cb)
dockhub.theme: { mode: "dark" | "light", accent: string }
dockhub.size: "compact" | "standard" | "wide"
dockhub.openUrl(url)                             // varsayılan tarayıcıda, kullanıcı onayıyla
```

`postMessage` tabanlı, her çağrı manifest izinlerine karşı doğrulanır. Sürümlü (`dockhub.apiVersion`).

## Görev 10.4: Arayüz

1. Widget galerisinde "Yüklü widget'lar" bölümü ve "Widget klasörünü aç", "Paket yükle…".
2. Kurulumda izin özeti diyaloğu: "Bu widget şunlara erişmek istiyor: api.example.com, bildirimler".
3. Hatalı widget (yüklenemedi, çöktü) kartta hata durumu gösterir, dock'u etkilemez.
4. Ayarlar → Hakkında → "Geliştirici modu": DevTools, klasörden canlı yeniden yükleme.

## Görev 10.5: Dokümantasyon ve örnek

1. `docs/widget-sdk.md`: manifest şeması, API referansı, tema değişkenleri, boyut kuralları, izin modeli.
2. `samples/widgets/hello-world` ve `samples/widgets/github-stars` (ağ izni örneği).
3. README "Writing a widget" bölümüne web widget yolu eklenir.

## Test planı

1. Örnek widget'lar kurulur, çalışır, tema değişimine uyar.
2. İzin dışı alan adına istek engellenir; `file://` erişimi engellenir.
3. Sonsuz döngüye giren bir widget dock'u dondurmaz (renderer ayrı süreç).
4. 10 web widget'ı ile bellek ölçümü (Faz 9.1 yöntemiyle) notlara yazılır.

## Commit önerisi

- `feat(sdk): manifest-based web widgets hosted in WebView2`
- `feat(sdk): dockhub bridge API with permission checks`
- `docs(sdk): widget SDK guide and samples`

## Uygulama notları


### 2026-09-26 — tamamlandı (MVP)

- **Model:** planda önerilen B seçeneği (WebView2) uygulandı. `WebWidgetCatalog`, `%AppData%\DockHub\widgets` altındaki manifest'leri okuyup doğruluyor (id kuralı, entry'nin klasör dışına çıkmaması, `minDockHubVersion`) ve `WidgetRegistry.Register` ile açılışta ekliyor.
- **Çalışma zamanı:** tüm widget'lar için ortak bir `CoreWebView2Environment` (`%LocalAppData%\DockHub\WebView2`) kullanılıyor. Dosyalar sanal host'tan (`<id>.widget.dockhub`) sunuluyor. Manifest dışındaki host'lara giden istekler 403 alıyor; yeni pencere, indirme ve izin istekleri kapalı. DevTools yalnızca `debugLogging` açıkken kullanılabiliyor.
- **Köprü (`window.dockhub`, apiVersion 1):** settings.get/onChange (manifest varsayılanları ile birleşik), storage.get/set (256 KB), notify (izin gerekiyor), openUrl (yalnızca http(s), 2 s sınırı), contextMenu.set/onSelect (en fazla 8), onTheme (CSS değişkenleri), onSize.
- **Arayüz:** galeriye "Install widget…" eklendi (izin özeti gösteren onay diyaloğuyla) ve "Open widgets folder". Manifest'te tanımlanan ayarlar dinamik olarak oluşturuluyor (text/number/toggle/choice).
- **Belgeler ve örnekler:** `docs/widget-sdk.md`, `samples/widgets/hello-world`, `samples/widgets/github-stars`.
- **Canlı test:** kullanıcı boşta iken geçici bir DOCKHUB_HOME ile iki örnek dock'ta çalıştırıldı. Saydam arka plan, accent rengi ve api.github.com'dan canlı veri doğrulandı; ardından kullanıcının dock'u geri başlatıldı.
- **Yapılmayanlar:** `.dockwidget` dosya ilişkilendirmesi (installer), klasörden canlı yeniden yükleme, "güvenilir DLL eklentileri" (A seçeneği). Bilinen sınırlama: WebView2 bir HWND olduğundan dock'un hover büyütmesi bu kartlara uygulanmıyor.
- Testler: 73.
