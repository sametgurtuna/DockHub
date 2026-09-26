# DockHub Geliştirme Planı

Bu klasör DockHub'daki tüm hataların ve iyileştirmelerin fazlara bölünmüş uygulama planıdır.
Her faz tek başına yapılabilir, test edilebilir ve commit'lenebilir bir iş paketidir.

## Nasıl kullanılır

- "Faz N'i yap" denildiğinde ilgili `faz-N-*.md` dosyası baştan sona uygulanır.
- Her faz dosyasında: **Amaç**, **Görevler** (dosya/satır referanslı), **Kabul kriterleri**, **Test adımları** ve **Commit önerisi** bulunur.
- Bir faz bitince aşağıdaki durum tablosu güncellenir (`⬜ → ✅`) ve faz dosyasının sonundaki "Uygulama notları" bölümüne yapılanlar ve sapmalar yazılır.
- Fazlar sırayla yapılmak üzere tasarlandı. Bağımlılıklar her dosyanın başında yazılı.
- Satır numaraları planın yazıldığı commit'e (`5ee84f7`) göredir; uygulama sırasında kaymış olabilir. Önce ilgili sembolü arayın.

## Durum

| Faz | Başlık | Öncelik | Boyut | Durum |
|---|---|---|---|---|
| 0 | [Bildirilen hatalar: kaybolan ikonlar, preview orta tık, Discord](faz-0-bildirilen-hatalar.md) | 🔴 Kritik | M | ✅ |
| 1 | [Doğruluk hataları ve veri güvenliği](faz-1-dogruluk-ve-veri-guvenligi.md) | 🔴 Kritik | M | ✅ |
| 2 | [Test ve CI altyapısı](faz-2-test-ve-ci.md) | 🟠 Yüksek | M | ✅ |
| 3 | [Görev çubuğu eşitliği: Win+1..9, kısayollar, ses/pil ikonları](faz-3-gorev-cubugu-esitligi.md) | 🟠 Yüksek | L | ✅ |
| 4 | [Güvenlik ağı: geri alma, dışa/içe aktarma, preset, güncelleme](faz-4-guvenlik-agi.md) | 🟠 Yüksek | L | ⬜ |
| 5 | [UX cilası: onboarding, ayarlarda arama, canlı önizleme, dock düzeni](faz-5-ux-cilasi.md) | 🟡 Orta | L | ⬜ |
| 6 | [Erişilebilirlik ve Türkçe arayüz](faz-6-erisilebilirlik-ve-yerellestirme.md) | 🟡 Orta | L | ⬜ |
| 7 | [Dock davranışı: profiller, intellihide, klasör grid](faz-7-dock-davranisi.md) | 🟡 Orta | L | ⬜ |
| 8 | [Yeni widget'lar](faz-8-yeni-widgetlar.md) | 🟢 Düşük | XL | ⬜ |
| 9 | [Performans ve kod sağlığı](faz-9-performans-ve-kod-sagligi.md) | 🟢 Düşük | M | ⬜ |
| 10 | [Üçüncü taraf widget SDK'sı](faz-10-widget-sdk.md) | 🟢 Düşük | XL | ⬜ |

Boyut: S = birkaç saat, M = 1-2 gün, L = 3-5 gün, XL = 1 hafta ve üzeri.

## Referans

- [00-bildirilen-hatalar-analizi.md](00-bildirilen-hatalar-analizi.md): Kullanıcının bildirdiği üç sorunun kök neden analizi (Faz 0'ın dayanağı).

## Genel kurallar (her faz için geçerli)

1. **Görev çubuğu her koşulda geri gelmeli.** `TaskbarController` ve `App.SafeRestoreTaskbar` yollarına dokunan her değişiklikten sonra `--restore-taskbar`, normal çıkış ve Görev Yöneticisi'nden sonlandırma senaryoları elle denenir.
2. **Config geriye dönük uyumlu kalmalı.** `config.json`'a yeni alan eklenirse varsayılan değer eski davranışı korumalı. Şema kırılırsa `AppConfig.CurrentVersion` artırılır ve `ConfigService` içine migration eklenir.
3. **Metinlerde uzun tire (em dash) kullanılmaz** (PRODUCT.md marka kuralı).
4. **Hafiflik:** yeni zamanlayıcılar hizalı olmalı ve olay tabanlı yaklaşım tercih edilmeli. 250 ms altı polling yeni koda eklenmez.
5. Her faz sonunda `dotnet build CustomDock.sln -c Release` uyarısız geçmeli. Faz 2'den sonra `dotnet test` da geçmeli.
6. README'de davranışı değişen her özellik aynı commit'te güncellenir.
