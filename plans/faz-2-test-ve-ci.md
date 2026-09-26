# Faz 2: Test ve CI Altyapısı

> Öncelik: 🟠 Yüksek · Boyut: M · Bağımlılık: Faz 0 ve 1 (test edilecek saf mantık orada oluşuyor)

## Amaç

Projede hiç otomatik test ve CI yok. Sonraki fazlar (özellikle Faz 4'teki Undo ve config işlemleri, Faz 9'daki refactor'lar) regresyon riski taşıyor. Bu faz:
- Saf mantık için bir unit test projesi,
- Her push'ta build + test, her tag'de installer üreten bir GitHub Actions iş akışı kurar.

## Kapsam dışı

- UI otomasyon testleri (FlaUI vb.). Maliyet/fayda oranı şu an düşük; Faz 6'daki erişilebilirlik işinden sonra yeniden değerlendirilebilir.

---

## Görev 2.1: Test projesi

1. `tests/CustomDock.Tests/CustomDock.Tests.csproj`:
   - `TargetFramework`: `net8.0-windows10.0.19041.0`, `UseWPF=true` (ConfigService `DispatcherTimer` kullanıyor).
   - Paketler: `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`, `FluentAssertions` (opsiyonel).
   - `<ProjectReference Include="..\..\src\CustomDock\CustomDock.csproj" />`
2. Ana projeye `<InternalsVisibleTo Include="CustomDock.Tests" />`.
3. `CustomDock.sln`'e test projesi eklenir.
4. **Ortam izolasyonu:** `AppPaths` statik alanları `DOCKHUB_HOME` ortam değişkenini tip ilk kullanıldığında okuyor. Test derlemesinde bir `[ModuleInitializer]` her test çalıştırması için `%TEMP%\dockhub-tests-<guid>` klasörünü `DOCKHUB_HOME` olarak ayarlar; test bitiminde silinir.
5. **Dispatcher gerektiren testler:** `StaFact` desteği için `Xunit.StaFact` paketi veya küçük bir `StaTestHelper.Run(Action)` yardımcısı.

## Görev 2.2: Yazılacak testler

| Alan | Test konusu | Kaynak |
|---|---|---|
| `AppPathResolver` | Squirrel/MSIX/normal yol için `StablePinPath`, `NormalizeExeKey`; büyük/küçük harf; `-beta` gibi sürüm ekleri | Faz 0.3 |
| `AppKeys` | `.exe`, `.lnk` (hedefli / AUMID'li / web app `_crx_`), `shell:AppsFolder`, pencere anahtarlarıyla eşleşme | [Shell/AppKeys.cs](../src/CustomDock/Shell/AppKeys.cs) |
| `ConfigService` | v1 → v2 migration (örnek v1 JSON fixture'ları), enum eşlemeleri (`HideTaskbar` → `Replace`, `Mica` → `Blur`), bozuk JSON → `.corrupt-*` yedeği + varsayılanlar, bilinmeyen widget'ın korunması (Faz 1.5) | [Core/ConfigService.cs](../src/CustomDock/Core/ConfigService.cs) |
| `ConfigService` öğe işlemleri | `AddItem`, `RemoveItem` (üst seviye / klasör içi / son çocuk silinince klasörün silinmesi), `MoveItem` (ileri/geri indeks, klasörden dışarı), `CreateGroupFromItems`, `UngroupAll`, `AddToGroup` | aynı |
| `ConfigService` widget ayarları | `GetItemSettings` aynı örneği döndürür, değişiklik `DockItem.Settings`'e yazılır, bozuk ayar JSON'ı varsayılana düşer | aynı |
| `AIUsageService.Parse` | Gerçek CLI çıktı örnekleri (fixture), yalnızca session, yalnızca week, hiçbiri, "login required" çıktısı → `NotLoggedIn` | Faz 1.7 |
| `JsonStore` | Atomik yazma (geçici dosya + move), bozuk dosya okuma | [Core/JsonStore.cs](../src/CustomDock/Core/JsonStore.cs) |
| `TaskbarEligibility` | Stil kombinasyonları → uygunluk (Faz 0.5 Yol B uygulandıysa) | Faz 0.5 |
| `ItemDataStore` | Silinen öğe dosyasının trash'e taşınması, 7 gün temizliği | Faz 1.4 |

Fixture'lar `tests/CustomDock.Tests/Fixtures/` altında, `CopyToOutputDirectory=PreserveNewest`.

**Kabul:** `dotnet test` yerelde yeşil; kapsam hedefi bu sınıflar için satır bazında %70+.

## Görev 2.3: GitHub Actions

`.github/workflows/ci.yml`:

```yaml
name: CI
on:
  push: { branches: [master] }
  pull_request:
jobs:
  build:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '8.0.x' }
      - run: dotnet restore CustomDock.sln
      - run: dotnet build CustomDock.sln -c Release --no-restore -warnaserror
      - run: dotnet test CustomDock.sln -c Release --no-build --logger trx --collect:"XPlat Code Coverage"
      - uses: actions/upload-artifact@v4
        if: always()
        with: { name: test-results, path: '**/TestResults/**' }
```

`.github/workflows/release.yml` (tag `v*`):
1. Checkout, setup-dotnet.
2. Tag sürümü ile `CustomDock.csproj` içindeki `<Version>` eşleşiyor mu kontrol et; eşleşmiyorsa başarısız ol.
3. `choco install innosetup -y`.
4. `pwsh installer/build.ps1`.
5. `softprops/action-gh-release` ile `installer/Output/DockHub-Setup-*-x64.exe` taslak (draft) release'e yüklenir. Release notları elle yazılır.

**Not:** `-warnaserror` ilk seferde mevcut uyarılar yüzünden başarısız olursa önce uyarılar temizlenir; mümkün değilse geçici olarak kaldırılır ve "Uygulama notları"na yazılır.

## Görev 2.4: Küçük altyapı dosyaları

1. `Directory.Build.props` (kök): ortak `LangVersion`, `Nullable`, `TreatWarningsAsErrors` (yalnızca CI'da, `$(CI)` koşuluyla).
2. `.editorconfig`: mevcut kod stilini (4 boşluk, `var` kullanımı, dosya kapsamlı namespace) kodlar. Yalnızca mevcut stili tarif eder; toplu yeniden biçimlendirme yapılmaz.
3. README'ye CI rozeti ve "Running tests" bölümü.

## Test planı

1. `dotnet test` yerelde yeşil.
2. Bir dalda (branch) PR açılır, CI koşar ve yeşil olur.
3. `v0.0.0-test` gibi bir test tag'i ile release iş akışı denenir, taslak release oluşur, sonra silinir.

## Commit önerisi

- `test: add unit test project with config, app key and path resolver tests`
- `ci: build and test on push, publish installer on tags`

## Uygulama notları

_(Faz uygulanırken doldurulacak.)_
