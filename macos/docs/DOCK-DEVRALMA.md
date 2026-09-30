# DockHub macOS — Sistem Dock'u ile İlişki

`T7-DOCK-DEVRALMA` görevinin kaydı. Karar `d-dock-geri-yukleme`.

## Kısa cevap: devralamıyoruz

Windows sürümü `Shell_TrayWnd`'yi gizleyip görev çubuğunun **yerine geçiyor**.
macOS'ta bunun karşılığı **yok**. Yapabildiğimiz tek şey sistem Dock'unu
otomatik gizlemeye almak ve boşalan kenarı kullanmak. Fare ekranın kenarına
gidince Apple'ın Dock'u yine belirir — bu bizim eksiğimiz değil, macOS'un sınırı.

`NSApplication.presentationOptions = .hideDock` da işe yaramıyor: yalnız kendi
uygulamanız **ön plandayken** etkili, biz ise `.accessory` olarak hiç öne
gelmeyen bir uygulamayız.

## Ölçülmüş bulgu: `visibleFrame` otomatik gizlemede büyümüyor

Yaygın varsayım "Dock'u auto-hide yaparsan `NSScreen.visibleFrame` genişler"
şeklinde. Bu makinede ölçtük, **doğru çıkmadı**:

| autohide | `visibleFrame.minY` | altta rezerve |
|---|---|---|
| 0 | 65 | 65pt |
| 1 | 65 | 65pt |

İki ayrı süreçte, `killall Dock` sonrası 3 saniye beklenerek, fare ekranın
ortasındayken (y=595/1080) 10 saniye boyunca 2 saniyede bir örneklendi. Değer
hiç değişmedi.

**Sonuç:** Replace modunda `visibleFrame`'e güvenilmez. `ScreenPlacement.usableArea`
şöyle çalışır:

- **Replace:** yatayda ve altta `screen.frame` (gerçek kenar), üstte
  `visibleFrame.maxY` (menü çubuğu korunur)
- **ShowBoth:** `visibleFrame` (sistem Dock'unun üstüne oturmayız)

Ölçülen fark: ShowBoth'ta dock `y=71`, Replace'te `y=6`.

## Geri yükleme: dört yol

Kullanıcının Dock ayarını değiştiriyoruz, o yüzden geri yüklemek zorunludur.
Önceki değer `session.json` içine yazılır ve şu yolların her biriyle geri gelir:

| Durum | Mekanizma | Ölçüldü mü |
|---|---|---|
| Normal çıkış (menüden Çık) | `applicationWillTerminate` | ✅ autohide 1 → 0 |
| `pkill` / oturum kapanması (SIGTERM) | Sinyal yakalayıcı | ✅ autohide 1 → 0 |
| Uygulama hiç açılmıyor | `--restore-dock` | ✅ çökme taklit edilip denendi |
| Sonraki açılış, mod ShowBoth ise | Açılışta `restore()` | kodda var |

### Bulunan hata

İlk uygulamada yalnız `applicationWillTerminate` vardı. Ölçüm sırasında
`pkill` ile kapatıldığında **Dock gizli kaldı** — SIGTERM AppKit'in temiz
kapanma akışını tetiklemiyor, süreç doğrudan ölüyor.

Düzeltme: `DispatchSource.makeSignalSource` ile SIGTERM, SIGINT ve SIGHUP
yakalanıyor; sinyal gelince önce `SystemDock.restore()` çalışıyor, sonra çıkılıyor.
Ölçüldü: `pkill` sonrası autohide 1 → 0, `session.json` temizlendi.

### Yakalanamayan durumlar

- **SIGKILL** (`kill -9`): işletim sistemi hiçbir kod çalıştırmaz, yakalamak
  teknik olarak mümkün değil
- **Elektrik kesintisi / donanım kapanması**

Bu iki durumda Dock otomatik gizlemede kalır. Çözüm: `--restore-dock`
çalıştırmak veya Sistem Ayarları ▸ Masaüstü ve Dock'tan elle kapatmak.
`session.json` silinmediği için değer kaybolmaz.

`hideAndRemember` kayıtlı önceki değerin **üzerine yazmaz**. Sebep: arka arkaya
çalıştırmalarda ilk kosumda kaydedilen gerçek başlangıç değeri korunmalı, yoksa
ikinci kosum "önceki değer = gizli" diye yanlış kaydeder ve geri yükleme
anlamsızlaşır.
