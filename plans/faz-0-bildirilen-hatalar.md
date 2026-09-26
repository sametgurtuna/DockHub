# Faz 0: Bildirilen Hatalar

> Öncelik: 🔴 Kritik · Boyut: M · Bağımlılık: yok
> Dayanak: [00-bildirilen-hatalar-analizi.md](00-bildirilen-hatalar-analizi.md)

## Amaç

Kullanıcının günlük kullanımda karşılaştığı üç sorunu kökten çözmek:

1. Uygulama ikonlarının kaybolması
2. Önizlemede orta tıkla pencere kapatılamaması
3. Discord'un (ve benzer "tray'e kapanan" uygulamaların) dock'ta görünmemesi

Çözümlerin sonradan doğrulanabilmesi için önce tanılama altyapısı kurulur.

## Kapsam dışı

- Pinlenmemiş uygulamalar alanının genel tasarımı (Faz 5). Burada yalnızca "görünür alan dışında kalan yeni uygulama" göstergesi eklenir.
- ManagedShell'i fork'lamak veya sürümünü yükseltmek. Önce mevcut sürümün public API'siyle çözülür; mümkün değilse görev 0.5'teki karar noktasına bakılır.

---

## Görev 0.1: Tanılama altyapısı

**Neden:** İkon ve pencere sorunlarında şu an hiçbir log yok. Düzeltmelerin işe yaradığını kanıtlamanın tek yolu bu.

1. `Core/Log.cs`'e `Log.Debug(string)` ekle. Yalnızca `config.json` → `"debugLogging": true` (yeni `AppConfig.DebugLogging`, varsayılan `false`, ayarlar arayüzüne eklenmez) veya `DOCKHUB_DEBUG=1` ortam değişkeni varken yazsın.
2. `Native/ShellIcons.cs` → `GetIcon`: her yöntemin (`GetShellItemImage`, `GetFileInfoIcon`, `ExtractAssociatedIcon`) başarısızlığını ve son sonuç `null` olduğunda `File.Exists(path)` bilgisini `Log.Debug` ile yaz. Aynı yol için aynı mesajı tekrar yazmamak için `HashSet<string>` ile tekilleştir.
3. `Native/ShellIcons.cs` → `ReadShortcut`: başarısız okumayı `Log.Warn` ile yaz (şu an sadece exception durumunda yazıyor).
4. Yeni `Shell/WindowDiagnostics.cs`: `string Dump(ShellHost shell, AppConfig config)` metodu. Çıktıya şunlar girsin:
   - ManagedShell `Tasks` servisindeki **tüm** `ApplicationWindow`'lar (filtresiz koleksiyon): hwnd, başlık, sınıf, exe, AUMID, cache'lenmiş `ShowInTaskbar`, anlık `CanAddToTaskbar`, `IsWindowVisible`, `WS_EX_*` bayrakları, owner, `ITaskList_Deleted`, `AppKeys.ForWindow` sonucu.
   - Pinli her öğe için `AppKeys.ForItem` sonucu, yolun var olup olmadığı ve eşleşen `AppGroup`.
   - `RunningAppsService.Groups` listesi.
5. Ayarlar → Hakkında sayfasına "Tanılama bilgisini kopyala" satırı ekle ([Settings/SettingsWindow.xaml](../src/CustomDock/Settings/SettingsWindow.xaml) About bölümü). Tıklanınca `Dump` çıktısı panoya kopyalanır ve `log.txt`'e de yazılır.
6. [`plans/tools/window-diag.ps1`](tools/window-diag.ps1) harici betik olarak hazır; README'nin "Known limitations/FAQ" bölümüne "bir uygulama dock'ta görünmüyorsa" başlığıyla kullanımını ekle.

**Kabul:** Discord açıkken "Tanılama bilgisini kopyala" çıktısında Discord'un ana penceresi, cache'lenmiş `ShowInTaskbar` değeriyle birlikte görünür.

---

## Görev 0.2: Sağlam ikon hattı (Sorun 1: K1, K3, K4)

### 0.2.1 Tek giriş noktası: `AppIcons`
Yeni `Shell/AppIcons.cs` (statik):

