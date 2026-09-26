# Bildirilen Hataların Analizi

Kullanıcının bildirdiği üç sorunun kök neden analizi. Çözümler [Faz 0](faz-0-bildirilen-hatalar.md)'da uygulanır.

Analiz; kaynak kod, kullanıcının `%AppData%\DockHub\config.json` dosyası ve ManagedShell 0.0.372'nin kaynağı (`cairoshell/ManagedShell`) üzerinden yapıldı.

---

## Sorun 1: Bazı uygulamaların ikonları kayboluyor (görünmez oluyor)

### Belirti
Dock'taki bazı uygulama butonları yerinde duruyor (hover efekti, tıklama çalışıyor) ama ikon görünmüyor. Sorun "bazen" oluyor, yeniden başlatınca ya da dock'ta bir değişiklik yapınca bazen düzeliyor.

### İkon akışı (mevcut kod)

```
AppButton ctor  ──► IconFor(path)                       Dock/AppButton.cs:224
                     ├─ .lnk ise hedef .exe'yi çöz        ShellIcons.ReadShortcut (sonuç kalıcı cache'lenir)
                     └─ ShellIcons.GetIcon(path, 96)     Native/ShellIcons.cs:20
                          ├─ IShellItemImageFactory.GetImage
                          ├─ SHGetFileInfo
                          └─ Icon.ExtractAssociatedIcon (sadece dosya varsa)
                          → hepsi başarısızsa: null (null cache'lenmez, ama kimse tekrar denemez)

AppButton.Refresh()                                      Dock/AppButton.cs:239
   if (Item is null || _icon.Source is null)
       _icon.Source = group?.Icon ?? _icon.Source;       ← uygulama çalışmıyorsa group null → Source null kalır
```

### Kök nedenler (olasılık sırasıyla)

**K1. Geçici hata sonrası yeniden deneme yok (en olası, "bazen" belirtisini açıklıyor).**
`AppButton` ikonunu yalnızca oluşturulurken bir kez yükler. `GetIcon` o anda `null` dönerse ikon kalıcı olarak boş kalır. Uygulama çalışmıyorsa `Refresh()` onu da doldurmaz. `null` dönmesinin tipik nedenleri:
- **Oturum açılışında otomatik başlatma (`--startup`):** Explorer'ın ikon önbelleği ve shell henüz hazır değilken `IShellItemImageFactory` ve `SHGetFileInfo` başarısız olabilir.
- **Uygulama güncellenirken:** Squirrel (Discord), MSIX (Claude) ve Electron updater'ları dosyaları değiştirirken exe kısa süreliğine kilitli olur veya yerinde bulunmaz.
- **COM/STA zamanlaması:** Görev çubuğu yeniden oluşturulurken (`TaskbarCreated`) shell çağrıları geçici olarak hata dönebilir.

Dock yeniden kurulunca (öğe ekleme/silme, ayar değişimi) `AppButton` yeniden oluşturulur ve ikon yeniden denenir. Bu yüzden sorun "kendiliğinden düzeliyor" gibi görünür.

**K2. Sürüm numarası içeren yollar (kesin olarak tekrarlayacak).**
Çalışan bir uygulama dock'a sürüklenerek pinlendiğinde `AppLauncher.PinnablePath` ([Services/AppLauncher.cs:103](../src/CustomDock/Services/AppLauncher.cs)) pencerenin gerçek exe yolunu kaydeder. Kullanıcının config'inde:

```
c:\users\samet\appdata\local\discord\app-1.0.9259\discord.exe                   (Squirrel, her güncellemede klasör değişir)
c:\program files\windowsapps\claude_2.9939.2.0_x64__pzs8sxrjxfjjc\app\claude.exe (MSIX, her güncellemede klasör değişir)
```

Uygulama güncellendiğinde eski klasör silinir, yol artık yoktur ve `GetIcon` `null` döner. Sonuç: ikon kalıcı olarak kaybolur. Ek olarak pinli buton ile çalışan pencere eşleşmez (anahtar `exe:` yoluna göre), bu yüzden aynı uygulama sonda ikinci bir ikon olarak da görünür. Claude, bir klasörün (group) içinde pinli. Klasör önizlemesinde `GroupItemView` `null` ikonu `Collapsed` yapar ([Dock/GroupItemView.cs:219](../src/CustomDock/Dock/GroupItemView.cs)), yani klasörde de ikon "kaybolur".

**K3. Kısayol çözümleme hatası kalıcı olarak cache'leniyor.**
`ShellIcons.ReadShortcut` hata durumunda `(null, null)` sonucunu `LinkCache`'e yazar ve uygulama kapanana kadar tekrar denemez. Geçici bir hata, o `.lnk` için hem ikonu hem çalışan uygulama eşleşmesini oturum boyunca bozar.

