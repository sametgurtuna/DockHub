# DockHub macOS — Görsel Dil

Karar `d-gorsel-hedef`. Kullanıcı görsel hedef olarak [dockset.app](https://dockset.app)
tanıtım sayfasındaki dock'u gösterdi. Değerler tahminle değil, **canlı sayfanın
hesaplanmış stillerinden okundu** (referans çubuk yüksekliği 42.9 birim).

Dockset ücretli bir üründür. Buradan alınan şey **genel görsel dil** — koyu cam
çubuk, yuvarlak kutucuklar — ki bu zaten Apple'ın kendi tasarım dilidir. İkon,
logo ve özgün grafikleri kopyalanmaz.

## Ölçülen değerler

| Özellik | Ölçülen | Bizdeki karşılık |
|---|---|---|
| Arka plan | `rgba(37,37,37,0.74)` | `NSVisualEffectView` + ton katmanı |
| Bulanıklık | `blur(18px) saturate(1.4)` | `material .hudWindow`, `blendingMode .behindWindow` |
| Köşe yarıçapı | `12.24px` → yüksekliğin **0.285**'i | `DockStyle.cornerRadius` |
| İç boşluk | `4.23px` → **0.098** | `DockStyle.padding` |
| Öğe arası | `4px` → **0.093** | `DockStyle.gap` |
| Öğe yüksekliği | `34px` → **0.792** | `DockStyle.itemHeight` |
| Öğe yarıçapı | `14px` → öğe yüksekliğinin **0.412**'si | `DockStyle.itemRadius` |
| Üst kenar ışığı | `inset 0 1px 1px rgba(255,255,255,0.133)` | 1pt üst çizgi |
| Alt kenar ışığı | `inset 0 -1px 1px rgba(255,255,255,0.04)` | 1pt alt çizgi |
| Dış gölge | `0 5px 20px rgba(0,0,0,0.2)` | katman gölgesi, yuvarlak yola oturtulmuş |
| Widget kutucuğu | `rgba(255,255,255,0.04)`, radius 14 | `DockStyle.itemBackground` |
| Yazı | `-apple-system`; etiketler 10–12px w400–600, sayılar 16–22px w500–600 | SF, `DockStyle` oranlarıyla |

## Neden oran, neden sabit değil

Değerler **koda sabit yazılmaz**, çubuk yüksekliğinden türetilir. Sebep: dock üç
boyutta çalışıyor (Small 48, Medium 56, Large 66) ve dikey dock'ta "kalınlık"
genişlik oluyor. Sabit bir `cornerRadius: 12` küçük dock'ta şişkin, büyükte
keskin görünürdü.

Ölçülen sonuç, 48 birimlik dock için:

```
cornerRadius  13.7   (48 × 0.285)
padding        4.7   (48 × 0.098)
öğe yüksekliği 38.0  (48 × 0.792)
ikon boyutu   31.2   (38 × 0.82)
```

## Üç uygulama ayrıntısı

**1. Gölge kırpılmamalı.** Cam katman yuvarlak köşe için `masksToBounds = true`
kullanıyor, bu da üstüne konan gölgeyi kırpardı. Çözüm: gölgeyi kırpılmayan bir
dış kap taşıyor ve `shadowPath` aynı yuvarlak dikdörtgene ayarlanıyor.
`NSWindow.hasShadow` kapatıldı, yoksa sistem köşeleri yok sayan dikdörtgen bir
gölge daha ekliyordu.

**2. Kenar ışığı cam hissinin asıl kaynağı.** Tek bir 1pt üst çizgi (beyaz %13.3)
ve çok daha soluk bir alt çizgi (%4). Kırpma sayesinde ikisi de yuvarlak köşeyi
izliyor. Bu ayrıntı olmadan çubuk "cam" değil "gri dikdörtgen" görünüyor.

**3. Uygulama ikonlarının kutucuğu yok.** Referansta da öyle: uygulamalar sade
ikon, widget'lar kutucuk. Uygulamanın altında çalışıyor noktası var — macOS
Dock'un kendi davranışı.

## Henüz uygulanmayanlar

- Renk tonunun `tintOpacity` ile ince ayarı kaba: şu an siyah katman, referansta
  arka plan renginin kendisi ayarlanıyor
- `Acrylic` seçeneğinde Windows'un gren dokusu yok (macOS'ta karşılığı yok)
- İkon büyütme animasyonu var ama macOS Dock'taki komşu-ikon dalgalanması yok