```csharp
/// Icon for a pinned item. Never returns null: falls back to the generic app icon.
public static ImageSource For(DockItem item, int sizePx, out bool isFallback);
```

Sıra:
1. `shell:AppsFolder\...` → `ShellIcons.GetIcon(path)`
2. `.lnk` → hedef `.exe` varsa onun ikonu (bugünkü `AppButton.IconFor` mantığı buraya taşınır), yoksa `.lnk`'nin kendi ikonu, yoksa `.lnk`'nin `IconLocation` alanı (`IShellLinkW.GetIconLocation` arayüze eklenir)
3. `.exe` / diğer → `ShellIcons.GetIcon(path)`
4. Yol yoksa → Görev 0.3'teki `AppPathResolver.Repair(path)` ile çözülen güncel yol denenir
5. Hepsi başarısızsa `ShellIcons.GetDefaultAppIcon()` ve `isFallback = true`

`AppButton.IconFor` ([Dock/AppButton.cs:233](../src/CustomDock/Dock/AppButton.cs)) ve `GroupItemView.GetChildIcon` ([Dock/GroupItemView.cs:232](../src/CustomDock/Dock/GroupItemView.cs)) bu metodu çağıracak şekilde değiştirilir. `GroupItemView` artık `null` yüzünden ikonu `Collapsed` yapmaz.

### 0.2.2 Yeniden deneme
`AppButton`'a `_iconIsFallback` alanı eklenir. İkon yedek (fallback) ise:
- 1 s, 3 s, 10 s, 30 s, 120 s aralıklarla `AppIcons.For` yeniden denenir (tek bir `DispatcherTimer`, her denemede interval büyür; başarıda durur).
- Bu uygulamanın bir penceresi açıldığında (`Group` set edilince) `group.Icon` yedek ikonun yerine geçer; `Refresh()`'teki koşul `Item is null || _icon.Source is null || _iconIsFallback` olur.
- `ShellIcons.ClearCache(path)` her yeniden denemeden önce çağrılır.

### 0.2.3 Global yenileme tetikleri
`DockWindow` içinde, tüm `AppButton` ve `GroupItemView`'lar için `RefreshIcons()` çağıran tek bir metod:
- `SystemEvents.PowerModeChanged` → `Resume`
- `SystemEvents.DisplaySettingsChanged`
- `TaskbarCreated` mesajı (Explorer yeniden başladığında; `ShellHost` zaten dinliyor olabilir, kontrol et)
- Uygulama açılışından 15 s sonra bir kez (oturum açılışı yarışı için)

`RefreshIcons()` yalnızca yedek ikonlu butonları yeniden yükler; başarılı ikonlara dokunmaz.

### 0.2.4 Kısayol cache'i
`ShellIcons.ReadShortcut`: `(null, null)` sonucu cache'lenmez, ya da 60 s TTL ile cache'lenir. Başarılı sonuçlar dosyanın `LastWriteTimeUtc` değeriyle birlikte cache'lenir; değişmişse yeniden okunur.

### 0.2.5 İkon cache'inin sınırlanması
`IconCache` sınırsız büyüyor. 256 girişlik basit bir LRU'ya çevrilir (Faz 9'a bırakılabilir, ama burada dokunulduğu için yapılması önerilir).

**Kabul:**
- Oturum açılışında otomatik başlatmada ikon kaybolmaz (10 yeniden başlatma denemesi).
- Pinli bir `.exe`'nin adı geçici olarak değiştirilip geri alındığında ikon en geç 30 s içinde geri gelir.
- Klasör önizlemesinde ikonu bulunamayan öğe gri varsayılan ikonla görünür, boşluk bırakmaz.

---

## Görev 0.3: Sürümlü yolların kalıcı çözümü (Sorun 1: K2, Sorun 3: D3)

### 0.3.1 `Shell/AppPathResolver.cs` (yeni, saf mantık, Faz 2'de test edilecek)

