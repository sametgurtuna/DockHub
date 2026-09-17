# Custom Dock

Windows 10/11 görev çubuğunun **yerini alan**, cam efektli, widget destekli bir dock.
macOS'taki *Dockset*'ten esinlenilmiştir. .NET 8 + WPF ile yazılmıştır ve kabuk entegrasyonu için
[ManagedShell](https://github.com/cairoshell/ManagedShell) kullanır.

```
[⊞ 🔍] │ 📁 🌐 📝 │ 🕒 3:43 │ ♫ Şu an çalan │ ☁ 18° │ ◎ CPU ◎ RAM │ 💧 │ ▮▮▮▯ %15 │ ✳ ▣ │ ˄ 🔊 📶 │ 03:43 │
 Başlat/Ara   sabitlenmiş     widget kartları (sürükle-bırak ile sıralanır)       çalışan     tepsi      saat
              uygulamalar                                                         uygulamalar
```

## Özellikler

### Görev çubuğunun yerini alma
- **Başlat düğmesi** Windows Başlat menüsünü açar (`IImmersiveLauncher`); ikinci tıklama menüyü kapatır. Sağ tık Win+X menüsünü açar.
- **Windows tuşu** her zamanki gibi çalışır. Başlat menüsü, arama, bildirim merkezi ve hızlı ayarlar Windows'un kendi panelleriyle açılır.
- **Arama** ve **Görev görünümü** düğmeleri (Ayarlar › Görev çubuğu bölümünden açılıp kapatılabilir).
- **Çalışan uygulamalar:**
  - Sabitlenmiş uygulamalar, açık pencereleriyle eşleştirilir. Aynı uygulamanın pencereleri tek düğmede gruplanır.
  - Diğer açık uygulamalar bir ayraçtan sonra listelenir.
  - Etkin, dikkat isteyen ve ilerleme durumları (ör. indirme çubuğu) gösterilir.
  - Tıklama pencereyi öne getirir, küçültür ya da pencereler arasında geçiş yapar. Shift+tık veya orta tık yeni pencere açar. Sağ tık pencere listesini, "Yönetici olarak çalıştır", "Dosya konumunu aç", "Sabitle/Kaldır" ve "Tüm pencereleri kapat" komutlarını gösterir.
  - Çalışan bir uygulamayı sabitlemek için dock'un içine sürükleyin ya da sağ tıklayıp **"Custom Dock'a sabitle"** komutunu seçin.
  - **Explorer'dan sabitleme:** bir `.exe` dosyasına ya da kısayola sağ tıklayıp **"Custom Dock'a sabitle"** komutunu seçin; uygulama sabitlenmiş uygulamaların sonuna eklenir. Windows 11'de bu komut **"Daha fazla seçenek göster"** altında (veya Shift + sağ tık ile) görünür, çünkü yeni kısa menü yalnızca Store'dan paketlenmiş uygulamaların komutlarını gösterir. Ayarlar › Genel bölümünden kapatılabilir.
- **Sistem tepsisi:**
  - Uygulama ikonları dock'ta gösterilir. Tıklama, sağ tık ve üzerine gelme işlemleri uygulamaya iletilir.
  - Gizli ikonlar "˄" menüsünde durur. Ayarlar › Görev çubuğu bölümünden hangi ikonların her zaman görüneceği seçilir. İlk açılışta Windows'taki tercihler içe aktarılır.
- **Saat:** tıklanınca bildirim merkezi açılır. Sağ tık menüsünde hızlı ayarlar, tarih/saat ayarları ve saniye/tarih seçenekleri bulunur. En sağdaki ince şerit masaüstünü gösterir.
- **Ekranda yer ayırma:** dock, görünmez bir AppBar ile kendi alanını ayırır. Büyütülmüş pencereler dock'un altına girmez.
- **Güvenli geri dönüş:** Uygulama kapanınca, çökünce ya da oturum kapanırken Windows görev çubuğu tepsi ikonlarıyla birlikte geri gelir. Ayrıntılar aşağıda.

### Dock
- **Görünüm:**
  - Arka plan: bulanık cam, Acrylic ya da düz. Renk yoğunluğu ayarlanabilir.
  - Tema: koyu, açık ya da sisteme uyan. Vurgu rengi Windows'tan alınır.
- **Boyut:**
  - Küçük (varsayılan): 48 DIP, Windows görev çubuğuyla aynı kalınlık.
  - Orta: 56 DIP.
  - Büyük: 66 DIP.
- **Biçim:** *Yüzen* (kenarlardan boşluklu, yuvarlak köşeli) ya da *Yapışık* (klasik görev çubuğu).
- **Genişlik ve hizalama:** tam genişlik ya da içeriğe göre; öğeler ortada ya da başta.
- **Konum:** alt, üst, sol veya sağ kenar.
  - Dikey dock'ta widget'lar kompakt kutucuk olarak görünür (ikon/halka ve kısa değer).
  - Kutucuğa tıklanınca widget'ın tamamı yandaki panelde açılır. Zamanlayıcılar, su sayacı ve hatırlatıcı gibi widget'larda tıklama doğrudan birincil eylemi yapar.
- **Kaydırma:** Öğeler sığmazsa fare tekerleği ile yumuşak yatay kaydırma yapılır, kenarlar solar ve ok düğmeleri görünür.
- **Otomatik gizleme:** dock ekran kenarına kayar, fare kenara gelince geri gelir.
- **Tam ekranda gizleme:** oyun, video ya da F11 modunda dock gizlenir.
- **Diğer:** çoklu monitör (dock'un gösterileceği ekran seçilir), monitör başına DPI (PerMonitorV2), Windows ile başlatma.
- **Sürükle-bırak:** Uygulamalar, widget'lar ve ayraçlar dock üzerinde sürüklenerek sıralanır. Explorer'dan .exe/.lnk bırakmak uygulamayı sabitler.
- **Akıcılık:** Animasyonlar ekranın yenileme hızında çalışır (60 Hz'den yüksek ekranlarda da).
  - Kaydırma, otomatik gizleme kayması ve açılır paneller kare eşzamanlı yumuşak geçişler kullanır.
  - Düğme ve kart vurguları yavaşça belirir; uygulama ikonları üzerine gelince hafifçe büyür.
  - Dock'a yeni eklenen öğeler büyüyerek belirir.
- **Menüler:** Sağ tık menüleri ve widget panelleri her zaman dock'un dışında, imleç ya da öğe hizasında açılır; dock'un altında kalmaz.
- **Dock menüsü** (boş alana sağ tık): Widget ekle, Uygulama sabitle, Ayraç ekle, Görev Yöneticisi, Hızlı ayarlar, Otomatik gizle, Windows görev çubuğunu gizle, Konum, Ayarlar, Çıkış.

### Widget'lar ve görünümleri

Her widget birden fazla kez eklenebilir; her kopyanın kendi ayarları vardır.
Görünüm (varyant), widget'a sağ tıklayıp **Görünüm** menüsünden ya da Ayarlar › Dock öğeleri bölümünden değiştirilir.

| Kategori | Widget | Görünümler | Notlar |
|---|---|---|---|
| Saatler | **Saat** | Analog, Dijital, Takvim | 12/24 saat, saniye, tarih biçimi. Takvim görünümü sıradaki anımsatıcıyı gösterir. |
| | **Dünya saati** | Tek şehir, Çoklu şehir | Şehir ekleme/sıralama; gündüz/gece kadranı. |
| | **Kronometre** | – | Tıkla başlat/duraklat, sağ tık sıfırla. |
| | **Odak zamanlayıcı** | – | Pomodoro: odak/mola süreleri, bitince bildirim. |
| | **Geri sayım** | – | Hazır süreler, etiket, bitince bildirim. |
| | **Alarm** | – | Saat, etiket, her gün tekrar. Alarm bildirimi sesli ve kalıcıdır. |
| | **Zaman ilerlemesi** | Çubuk, Halka | Günün, haftanın, ayın ya da yılın geçen kısmı. |
| Hatırlatıcılar | **Su içme** | Zamanlayıcı, Günlük hedef | Tıkla: +1 bardak. Aralıklı bildirimde "İçtim" düğmesi bulunur. |
| | **Anımsatıcılar** | Liste, Sıradaki, Sayı | Toast bildirimi ve "10 dk ertele". |
| Notlar | **Yapışkan not** | – | Dock'ta notun önizlemesi görünür. Tıklanınca büyük not kağıdı açılır. Kağıttaki ayar düğmesi **Özelleştir** sayfasını açar: kağıt rengi (sarı, turuncu, kırmızı, mor, mavi, yeşil) ve yazı boyutu. Kendiliğinden kaydedilir. |
| Medya | **Şu an çalıyor** | Tam, Kompakt, Mini | Windows SMTC: Spotify, tarayıcılar, VLC vb. Kapak, ilerleme, önceki/oynat/sonraki. |
| Sistem | **CPU ve bellek** | Sayılar, Halkalar, Çubuklar | Güncelleme aralığı 1–10 sn. |
| | **Ağ** | Yalnızca sayılar, Grafikli | Anlık indirme/yükleme hızı. |
| | **Durum** | Halkalar, Yüzde halkası, Yalnızca ikon | Pil, disk, bellek ve işlemci. |
| Hava durumu | **Hava durumu** | Güncel, Durum, Saatlik tahmin | Open-Meteo, API anahtarı gerekmez. Şehir ya da Windows konumu kullanılır. |

Widget eklemek için Ayarlar › **Widget galerisi** bölümünde önizlemenin yanındaki **+** düğmesine tıklayın.
Widget'a sağ tıklayıp **Widget ayarları…** komutunu seçmek, o öğenin ayar sayfasını açar.

## Gereksinimler

- Windows 10 1809 (build 17763) veya üstü. Windows 11 22H2+ önerilir (yuvarlak köşeler, Mica ayarlar penceresi).
- Çalıştırmak için [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
- Derlemek için .NET 8 SDK veya üstü (.NET 9/10 SDK da `net8.0` hedefini derler)
- NuGet bağımlılıkları (derlemede kendiliğinden indirilir):
  - [`ManagedShell`](https://www.nuget.org/packages/ManagedShell) 0.0.372: görev listesi, sistem tepsisi, AppBar, tam ekran algılama (Apache-2.0)
  - [`Microsoft.Toolkit.Uwp.Notifications`](https://www.nuget.org/packages/Microsoft.Toolkit.Uwp.Notifications) 7.1.3: toast bildirimleri (MIT)

## Derleme ve çalıştırma

```bash
dotnet build CustomDock.sln -c Release
```

```bash
dotnet run --project src/CustomDock -c Release
```

Visual Studio 2022 ile `CustomDock.sln` dosyasını açıp F5 ile de çalıştırabilirsiniz.

### Yayınlama (kurulumsuz klasör)

```bash
dotnet publish src/CustomDock -c Release -r win-x64 --self-contained false -o publish
```

`publish\CustomDock.exe` çalıştırılabilir. .NET runtime'ı pakete dahil etmek için `--self-contained true` kullanın.

### Komut satırı

| Komut | İşlev |
|---|---|
| `CustomDock.exe` | Başlatır. Zaten çalışıyorsa ayarlar penceresini öne getirir. |
| `CustomDock.exe --exit` | Çalışan örneği düzgünce kapatır (görev çubuğu geri gelir). |
| `CustomDock.exe --pin "<dosya>"` | Uygulamayı dock'a sabitler (Explorer menüsünün kullandığı komut). Dock kapalıysa açar. |
| `CustomDock.exe --restore-taskbar` | **Acil durum:** Windows görev çubuğunu her koşulda geri getirir. |

`CUSTOMDOCK_HOME` ortam değişkeni ayar/veri klasörünü değiştirir. Taşınabilir kullanım ya da denemeler içindir; örneğin `set CUSTOMDOCK_HOME=D:\DockTest`.

## Görev çubuğunun yerini alma: nasıl çalışır, izinler

**Yönetici izni gerekmez.** Uygulama `asInvoker` olarak çalışır. Yönetici olarak **çalıştırmamanız** önerilir, çünkü yükseltilmiş bir pencereye Explorer'dan sürükle-bırak yapılamaz (UIPI). Explorer kapatılmaz; Custom Dock onun yanında çalışır.

Ayarlar › Genel › **"Görev çubuğunun yerini al" = Custom Dock** seçiliyken (varsayılan):

1. ManagedShell'in görev servisi açık pencereleri izler. Sistem tepsisi servisi `Shell_TrayWnd` bildirimlerini devralır; uygulamalar tepsi ikonlarını dock'a kaydeder.
2. Explorer'ın "taskman" penceresi geri verilir, böylece **Windows tuşu** Başlat menüsünü açmaya devam eder.
3. Windows görev çubuğunun durumu (`ABM_GETSTATE`) `%AppData%\CustomDock\session.json` dosyasına yazılır. Ardından görev çubuğu otomatik gizle moduna alınır ve `Shell_TrayWnd` / `Shell_SecondaryTrayWnd` pencereleri gizlenir.
4. 250 ms'de bir çalışan hafif bir denetleyici, Explorer görev çubuğunu kendiliğinden gösterirse onu yeniden gizler. Başlat menüsü, arama veya bir kabuk açılır menüsü açıkken denetleyici araya girmez.
5. Dock, görünmez ve tıklamaları geçiren bir AppBar ile ekranın kenarında kendi kalınlığı kadar yer ayırır.

"İkisi birlikte" modunda Windows görev çubuğuna dokunulmaz ve tepsi devralınmaz. Dock, görev çubuğunun üstünde durur. Mod değiştirildiğinde uygulama kendini yeniden başlatır.

Windows görev çubuğu şu durumlarda **otomatik olarak geri gelir:**

- Mod "İkisi birlikte" yapıldığında
- Uygulama normal şekilde kapatıldığında (dock menüsü, tray menüsü, `--exit`)
- Oturum kapatılırken (`SessionEnding`)
- İşlenmeyen bir hata olduğunda (`UnhandledException` / `ProcessExit`)
- Uygulama zorla sonlandırıldıysa (Görev Yöneticisi, elektrik kesintisi vb.):
  - Görev çubuğu otomatik gizlenen modda kalır; fare ekranın alt kenarına götürülünce yine açılır.
  - Bir sonraki açılışta ya da `--restore-taskbar` ile `session.json` okunur ve orijinal durum geri yüklenir.
  - Uygulamalara `TaskbarCreated` yayını gönderilir, böylece tepsi ikonları Explorer'a yeniden kaydolur.

**Acil durumda** şu yollardan biri kullanılabilir:

```bash
CustomDock.exe --restore-taskbar
```

- Ayarlar › Genel › "Windows görev çubuğunu geri getir"
- Tray menüsü → "Görev çubuğunu geri getir"
- Son çare: `explorer.exe`'yi yeniden başlatın ve Windows Ayarları › Kişiselleştirme › Görev çubuğu › "Görev çubuğunu otomatik olarak gizle" seçeneğini kapatın.

> **Notlar**
> - Windows 11'in ağ, ses ve pil simgeleri Explorer'ın içindedir. Dock'ta bu simgelerin klasik karşılıkları gösterilir. Hızlı ayarlar paneline saate ya da dock'a sağ tıklayarak veya Win+A ile ulaşılır.
> - Üçüncü taraf görev çubuğu değiştiricileri (StartAllBack, ExplorerPatcher, RetroBar vb.) aynı anda çalıştırılmamalıdır.

## Diğer izinler ve notlar

- **Explorer menüsü:** "Custom Dock'a sabitle" komutu `HKCU\Software\Classes\exefile\shell` ve `lnkfile\shell` altına kaydedilir. Yönetici izni gerekmez. Ayar kapatılınca kayıtlar silinir. Uygulama taşındığında açılışta yol güncellenir.
- **Konum (hava durumu → Otomatik):** Windows Ayarları › Gizlilik ve güvenlik › Konum altında hem konum hizmetleri hem de *"Masaüstü uygulamalarının konumunuza erişmesine izin ver"* açık olmalıdır. Kapalıysa widget şehir seçmenizi ister.
- **Bildirimler:**
  - Ek izin gerekmez; uygulama ilk bildirimde kendini (AUMID) HKCU altına kaydeder.
  - *Rahatsız etme* açıksa bildirimler Bildirim Merkezi'nde birikir.
  - Toast gösterilemezse tray balonu kullanılır.
- **Medya (SMTC):** Ek izin veya hesap gerekmez. Oynatıcının Windows medya kontrollerini desteklemesi yeterlidir.
- **Ağ:** Yalnızca hava durumu için `api.open-meteo.com` ve şehir araması için `geocoding-api.open-meteo.com` adreslerine istek yapılır.

## Ayar ve veri dosyaları

| Dosya | İçerik |
|-------|--------|
| `%AppData%\CustomDock\config.json` | Tüm ayarlar ve dock öğeleri (sıra, varyant, öğe başına ayarlar) |
| `%AppData%\CustomDock\data\notes-<öğe>.json` | Her yapışkan notun metni |
| `%AppData%\CustomDock\data\reminders.json` | Bekleyen anımsatıcılar |
| `%AppData%\CustomDock\data\hydration.json` | Günlük su sayacı |
| `%AppData%\CustomDock\data\weather-cache.json` | Konum başına son hava durumu (açılışta hızlı gösterim için) |
| `%AppData%\CustomDock\session.json` | Görev çubuğu geri yükleme bilgisi (yalnızca gizliyken bulunur) |
| `%AppData%\CustomDock\pin-requests.txt` | Explorer'dan gelen ve henüz işlenmemiş sabitleme istekleri (geçici) |
| `%AppData%\CustomDock\log.txt` | Günlük (512 KB'ta döner) |

- Dosyalar önce geçici bir dosyaya yazılıp sonra yerine taşınır; yazma yarıda kalırsa dosya bozulmaz.
- Bozuk bir dosya bulunursa yedeği (`*.corrupt-<tarih>`) alınır ve varsayılanlarla devam edilir.
- v1 `config.json` dosyası ilk açılışta kendiliğinden v2'ye taşınır. Taşınanlar: sabitlenmiş uygulamalar, widget'lar, widget ayarları ve not.

Örnek `config.json` (kısaltılmış):

```json
{
  "version": 2,
  "taskbarMode": "Replace",
  "hideOnFullscreen": true,
  "showStartButton": true,
  "showSearchButton": true,
  "showRunningApps": true,
  "showTray": true,
  "pinnedTrayIcons": ["c:\\program files\\spotify\\spotify.exe"],
  "edge": "Bottom",
  "autoHide": false,
  "theme": "Dark",
  "backdrop": "Blur",
  "tintOpacity": 0.35,
  "size": "Small",
  "layout": "Floating",
  "widthMode": "Full",
  "alignment": "Center",
  "edgeMargin": 6,
  "items": [
    { "id": "e0e4424de2", "kind": "App", "path": "C:\\WINDOWS\\explorer.exe" },
    { "id": "d09f6ec024", "kind": "Separator" },
    { "id": "521927ad3f", "kind": "Widget", "widget": "clock", "variant": "analog",
      "settings": { "use24Hour": true, "showSeconds": false, "dateFormat": "Short" } },
    { "id": "2f50f40ec4", "kind": "Widget", "widget": "weather", "variant": "hourly",
      "settings": { "locationMode": "City", "cityName": "İstanbul", "latitude": 41.0138, "longitude": 28.9497 } }
  ]
}
```

Uygulama öğelerinde `path` şunlardan biri olabilir:

- bir `.exe` yolu
- bir `.lnk` kısayolu
- herhangi bir dosya
- `shell:AppsFolder\<AUMID>` biçiminde bir Store/PWA uygulaması (Ayarlar › Dock öğeleri › *Uygulama ekle* bu listeden seçtirir)

## Proje yapısı

```
CustomDock.sln
src/CustomDock/
├── App.xaml(.cs)            Giriş: tek örnek, --exit / --restore-taskbar, çökme güvenliği, kabuk ve dock kurulumu
├── app.manifest             PerMonitorV2 DPI, asInvoker
├── Assets/CustomDock.ico    Uygulama/tray ikonu (tools/generate-icon.ps1 ile üretilir)
├── Core/                    AppConfig (v2 öğe modeli), ConfigService (v1 → v2 taşıma), JSON depolama, tema, log
├── Native/                  P/Invoke, DWM/cam efektleri, monitörler, yüksek çözünürlüklü kabuk ikonları
├── Shell/                   ManagedShell entegrasyonu
│   ├── ShellHost.cs           Görevler, tepsi, Başlat/arama/bildirim merkezi komutları
│   ├── TaskbarController.cs   Windows görev çubuğunu gizleme/geri getirme, çökme kurtarma
│   ├── StartMenuLauncher.cs   IImmersiveLauncher ile Başlat menüsü
│   ├── RunningAppsService.cs  Pencereleri uygulamaya göre gruplama
│   └── AppKeys.cs, TrayPreferences.cs, DefaultItems.cs
├── Services/                Saat, sistem/ağ izleme, medya (SMTC), hava durumu (+WeatherHub önbelleği), bildirim, anımsatıcı, su, uygulama başlatıcı
├── Dock/                    DockWindow (bölgeler, kaydırma, sürükle-bırak, konum, otomatik gizleme), AppButton,
│                            WidgetItemView (kart / dikey kutucuk + panel), TrayIconView, SpaceReserver, EdgeTriggerWindow, TrayIconManager
├── Controls/                WidgetCard, DockZonesPanel, RingGauge, AnalogClock, WeatherIcon, TickBar, Sparkline, Glyphs, SettingRow
├── Settings/                Ayarlar penceresi (Mica), widget galerisi, uygulama seçici, widget ayar şablonları
├── Themes/                  Dark.xaml, Light.xaml (renkler), Controls.xaml (stiller)
└── Widgets/                 WidgetBase, WidgetRegistry, CompactTile ve her widget için ayrı klasör
    ├── Clock/  WorldClock/  Timers/ (kronometre, odak, geri sayım, alarm)  TimeProgress/
    ├── Hydration/  Reminders/  Notes/  Media/
    └── System/ (CPU-bellek, ağ, durum)  Weather/
```

## Yeni widget ekleme

1. `Widgets/Ornek/OrnekWidget.xaml` dosyasını ekleyin. Kök eleman `w:WidgetBase` olmalıdır. Farklı görünümler için `Layout_<varyant>` adlı öğeler kullanın:

   ```xml
   <w:WidgetBase x:Class="CustomDock.Widgets.OrnekWidget"
                 xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                 xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                 xmlns:w="clr-namespace:CustomDock.Widgets">
       <Grid>
           <TextBlock x:Name="Layout_big" Style="{StaticResource ValueText}" VerticalAlignment="Center" />
           <TextBlock x:Name="Layout_small" Style="{StaticResource TitleText}" VerticalAlignment="Center" />
       </Grid>
   </w:WidgetBase>
   ```

2. Code-behind dosyasında yaşam döngüsü metotlarını uygulayın. `OnAttached` içinde servislere abone olun, `OnDetached` içinde aboneliği bırakın. Dikey dock için `UpdateCompact` metodunu doldurun:

   ```csharp
   public sealed class OrnekSettings : ObservableObject
   {
       private string _text = "Merhaba";
       public string Text { get => _text; set => Set(ref _text, value); }
   }

   public partial class OrnekWidget : WidgetBase
   {
       private OrnekSettings _settings = new();

       public OrnekWidget() => InitializeComponent();

       protected override void OnAttached()
       {
           _settings = GetSettings<OrnekSettings>();          // öğenin config.json ayarları; değişince kaydedilir
           _settings.PropertyChanged += (_, _) => Render();
           AppServices.Clock.MinuteTick += OnTick;            // paylaşılan, hizalı zamanlayıcı
       }

       protected override void OnDetached() => AppServices.Clock.MinuteTick -= OnTick;

       protected override void OnVariantChanged()             // ilk bağlanmada ve görünüm değişince
       {
           ShowLayout(Layout_big, Layout_small);
           Render();
       }

       private void OnTick(object? s, DateTime now) => Render();

       private void Render()
       {
           Layout_big.Text = Layout_small.Text = _settings.Text;
           RefreshCompact();                                  // dikey dock kutucuğunu da güncelle
       }

       protected override void UpdateCompact(CompactTile tile)
       {
           tile.ShowGlyph(Descriptor.Icon, Descriptor.AccentKey);
           tile.Text = _settings.Text[..Math.Min(4, _settings.Text.Length)];
       }
   }
   ```

3. Widget'ı `Widgets/WidgetRegistry.cs` içindeki listeye ekleyin:

   ```csharp
   new()
   {
       Id = "ornek", Name = "Örnek", Category = WidgetCategories.Notes, Description = "…",
       IconPath = "M4,4 H20 V20 H4 Z", AccentKey = "AccentGreenBrush",
       Variants = new[] { new WidgetVariant("big", "Büyük"), new WidgetVariant("small", "Küçük") },
       Factory = () => new OrnekWidget(), SettingsType = typeof(OrnekSettings),
   },
   ```

4. (İsteğe bağlı) Ayar arayüzü için `Settings/WidgetSettingsTemplates.xaml` dosyasına `DataType="{x:Type w:OrnekSettings}"` olan bir `DataTemplate` ekleyin. Daha karmaşık arayüzler için `SettingsViewFactory` ile bir UserControl döndürebilirsiniz (bkz. `WeatherSettingsView`).

Widget galeride kendiliğinden görünür. Diğer yardımcılar:

- **Popup:** `OpenPopup(popup)` popup'ı dock kenarına göre doğru yönde açar ve açıkken otomatik gizlemeyi durdurur. Dikey dock'ta kutucuğa hizalanır.
- **Gizlenme:** Widget kendi `Visibility` değerini `Collapsed` yaparsa kartı da dock'tan kalkar.
- **Kutucuk tıklaması:** `OnCompactClick` `true` döndürürse dikey dock'ta kutucuğa tıklamak panel açmak yerine o eylemi yapar.

## Performans

- **Zamanlayıcılar:**
  - Saat tabanlı widget'lar tek bir paylaşılan, saniye/dakika hizalı zamanlayıcıyı kullanır. Saniyeye ihtiyaç yoksa dakikada bir çalışır, abone yoksa durur.
  - Hiçbir zamanlayıcı 1 saniyeden sık çalışmaz. İstisnalar: görev çubuğu denetleyicisi (250 ms, tek bir Win32 çağrısı) ve Başlat menüsü görünürlük yoklaması (200 ms).
- **İzleme:**
  - Sistem izleme `GetSystemTimes` / `GlobalMemoryStatusEx` kullanır. Disk doluluğu dakikada bir ölçülür.
  - Ağ ölçümü yalnızca ağ widget'ı varken çalışır.
- **Olay tabanlı güncellemeler:**
  - Pencere listesi ve tam ekran algılama ManagedShell'in kabuk kancalarıyla (olay tabanlı) güncellenir.
  - Medya SMTC olaylarıyla güncellenir; ilerleme çubuğu yalnızca çalarken saniyede bir güncellenir.
- **Hava durumu:** aynı konumu kullanan widget'lar tek bir istek ve önbelleği paylaşır. Veri 30 dakikada bir yenilenir, hata olursa 3 dakika sonra tekrar denenir.
- **Kaydırma animasyonu:** yalnızca kaydırma sırasında çalışır.

## Bilinen kısıtlamalar

- **Pencere efektleri:**
  - Bulanık cam efekti `SetWindowCompositionAttribute` ile uygulanır (pencere etkin olmasa da çalışır).
  - Köşe yarıçapı Windows'un DWM yuvarlamasıdır (~8 px). Windows 10'da köşeler kare kalır.
- **Kabuk entegrasyonu:**
  - Windows 11'in yeni XAML tepsi öğeleri (ağ/ses/pil hızlı ayarları, dil çubuğu) Explorer'a aittir. Dock klasik tepsi simgelerini gösterir.
  - Windows'un "çalışan uygulama ilerleme çubuğu" bilgisi, Windows tuşu Explorer'da kaldığı için bazı uygulamalarda gösterilmeyebilir.
- **Çoklu monitör:** Dock tek bir monitörde gösterilir; ikincil monitörlerde Windows görev çubuğu da gizlenir.

## Lisanslar ve teşekkürler

- [ManagedShell](https://github.com/cairoshell/ManagedShell): Apache-2.0
- Başlat menüsü (`IImmersiveLauncher`), tepsi ikonu fare iletimi ve "masaüstünü göster" teknikleri [RetroBar](https://github.com/dremin/RetroBar) projesinden uyarlanmıştır (Apache-2.0).
- Hava durumu: [Open-Meteo](https://open-meteo.com/) (CC BY 4.0)
