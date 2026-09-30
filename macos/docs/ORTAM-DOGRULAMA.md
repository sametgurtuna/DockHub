# DockHub macOS — Ortam Doğrulaması

`T0-ORTAM` görevinin ölçüm kaydı. Tekrar üretmek için:

```sh
DockHub/macos/Scripts/verify-toolchain.sh
```

Betik geçici bir SPM paketi kurar, SwiftUI görünümünü `NSHostingView` ile bir
`NSPanel` içine yerleştirir, `.app` paketine çevirir, çalıştırır ve pencerenin
ekranda olup olmadığını bildirir. Hiçbir kalıcı dosya bırakmaz.

## Ölçülen araç zinciri

| | |
|---|---|
| macOS | 27.0 (build 26A428) |
| Xcode | 27.0 |
| `xcode-select -p` | `/Applications/Xcode.app/Contents/Developer` |
| Swift | 6.4 (swiftlang-6.4.0.34.1), target `arm64-apple-macosx27.0.0` |
| macOS SDK | 27.0 |

Karar `d-macos-hedefi-v2` bu ölçüme dayanır: minimum hedef **macOS 27.0**.

## Ölçülen derleme ve çalıştırma

```
=== swift build ===
Build complete! (9,72 sec)

=== .app paketi ===
  olusturuldu: Probe.app

--- Gozlem ---
  bundleIdentifier : com.dockhub.probe
  SwiftUI+NSHostingView : kuruldu
  panel.isVisible : true
  occlusionState .visible : true
  frame : x=780 y=89 w=360 h=56
  screen : VX3218-PC-MHD scale 1.0
  SONUC : PENCERE EKRANDA
```

**Gözlemin niteliği:** bu programatik bir gözlemdir, insan gözüyle bakılmamıştır.
`NSWindow.isVisible` pencerenin sıraya alındığını, `occlusionState.contains(.visible)`
ise sistemin pencereyi gerçekten görünür saydığını (küçültülmemiş, tamamen
örtülmemiş) söyler. İkisinin birlikte doğru olması pencerenin ekranda olduğunun
güçlü göstergesidir. Görsel doğrulama için betik elle çalıştırılabilir; panel
1,5 saniye ekranda kalır.

## Doğrulanan zincir

Sonda, DockHub'ın gerçekten kullanacağı yığını uçtan uca çalıştırdı:

`SwiftUI.View` → `NSHostingView` → `NSPanel` (`.nonactivatingPanel`,
`level = .statusBar`, `collectionBehavior = [.canJoinAllSpaces, .stationary]`)
→ `NSVisualEffectView` (`material = .hudWindow`, `blendingMode = .behindWindow`)
→ ekranın alt kenarına `setFrameOrigin` ile yerleştirme.

`NSApplication.setActivationPolicy(.accessory)` ile uygulama Dock'ta ikon
göstermedi — `LSUIElement` davranışının kod tarafındaki karşılığı.

## Bilinen zararsız uyarı

Çalıştırmada şu satır çıkıyor:

```
sandbox_extension_issue_file_to_process failed for /var/folders/.../Probe.app: 1 (Operation not permitted)
```

Sebebi, imzasız bir `.app` paketinin geçici klasörden (`/var/folders/...`)
çalıştırılması. Hiçbir şeyi engellemiyor; pencere yine açıldı. Uygulama kalıcı
bir konumdan ve imzalı çalıştığında bu uyarı kalkar.

## Xcode neden hâlâ gerekli

`swift build` derlemek için yeterli ([MIMARI.md](MIMARI.md) bölüm 1). Xcode şunlar
için gerekiyor:

- `codesign` ile Developer ID imzalama ve `notarytool` ile notarization (`ag-packaging`)
- `SMAppService.loginItem` kaydının gerçekten işlemesi — imzasız ve `/Applications`
  dışındaki paket `status = 3` (`notFound`) veriyor (`ag-autostart`, `wf-startup`)
- Instruments ile performans ölçümü ve SwiftUI önizleme