```csharp
/// Stable path to store when pinning a running app (never a versioned install folder).
public static string StablePinPath(string exePath, string? aumid);

/// Version-independent matching key for an executable path.
public static string NormalizeExeKey(string exePath);

/// Finds the current location of a pinned path whose versioned folder no longer exists.
public static string? Repair(string path);
```

Tanınacak kalıplar:

| Tür | Örnek | Kararlı pin yolu | Normalize anahtar |
|---|---|---|---|
| Squirrel | `...\Discord\app-1.0.9259\Discord.exe` | Kökte aynı adlı stub varsa `...\Discord\Discord.exe`, yoksa `...\Discord\Update.exe` + `Arguments = "--processStart Discord.exe"` | `...\discord\app-*\discord.exe` |
| MSIX | `C:\Program Files\WindowsApps\Claude_2.9939.2.0_x64__pzs8sxrjxfjjc\app\claude.exe` | Pencerenin AUMID'i varsa `shell:AppsFolder\<AUMID>` | `msix:claude__pzs8sxrjxfjjc\app\claude.exe` (aile adı + paket içi yol) |
| Diğer | `C:\Program Files\App\app.exe` | Değişmez | Değişmez |

Squirrel regex: `\\app-\d+(\.\d+){1,3}(-[^\\]+)?\\` (büyük/küçük harf duyarsız).
MSIX regex: `\\WindowsApps\\(?<name>[^_\\]+)_(?<ver>[\d.]+)_(?<arch>[^_\\]*)_[^_\\]*_(?<pub>[^\\]+)\\`.

`Repair`:
- Squirrel: üst klasördeki en yüksek sürümlü `app-*` klasöründe aynı dosya adı.
- MSIX: `Windows.Management.Deployment.PackageManager().FindPackagesForUser("", familyName)` → `InstalledLocation.Path` + paket içi yol. (WindowsApps klasörünü listelemek ACL yüzünden başarısız olur, bu yüzden API kullanılır. TFM zaten `net8.0-windows10.0.19041.0`, WinRT projeksiyonu mevcut.)

### 0.3.2 Entegrasyon
1. `AppLauncher.PinnablePath` ([Services/AppLauncher.cs:103](../src/CustomDock/Services/AppLauncher.cs)) `StablePinPath` kullanır. Squirrel durumunda `DockItem.Arguments` da doldurulur, bu yüzden `PinnablePath` imzası `(string Path, string? Arguments)` döndürecek şekilde değişir; çağıranlar güncellenir (sürükle-bırak pinleme `DockWindow.Items.cs`, bağlam menüsü "Pin to dock").
2. `AppKeys.ForWindow` ve `AppKeys.ForItem` `exe:` anahtarlarını `NormalizeExeKey` ile üretir. `.lnk` hedefi `Update.exe` olan ve AUMID'i `com.squirrel.*` olan kısayollar için anahtar, `NormalizeExeKey(<kök>\app-*\<ad>.exe)` olur (Discord'un Başlat menüsü kısayolu da doğru eşleşsin diye).
3. **Config onarımı:** `ConfigService.Load` sonrası tüm App öğeleri (klasör içleri dahil) taranır. Yolu mevcut olmayan ve `Repair` ile bulunan öğeler güncellenir, Squirrel/MSIX sürümlü yollar kararlı yola çevrilir. Her değişiklik `Log.Info` ile yazılır. Bu bir şema değişikliği değildir, `CurrentVersion` artmaz.
4. `DockWindow._itemKeys` ([Dock/DockWindow.xaml.cs:46](../src/CustomDock/Dock/DockWindow.xaml.cs)) hiçbir yerde temizlenmiyor. Öğe kimliğine göre cache'lendiği için yolu onarılan veya ayarlardan "Target" alanı değiştirilen öğe eski anahtarla eşleşmeye devam ediyor. `ItemsChanged` sırasında ve öğenin `Path` değeri değiştiğinde ilgili girdi silinmeli.

**Kabul:**
- Kullanıcının config'indeki Discord ve Claude yolları açılışta kararlı yollara çevrilir.
- Discord'un `app-1.0.9259` klasörü `app-1.0.9999` olarak yeniden adlandırılıp Discord kökteki stub'dan başlatıldığında pinli ikon "çalışıyor" gösterir ve sonda ikinci bir Discord ikonu oluşmaz. (Test sonrası klasör adı geri alınır.)

