# DockHub macOS — Widget Şeması

Windows v0.6.1 ile macOS arasında widget kimliklerinin, varyantlarının, ayar
anahtarlarının ve çalışma verisinin karşılaştırması. Kaynak:
`src/CustomDock/Widgets/WidgetRegistry.cs`, widget başına ayar sınıfları ve
`Services/*Service.cs`. Orvant karşılığı: `T16-WIDGET-KIMLIK`.

Neden önemli: `config.json` iki platformda okunabilir olmalı (MIMARI.md bölüm 3).
Alan adları uyumlu olsa da widget kimliği farklıysa Windows'tan gelen bir widget
macOS'ta "bilinmeyen widget" olarak görünür, varyantı tanınmazsa varsayılana düşer.

## 1. Kimlik ve varyantlar — eşitlendi

T16'dan önce macOS kendi adlarını yazıyordu. Artık kayıt defteri Windows'la aynı.
Eski adlar config yüklenirken bir kez taşınıyor ve dosyaya yazılıyor
(`WidgetRegistry.migrateLegacyIds`, `ConfigService`). Klasör içindeki öğeler de
taşınıyor. Taşıma yalnız Windows'ta bulunmayan eski adları eşlediği için Windows'un
yazdığı bir config'e dokunmuyor.

| Eski macOS | Windows (şimdi macOS da) |
|---|---|
| `sticky-note` / `single` | `notes` / `sticky` |
| `now-playing` | `media` |
| `trash` | `recycle-bin` |
| `device-battery` | `battery-devices` |
| `stopwatch`, `focus`, `countdown`, `alarm` / `single` | aynı kimlik / `default` |
| `hydration` / `goal` | `hydration` / `progress` |
| `network` / `graph` | `network` / `chart` |
| `weather` / `condition` | `weather` / `conditions` |

Varsayılan varyant (listenin ilki) de Windows'la aynı sıraya getirildi:
`clock` → `analog`, `reminders` → `list`. Bir öğenin `variant` alanı boşsa artık
Windows'taki varsayılan uygulanır.

Kategoriler Windows'unkilerin çevirisi: Saatler, Hatırlatıcılar, Yapışkan notlar,
Medya, Sistem, Hava durumu, Yapay zeka. macOS'a özgü widget'lar "Ekler"de.

## 2. Eksik varyantlar — parite açığı

Windows'ta olup macOS'ta henüz çizilmeyen varyantlar. Kayıt defterine **konmadı**:
galeride seçilebilir görünüp çalışmaması yanıltıcı olurdu. Windows'tan gelen bir
config bu varyantı içerirse widget bildiği görünümlerden biriyle çizilir (örneğin
`clock/calendar` dijital görünür), değer config'ten silinmez.