**K4. Klasör alt ikonları farklı bir yol kullanıyor.**
`GroupItemView.GetChildIcon`, `IconFor` yerine doğrudan `GetIcon(child.Path, 48)` çağırır. `.lnk` hedefini çözmez ve fallback'i yoktur.

**K5. (Düşük olasılık) Uyku/ekran değişimi sonrası render kaybı.**
K1-K4 düzeltildikten sonra sorun sürerse, uykudan dönüş ve ekran değişiminde (`SystemEvents.PowerModeChanged`, `DisplaySettingsChanged`) ikonların yeniden yüklenmesi denenmeli.

### Kanıt eksikliği
Şu an `GetIcon` başarısız olduğunda (exception olmadan `null` döndüğünde) hiçbir log yazılmıyor. Faz 0'da tanılama logu ekleniyor.

---

## Sorun 2: Preview penceresinde orta tık pencereyi kapatmıyor

### Belirti
Uygulamanın üzerine gelince açılan canlı önizleme kartında orta tuşa basınca hiçbir şey olmuyor. Windows görev çubuğunda aynı hareket o pencereyi kapatır.

### Kök neden (kesin)
[Dock/WindowPreviewWindow.cs](../src/CustomDock/Dock/WindowPreviewWindow.cs) içinde her kart (`card`) için yalnızca şunlar bağlı:
- `MouseEnter` / `MouseLeave` (hover rengi)
- `MouseLeftButtonUp` (pencereyi öne getir)
- Kapat butonu `Click` → `w.Close()`

**Orta tuş için hiçbir handler yok.** Bu eksik bir özellik; olay hiçbir yerde işlenmiyor.

### Aynı bölgede ek hatalar
1. **Kapat sonrası yeniden gösterim yanlış kenarı kullanıyor:** Kapat butonu handler'ı `ShowFor(_currentButton, _currentGroup, DockEdge.Bottom)` çağırıyor. Dock üstte, solda veya sağdayken önizleme yanlış yerde açılır. `AppServices.Config.Edge` (veya ilgili dock'un kenarı) kullanılmalı.
2. **Kapatılan pencere listede kalıyor:** `w.Close()` yalnızca `WM_CLOSE` gönderir; pencere asenkron kapanır. Hemen ardından `Dispatcher.BeginInvoke(Background, ...)` ile liste yeniden kuruluyor, bu anda pencere hâlâ `_currentGroup.Windows` içinde olduğu için kapatılan kart tekrar çiziliyor. "Kaydetmek istiyor musunuz?" diyaloğu çıkaran pencerelerde ise pencere hiç kapanmayabilir. Doğru yaklaşım: kartı hemen gizleyip listeyi `AppGroup` değişim olayıyla (`RunningAppsService.GroupsChanged` / `AppGroup.PropertyChanged(WindowCount)`) güncellemek.
3. **Dock butonunda orta tık** yeni pencere açıyor (`AppButton.OnMiddleDown`). Bu Windows ile aynı davranış, doğru. Ancak önizleme açıkken butona orta tıklanırsa önizleme kapanmıyor; kapanmalı.

---

## Sorun 3: Discord açılıyor ama dock'ta görünmüyor

### Belirti
Discord açık (penceresi ekranda), ama dock'ta çalışan uygulama olarak görünmüyor. Pinli Discord ikonunda "çalışıyor" göstergesi, önizleme veya sonda ayrı bir ikon yok. Diğer uygulamalarda sorun yok.

### Pencerelerin dock'a gelme yolu

```
ManagedShell TasksService (shell hook: HSHELL_WINDOWCREATED / REDRAW / ACTIVATED)
   └─ ApplicationWindow.ShowInTaskbar   (CACHE'lenir: _showInTaskbar)
        = !cloaked && !immersive && CanAddToTaskbar
        CanAddToTaskbar = IsWindow && IsWindowVisible
                          && (owner == 0 || WS_EX_APPWINDOW)
                          && (!WS_EX_NOACTIVATE || WS_EX_APPWINDOW)
                          && !WS_EX_TOOLWINDOW
                          && GetProp("ITaskList_Deleted") == 0
   └─ Tasks.GroupedWindows (Filter: ShowInTaskbar)
        └─ DockHub RunningAppsService.Rebuild()   Shell/RunningAppsService.cs:198  (.Where(w => w.ShowInTaskbar))
             └─ AppKeys.ForWindow → "exe:<tam yol>"   Shell/AppKeys.cs:16
                  └─ DockWindow.RefreshRunningApps: pinli anahtarla eşleştir, değilse sona ekle
```

### Kök nedenler (olasılık sırasıyla)