---

## Görev 0.4: Önizlemede orta tık ile kapatma (Sorun 2)

Dosya: [Dock/WindowPreviewWindow.cs](../src/CustomDock/Dock/WindowPreviewWindow.cs)

1. Kart oluşturma döngüsünde `card.MouseUp` handler'ı ekle:
   ```csharp
   card.MouseUp += (_, e) =>
   {
       if (e.ChangedButton != MouseButton.Middle) return;
       e.Handled = true;
       CloseWindowFromPreview(w, card);
   };
   ```
   `MouseDown` yerine `MouseUp` kullanılır (Windows davranışı; yanlışlıkla basmayı sürükleyerek iptal etmeye izin verir). Orta tuşa basılıp kart dışına çıkılırsa kapatılmaz: `MouseDown`'da kart `CaptureMouse` yapar, `MouseUp`'ta `IsMouseOver` kontrol edilir.
2. Kapat butonu ve orta tık ortak `CloseWindowFromPreview(ApplicationWindow w, FrameworkElement card)` metodunu kullanır:
   - `w.Close()` çağrılır.
   - Kart hemen `Motion.PopOut` ile gizlenir ve `_previewItems`'dan çıkarılır, DWM thumbnail'i `DwmUnregisterThumbnail` ile bırakılır.
   - Kalan kart yoksa `HidePreview()`.
   - Pencere "Kaydetmek istiyor musunuz?" gibi bir diyalog açıp kapanmazsa: `AppGroup.PropertyChanged(WindowCount)` gelene kadar bir şey yapılmaz; kullanıcı önizlemeye tekrar geldiğinde pencere doğal olarak yine listelenir.
3. Hardcode edilmiş `DockEdge.Bottom` kaldırılır. `ShowFor` çağrısı, butonun bağlı olduğu dock'un kenarını kullanır. `ShowFor` son kullanılan `edge` değerini bir alanda tutar (`_currentEdge`), yeniden gösterimde onu kullanır.
4. Grup değişimini dinle: `ShowFor` açıkken `_currentGroup.PropertyChanged` abone olunur. `WindowCount` değişirse kartlar yeniden kurulur (kapatılan pencere düşer, yeni açılan eklenir). `HidePreview`'da abonelik kaldırılır.
5. `AppButton.OnMiddleDown` ([Dock/AppButton.cs:380](../src/CustomDock/Dock/AppButton.cs)) başında `WindowPreviewWindow.Instance.HidePreview()` çağrılır.
6. Kart tooltip'i: "Tıkla: öne getir · Orta tık: kapat".

**Kabul:**
- 3 pencereli bir uygulamada önizlemede ortadaki karta orta tıklanınca o pencere kapanır, kalan iki kart yerinde kalır ve önizleme kapanmaz.
- Son pencereye orta tıklanınca önizleme kapanır.
- Dock sol/sağ/üst kenardayken kapatma sonrası önizleme doğru yerde kalır.
- Kaydedilmemiş belgesi olan Notepad penceresinde orta tık, Notepad'in kaydetme diyaloğunu açar; DockHub hata vermez.

---

## Görev 0.5: Discord ve "gizli oluşturulan" pencereler (Sorun 3: D1, D2)

### 0.5.1 Ön inceleme (karar noktası)
ManagedShell 0.0.372 derlemesinin public yüzeyini doğrula (küçük bir `dotnet script` veya test projesiyle reflection):
- `ShellManager.TasksService` (veya eşdeğeri) erişilebilir mi?
- `ApplicationWindow.SetShowInTaskbar()` ve `UpdateProperties()` public mi?
- `TasksService.Windows` koleksiyonu public ve yazılabilir mi? `new ApplicationWindow(TasksService, IntPtr)` constructor'ı public mi?

Sonuca göre A veya B yolu seçilir ve bu dosyanın "Uygulama notları" bölümüne yazılır.

