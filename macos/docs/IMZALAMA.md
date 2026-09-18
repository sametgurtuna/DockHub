# DockHub macOS — Kod İmzalama

Üç seviye var ve şu an **birincisindeyiz.**

## 1. Ad-hoc imza — şu anki durum, ücretsiz

`Scripts/bundle.sh` her paketlemede şunu çalıştırıyor:

```sh
codesign --force --sign - --identifier com.dockhub.mac .build/DockHub.app
```

`--sign -` ad-hoke imzadır: Apple ID, hesap veya sertifika gerektirmez.
Uygulamanın kendi makinenizde çalışması için yeterli — dock şu an böyle çalışıyor.

**Sınırı:** ad-hoc imzanın kimlik bilgisi (`Team ID`) yoktur. macOS, verilen
izinleri (TCC: Erişilebilirlik, Otomasyon, Konum) uygulamanın imzasına bağlar.
Ad-hoc imzada bu bağ zayıftır ve **yeniden derlemede izinler sıfırlanabilir.**
Henüz izin gerektiren bir özellik eklemediğimiz için bu sorun ortaya çıkmadı;
`wf-running-apps` (Erişilebilirlik) geldiğinde ortaya çıkacak.

## 2. Ücretsiz Apple ID ile geliştirme imzası — önerilen sonraki adım

Bu adımı **sizin yapmanız gerekiyor**, ben yapamam: Xcode'a Apple ID ile giriş
hesap kimlik bilgilerinizi gerektiriyor ve başkasının hesabına giriş yapmam doğru
olmaz. Adımlar:

1. Xcode'u açın → **Xcode ▸ Settings ▸ Accounts**
2. Sol altta **+** → **Apple ID** → kendi Apple ID'nizle giriş yapın (ücretsiz)
3. Hesap seçili haldeyken **Manage Certificates…** → **+** → **Apple Development**
4. Sertifika adını öğrenin:

```bash
security find-identity -v -p codesigning
```

Çıktıda `Apple Development: ad@ornek.com (XXXXXXXXXX)` göreceksiniz. Sonra bana
söyleyin, `Scripts/bundle.sh` içindeki `--sign -` yerine o kimliği yazayım.

**Kazancı:** imza sabitlenir, TCC izinleri her derlemede sıfırlanmaz.
**Sağlamadığı:** dağıtım. Başka makinede hâlâ Gatekeeper engeller.

## 3. Developer ID + notarization — dağıtım için, ücretli

Apple Developer Program üyeliği gerekir (uzun süredir yılda 99 USD; güncel
fiyatı Apple'ın sitesinden doğrulayın). Akış:

```sh
codesign --force --options runtime --timestamp \
         --sign "Developer ID Application: Ad Soyad (TEAMID)" DockHub.app
xcrun notarytool submit DockHub.dmg --apple-id ... --team-id ... --wait
xcrun stapler staple DockHub.dmg
```

Bu üç adım tamamlanmadan `.dmg`'yi indiren kişi "geliştirici doğrulanamadı"
uyarısı alır ve Sistem Ayarları'ndan elle izin vermek zorunda kalır.

### Neyi açar

| Etkilenen | Neden gerekli |
|---|---|
| `wf-installer` | Notarization olmadan dağıtım pratik değil |
| `wf-startup` | `SMAppService.mainApp.status` şu an `3` (`notFound`) — imzasız ve `/Applications` dışındaki paket giriş öğesi olarak kaydolmuyor |
| `wf-notifications` | Bildirim **teslimi** henüz ölçülmedi; imzasız pakette çalışmama ihtimali var |
| Erişilebilirlik/Otomasyon | İzinlerin derlemeler arası kalıcı olması |

### Karar durumu

Projenin açık sorusu olarak duruyor. `d-macos-hedefi-v2` uyarınca uygulama önce
tek makine için geliştirildiğinden **şimdilik gerekmiyor.** Uygulamayı başkasına
(arkadaşınıza dahil) vermek istediğinizde zorunlu hale gelir.