| Widget | Eksik varyant | Windows adı |
|---|---|---|
| `clock` | `calendar` | Calendar (sıradaki hatırlatıcıyı da gösterir) |
| `system` | `bars` | Bars |
| `status` | `icons` | Icon only |
| `weather` | `hourly` | Hourly forecast |
| `ai-usage` | `numbers` | Numbers (Windows'ta varsayılan) |

## 3. macOS'a özgü widget'lar

| Kimlik | Not |
|---|---|
| `battery` | Windows'ta pil, `status` widget'ının halkalarından biri. macOS'ta ayrıca tek başına widget olarak da var. |
| `shortcut` | Parite dışı ek (`T14-EKLER`). |
| `airdrop` | Parite dışı ek (`T14-EKLER`). |

Windows bu kimlikleri tanımaz. Aynı config Windows'a taşınırsa bu üç widget orada
görünmez ama silinmez.

## 4. Ayar anahtarları — henüz eşitlenmedi

Widget başına ayarlar `DockItem.settings` içinde. Windows'ta her ayar sınıfı
camelCase anahtarla yazılıyor, enum'lar PascalCase string. macOS anahtarları
bağımsız seçilmişti:

| Widget | Windows anahtarları (varsayılan) | macOS anahtarları | Fark |
|---|---|---|---|
| `clock` | `use24Hour` (true), `showSeconds` (false), `dateFormat` (`Short`/`Long`/`Numeric`/`WeekdayOnly`) | `showDate`, `showSeconds` | `use24Hour` ve `dateFormat` yok, `showDate` fazladan |
| `world-clock` | `cities: [{label, timeZoneId}]`, `use24Hour` | `cities: [{name, timezone}]` | Alan adları farklı. `timeZoneId` Windows saat dilimi kimliği ("Turkey Standard Time"), macOS IANA ("Europe/Istanbul") kullanıyor, CLDR `windowsZones` eşleme tablosu gerekir. |
| `focus` | `focusMinutes` (25), `breakMinutes` (5), `autoStartNext` (true), `sessionsBeforeLongBreak` (4), `longBreakMinutes` (15) | `focusMinutes`, `breakMinutes` | Uzun mola ve otomatik başlatma yok |
| `countdown` | `seconds` (300), `label` ("Countdown") | `minutes`, `label` | Birim farklı: saniye / dakika |
| `alarm` | `enabled`, `time` ("07:30"), `repeatDaily`, `label` | `enabled`, `time`, `repeatDaily`, `label` | **Aynı** |
| `time-progress` | `mode` (`Day`/`Week`/`Month`/`Year`, varsayılan `Year`) | `scope` (`day`/…) | Anahtar ve değer biçimi farklı |
| `hydration` | `dailyGoal` (8), `intervalMinutes` (60), `startHour` (9), `endHour` (22), `notificationsEnabled` (true), `glassMl` (250) | `goal`, `count`, `date` | Anahtar farklı; sayaç Windows'ta ayarda değil (bölüm 5) |
| `notes` | `fontSize` (20), `width` (`Narrow`/`Normal`/`Wide`), `color` (`Yellow`, `Green`, `Blue`, `Pink`, `Purple`, `Orange`, `Red`) | `color` (küçük harf), `text` | Renk küçük harf yazılıyor; metin Windows'ta ayarda değil |
| `media` | `hideWhenIdle` | — | Yok |
| `system` | `updateIntervalSeconds` (2) | `interval` | Anahtar farklı |
| `status` | `showBattery`, `showDisk`, `showMemory`, `showCpu` (hepsi true) | `interval` | Göstergeler seçilemiyor |
| `weather` | `locationMode` (`Auto`/`City`), `cityName` ("Istanbul"), `latitude`, `longitude`, `useFahrenheit` | `latitude`, `longitude`, `place` | `cityName`/`place`; kip ve Fahrenheit yok |
| `reminders`, `network`, `audio`, `recycle-bin`, `battery-devices`, `ai-usage` | ayar sınıfı yok | `reminders` (bölüm 5), `interval` | — |

## 5. Çalışma verisi — yapısal fark

Windows sayaç, liste ve metin gibi değişen verileri config'e değil, veri
klasöründeki ayrı dosyalara yazıyor (`JsonStore.DataPath(ad)` →
`%AppData%\DockHub\data\<ad>.json`). macOS bunları widget ayarının içinde tutuyor.

| Veri | Windows | macOS |
|---|---|---|
| Hatırlatıcılar | **Global** `data/reminders.json`: `{items: [{id, text, due}]}`. `due` tam tarih ve saat, tek seferlik; bildirimde "10 dk ertele" var. Birden çok hatırlatıcı widget'ı aynı listeyi gösterir. | **Widget başına** `settings.reminders: [{text, time}]`, her gün tekrar eden saat. |
| Su takibi sayacı | Global `data/hydration.json`: `{date, count, lastEvent}` | Widget başına `settings.count` ve `settings.date` |
| Not metni | Widget başına `data/notes-<id>.json`: `{text, updatedAt}` | `settings.text` |
| Hava durumu önbelleği | `data/weather-cache.json` | diske yazılmıyor, her açılışta yeniden çekiliyor |

Hatırlatıcılarda fark yalnız biçim değil, **davranış**: Windows tarihli ve tek
seferlik hatırlatıcı tutuyor, macOS günlük tekrar. `pc-davranis-v1` bakımından bu
bir parite açığı.

## 6. Sonraki iş

Bölüm 4 ve 5, ayrı bir görevde eşitlenecek. Sebebi: ayarlar penceresindeki widget
ayar formları bu anahtarları yazacak. Önerilen sıra:

1. Ayar anahtarları ve değer biçimleri (bölüm 4). Eski anahtarlar bu belgedeki
   gibi taşınır.
2. Çalışma verisini `data/` klasörüne taşımak (bölüm 5). Hatırlatıcıların
   tarihli/tek seferlik davranışa geçmesi kullanıcıya sorulmalı, çünkü mevcut
   günlük hatırlatıcıların anlamı değişir.
3. Eksik varyantlar (bölüm 2).