### 0.5.2 Görünürlük izleyici (her iki yolda ortak)
Yeni `Shell/WindowVisibilityWatcher.cs`:
- `SetWinEventHook(EVENT_OBJECT_SHOW (0x8002), EVENT_OBJECT_HIDE (0x8003), ..., WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS)` ve ayrıca `EVENT_OBJECT_CLOAKED (0x8017)` ile `EVENT_OBJECT_UNCLOAKED (0x8018)`.
- Filtre: `idObject == OBJID_WINDOW (0)`, `idChild == 0`, `GetAncestor(hwnd, GA_ROOT) == hwnd` (yalnızca üst seviye pencereler).
- Olaylar 150 ms debounce ile toplanır (Electron açılışta çok sayıda SHOW üretir).
- Delegate referansı alan olarak tutulur (GC tarafından toplanmasın), `Dispose`'da `UnhookWinEvent`.
- Yalnızca `Replace` ve `ShowBoth` modlarının ikisinde de çalışır; maliyeti çok düşük.

### 0.5.3 Yol A (ManagedShell API'si yeterliyse)
Debounce sonrası her hwnd için:
- ManagedShell'in `Windows` koleksiyonunda varsa → `window.UpdateProperties()` (veya `SetShowInTaskbar()`).
- Yoksa ve `CanAddToTaskbar` kuralını sağlıyorsa → ManagedShell'e ekle (public API ile).

### 0.5.4 Yol B (API yetersizse)
`RunningAppsService` içinde ek bir kaynak tutulur:
- `_supplemental: Dictionary<IntPtr, ApplicationWindow>`: izleyicinin bulduğu, ManagedShell'in listesinde olmayan veya `ShowInTaskbar=false` cache'lenmiş ama gerçekte uygun olan pencereler.
- `Rebuild()` bu pencereleri de gruplara ekler. Pencere kapanınca veya gizlenince (`EVENT_OBJECT_HIDE`, `EVENT_OBJECT_DESTROY`) çıkarılır.
- Uygunluk DockHub tarafında `CanAddToTaskbar` kuralının kopyasıyla hesaplanır (`Shell/TaskbarEligibility.cs`, analiz dosyasındaki kural). ManagedShell'in cache'ine güvenilmez.
- Son çare: reflection ile `SetShowInTaskbar` çağrısı. Yapılırsa `try/catch` ile sarılır ve başarısızlıkta Yol B'nin yukarıdaki kısmına düşülür.

### 0.5.5 Savunma amaçlı küçük düzeltmeler
- `RunningAppsService.WatchedProperties` ([Shell/RunningAppsService.cs:136](../src/CustomDock/Shell/RunningAppsService.cs)) kümesine `"ShowInTaskbar"` ve `"HMonitor"` eklenir.
- **D2 (yalnızca tanılama doğrularsa):** `ITaskList_Deleted` işaretli ama görünür, aktif ve `WS_EX_APPWINDOW`'lu bir pencere, 2 s boyunca öyle kalırsa DockHub bu pencereyi uygun sayar (başka sürecin penceresindeki özellik silinmez, yalnızca yok sayılır). Tanılama D2'yi doğrulamazsa bu madde atlanır ve notlara yazılır.