**D1. ManagedShell'in `ShowInTaskbar` cache'i Electron'un "gizli oluştur, sonra göster" davranışını kaçırıyor (en olası).**
- ManagedShell açılışta mevcut pencereleri tararken yalnızca **o anda görünür** olanları ekliyor (`getInitialWindows`: `CanAddToTaskbar && ShowInTaskbar`).
- `ShowInTaskbar` bir kez hesaplanıp cache'leniyor. Yalnızca `HSHELL_WINDOWCREATED` (pencere zaten listedeyse), `HSHELL_REDRAW` ve `HSHELL_WINDOWACTIVATED` olaylarında yeniden hesaplanıyor. **Görünürlük değişimi tek başına yeniden hesaplamayı tetiklemiyor.**
- Discord (Electron) ana penceresini `show: false` ile **gizli oluşturur**, önce ayrı bir "Discord Updater" splash penceresi gösterir, sonra ana pencereyi gösterir. Oturum açılışında da genelde **tray'e küçültülmüş** başlar; dock'taki ikona tıklanınca ikinci `Discord.exe` ilk örneğe "göster" sinyali gönderir ve mevcut gizli pencere görünür olur.
- Bu senaryoda pencere ya hiç listeye girmiyor ya da `ShowInTaskbar = false` olarak cache'lenip öyle kalıyor. Discord'un başlığı uzun süre "Discord" olarak sabit kaldığından `HSHELL_REDRAW` da tetiklenmiyor. Pencereye tıklayıp aktif etmek (ACTIVATED) bazen düzeltir. Kullanıcının "bazen görünüyor, bazen görünmüyor" gözlemiyle uyumlu.
- Diğer uygulamaların çoğu penceresini görünür oluşturduğu için etkilenmiyor. Aynı sorun Slack, Teams (classic), WhatsApp masaüstü, Spotify gibi "tray'e kapanan" Electron/CEF uygulamalarında da görülebilir.

**D2. `ITaskList_Deleted` özelliği takılı kalıyor (olası, D1 ile birlikte).**
Discord tray'e küçülürken Electron `setSkipTaskbar(true)` → `ITaskbarList::DeleteTab` çağırır ve pencereye `ITaskList_Deleted` özelliği eklenir. Geri gösterirken `AddTab` bu özelliği kaldırmalı. DockHub görev çubuğunu devraldığında (`Replace` modu) `ITaskbarList` mesajlarının hangi pencereye gittiğine bağlı olarak özellik takılı kalabilir; bu durumda `CanAddToTaskbar` hep `false` döner.

**D3. Sürümlü yol uyuşmazlığı (Discord güncellenince kesin yaşanacak).**
Discord `app-1.0.9259\discord.exe` yoluyla pinli (Sorun 1, K2). Discord güncellenince çalışan pencerenin anahtarı `exe:...\app-1.0.9xxx\discord.exe` olur, pinli anahtarla eşleşmez. Pinli ikon "çalışmıyor" görünür ve ikonu kaybolur; çalışan Discord sonda ayrı bir ikon olarak eklenir.

**D4. Sondaki çalışan uygulamalar kaydırma alanının dışında kalıyor (katkıda bulunan etken).**
Pinlenmemiş çalışan uygulamalar `ItemsPanel`'in en sonuna eklenir. Kullanıcının dock'u dolu ve taşıyor (ekran görüntüsünde `< >` okları var), dolayısıyla sona eklenen ikon görünür alanın dışında kalabilir. D3 gerçekleştiğinde Discord "hiç yok" gibi görünür.

**D5. Çoklu monitör filtresi (düşük olasılık).**
Kullanıcıda `showOnAllDisplays: true` ve `runningAppsOnOwnDisplay: true`. Bu filtre yalnızca pinlenmemiş uygulamalara uygulanıyor. Discord penceresi ikinci monitördeyse ve D3 gerçekleşmişse, Discord yalnızca o monitörün dock'unda görünür.

### Doğrulama yöntemi
Discord açıkken [`tools/window-diag.ps1`](tools/window-diag.ps1) çalıştırılır. Betik, Discord'un tüm üst seviye pencerelerini stil, owner, görünürlük, cloaked ve `ITaskList_Deleted` bilgileriyle listeler:

```powershell
pwsh -File "C:\Users\samet\Desktop\Project\customdock\plans\tools\window-diag.ps1" -ProcessName Discord
```

Beklenen yorum:
- Ana pencere `Visible=True`, `ToolWindow=False`, `Owner=0`, `TaskListDeleted=False` ise pencere uygun demektir. Dock'ta yine görünmüyorsa sorun **D1** (cache).
- `TaskListDeleted=True` ise sorun **D2**.
- `Path` pinli yoldan farklıysa sorun **D3**.

Faz 0'da aynı bilgiyi uygulama içinden veren bir tanılama komutu da ekleniyor (ManagedShell'in cache'lediği `ShowInTaskbar` değeri dahil).