**Kabul:**
- DockHub açıkken Discord tray'den açıldığında 1 s içinde dock'ta görünür (10 deneme).
- Discord çalışırken DockHub yeniden başlatıldığında, Discord tray'deyse görünmez; pencere açıldığında görünür.
- Discord tray'e kapatıldığında dock'tan kaybolur (çalışıyor göstergesi söner).
- Aynı testler Spotify ve Slack (veya kurulu başka bir Electron uygulaması) ile tekrarlanır.
- Boştayken (idle) DockHub CPU kullanımı değişmez (Görev Yöneticisi'nde 5 dk gözlem, %0.x seviyesinde kalmalı).

---

## Görev 0.6: Görünür alan dışında kalan yeni uygulamalar (D4, küçük)

`DockWindow.RefreshRunningApps` yeni bir pinlenmemiş grup eklediğinde, o buton kaydırılabilir alanın görünür kısmında değilse:
- Sağ (dikeyde alt) kaydırma okunun üzerinde 6 px'lik accent renkli bir nokta gösterilir.
- Kullanıcı o yöne kaydırınca veya 10 s geçince nokta kaybolur.
- Grup `IsFlashing` (dikkat istiyor) ise nokta turuncu olur.

**Kabul:** Taşan bir dock'ta yeni açılan pinlenmemiş uygulama için okta nokta belirir.

---

## Test planı (faz sonu)

1. `dotnet build CustomDock.sln -c Release` uyarısız.
2. Görev 0.1-0.6'daki kabul kriterleri tek tek denenir; sonuçlar "Uygulama notları"na yazılır.
3. Regresyon: pinli uygulamaya tıklama, Shift+tık, orta tık (yeni pencere), sürükle-bırakla pinleme, Explorer "Pin to DockHub", klasöre sürükleme, sağ tık Jump List.
4. Görev çubuğu geri gelme: normal çıkış, `--restore-taskbar`, Görev Yöneticisi'nden sonlandırma.

## Commit önerisi

Görev başına ayrı commit:
- `feat(diag): add icon/window diagnostics and debug logging`
- `fix(icons): never leave app buttons blank, retry failed icon loads`
- `fix(apps): pin stable paths for Squirrel/MSIX apps and repair versioned pins`
- `fix(preview): middle-click closes a window from the thumbnail preview`
- `fix(tasks): show windows that become visible after creation (Discord, Electron apps)`
- `feat(dock): hint when a new running app is scrolled out of view`

## Uygulama notları


### 2026-09-26 — tamamlandı

- **0.5 kararı: Yol A (hibrit).** ManagedShell 0.0.372'de `ApplicationWindow.SetShowInTaskbar()` public. Filtresiz pencere listesine `Tasks.GroupedWindows` (`CollectionView.SourceCollection`) üzerinden ulaşılıyor. Listede olmayan pencereler için private `TasksService.addWindow` reflection ile çağrılıyor (TaskbarButtonCreated mesajı da gönderilsin diye). Bulunamazsa `new ApplicationWindow(...)` ile listeye ekleniyor.
- **D2 (`ITaskList_Deleted`) doğrulanmadı:** Discord'un ana penceresinde bayrak yok (`window-diag.ps1`: `TaskListDeleted=False`, `TaskbarEligible=True`). Bu yüzden özel bir işlem eklenmedi.
- **Paketlenmiş uygulamalar:** WindowsApps altındaki süreçler artık AUMID ile eşleşiyor. AUMID pencereden, yoksa `GetApplicationUserModelId` ile süreçten alınıyor. Pin tarafında ise `PackageManager` kullanılıyor.
- **Canlı doğrulama (kullanıcının config'iyle):**
  - Açılışta Discord `app-1.0.9259\discord.exe` → `discord\discord.exe`, Claude `WindowsApps\claude_2.9939...` → `shell:AppsFolder\Claude_pzs8sxrjxfjjc!Claude` olarak onarıldı.
  - Discord dock'ta "çalışıyor" göstergesi ve rozetiyle görünüyor.
  - Önizlemede orta tık pencereyi kapattı, kart ve gösterge anında düştü.
  - Tray'den yeniden açılan Discord dock'ta anında aktif göründü.
- **Kanıt:** Eski log'da 22-23 Eylül'de Claude güncellemesinden sonra `claude_2.2553...` yolu için "Failed to launch application" hataları var (K2 gerçekten yaşanmış).
- **Diğer bulgular (sonraki fazlara):**
  - Win+Alt+D kaydı başarısız, Ctrl+Alt+D'ye düşüyor (Windows 11 Win+Alt+D'yi takvim için kullanıyor) → Faz 3.2.
  - HyperX pil okuma her 30 s'de INFO log yazıyor → Faz 9.
  - 24 Eylül'de `config.json` null byte'larla bozulmuş → Faz 4.2'deki otomatik yedek önemli.
  - Klasör içinde pinli bir uygulama çalışırken dock'un sonunda ayrıca görünüyor → Faz 7.
