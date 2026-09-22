import AppKit
import DockHubCore
import DockHubPlatform
import DockHubUI

/// Komut satiri oz-testleri: gercek uygulamayi calistirip olcer ve kapanir.
///   --verify          dock ve widget olcumlerini yazar
///   --activate <ad>   tiklamanin cagirdigi kod yolunu calistirir
///   --test-timers     kronometreyi gercek zamanla sinar
///   --test-persist    widget ayarinin diske yazildigini sinar
/// AppDelegate'ten ayri dosyada: gorev kanitlari bu ciktilara dayaniyor,
/// uygulama kablolamasi degistikce eskimesinler.
@MainActor
enum SelfTests {
    /// Bir oz-test istendiyse baslatir ve true doner (normal akis devam etmemeli).
    /// --verify normal akisin UZERINE calisir, bu yuzden false doner.
    static func start(service: ConfigService, panel: DockPanel) -> Bool {
        let args = CommandLine.arguments
        if let i = args.firstIndex(of: "--activate"), i + 1 < args.count {
            Task { await activate(args[i + 1], panel: panel) }
            return true
        }
        if args.contains("--test-timers") { Task { await timers() }; return true }
        if args.contains("--test-persist") { Task { await persist(service: service, panel: panel) }; return true }
        if args.contains("--verify") {
            Task {
                // CPU yuzdesi iki olcum arasindaki farktan cikar; bir tik bekleniyor.
                try? await Task.sleep(for: .seconds(4))
                printObservation(service: service, panel: panel)
                NSApp.terminate(nil)
            }
        }
        return false
    }

    /// Dock'a TIKLAMA ile ayni kod yolunu (model.activate) calistirir.
    /// Fare olayi disinda her sey ayni akis.
    private static func activate(_ hedef: String, panel: DockPanel) async {
        try? await Task.sleep(for: .milliseconds(400))
        func ad(_ i: DockItem) -> String { i.name ?? i.path.map(AppCatalog.displayName(forAppAt:)) ?? "" }
        guard let item = panel.model.items.first(where: {
            $0.kind == .app && ad($0).localizedCaseInsensitiveContains(hedef)
        }) else {
            print("BULUNAMADI: \(hedef)"); NSApp.terminate(nil); return
        }
        let onceCalisiyor = item.path.map { panel.model.runningPaths.contains($0) } ?? false
        print("--- Tiklama testi ---")
        print("  hedef            : \(ad(item))  (\(item.path ?? "-"))")
        print("  once calisiyor mu: \(onceCalisiyor)")
        panel.model.activate(item)               // tiklamanin cagirdigi ayni fonksiyon
        try? await Task.sleep(for: .seconds(3))
        panel.model.refreshRunning()
        let sonra = item.path.map { panel.model.runningPaths.contains($0) } ?? false
        let on = item.path.flatMap { AppCatalog.runningApplication(forAppAt: $0) }?.isActive ?? false
        print("  sonra calisiyor mu: \(sonra)")
        print("  one geldi mi      : \(on)")
        // Calisan uygulamada basari olcutu one gelmesi; yalniz calismasi yetmez.
        let sonuc = onceCalisiyor ? (on ? "ONE GELDI" : "CALISIYOR AMA ONE GELMEDI")
                                  : (sonra ? "UYGULAMA ACILDI" : "ACILMADI")
        print("  SONUC             : \(sonuc)")
        NSApp.terminate(nil)
    }

    /// TimerStore'u gercekten calistirip davranisini olcer.
    private static func timers() async {
        let store = TimerStore.shared
        let id = "test-kronometre"
        print("--- Kronometre testi ---")
        print("  baslangic elapsed : \(String(format: "%.2f", store.elapsed(id)))  calisiyor=\(store.isRunning(id))")
        store.toggle(id)
        try? await Task.sleep(for: .milliseconds(1500))
        let t1 = store.elapsed(id)
        print("  1.5 sn sonra      : \(String(format: "%.2f", t1))  calisiyor=\(store.isRunning(id))")
        store.toggle(id)                                   // duraklat
        let t2 = store.elapsed(id)
        try? await Task.sleep(for: .milliseconds(1000))
        let t3 = store.elapsed(id)
        print("  duraklatildi      : \(String(format: "%.2f", t2))  calisiyor=\(store.isRunning(id))")
        print("  1 sn bekledikten  : \(String(format: "%.2f", t3))  (donmus olmali)")
        store.toggle(id)                                   // devam
        try? await Task.sleep(for: .milliseconds(1000))
        let t4 = store.elapsed(id)
        print("  devam + 1 sn      : \(String(format: "%.2f", t4))  (t2 uzerine eklenmeli)")
        store.reset(id)
        print("  sifirlandi        : \(String(format: "%.2f", store.elapsed(id)))  calisiyor=\(store.isRunning(id))")
        let ilerledi = t1 > 1.3 && t1 < 1.8
        let dondu = abs(t3 - t2) < 0.05
        let devam = t4 > t2 + 0.9 && t4 < t2 + 1.3
        let sifir = store.elapsed(id) == 0 && !store.isRunning(id)
        print("  SONUC             : ilerledi=\(ilerledi) durakladi=\(dondu) devamEtti=\(devam) sifirlandi=\(sifir)")
        print("  GECTI             : \(ilerledi && dondu && devam && sifir)")
        NSApp.terminate(nil)
    }

    /// Widget ayarinin diske gercekten yazildigini olcer.
    private static func persist(service: ConfigService, panel: DockPanel) async {
        try? await Task.sleep(for: .milliseconds(300))
        guard let su = panel.model.items.first(where: { $0.widget == "hydration" }) else {
            print("hydration widget'i config'te yok"); NSApp.terminate(nil); return
        }
        func diskten() -> Double {
            guard let d = try? Data(contentsOf: service.url),
                  let o = try? JSONSerialization.jsonObject(with: d) as? [String: Any],
                  let items = o["items"] as? [[String: Any]],
                  let it = items.first(where: { $0["id"] as? String == su.id }),
                  let st = it["settings"] as? [String: Any] else { return -1 }
            return st["count"] as? Double ?? -1
        }
        let once = diskten()
        print("--- Kalicilik testi ---")
        print("  diskteki count (once) : \(once)")
        panel.model.setSetting(su.id, "count", .number(once + 3))
        let sonra = diskten()
        print("  setSetting cagrildi   : \(once) -> \(once + 3)")
        print("  diskteki count (sonra): \(sonra)")
        let bellek = panel.model.items.first(where: { $0.id == su.id })?
            .numberSetting("count", default: -1) ?? -1
        print("  bellekteki deger      : \(bellek)")
        print("  GECTI                 : \(sonra == once + 3 && bellek == once + 3)")
        NSApp.terminate(nil)
    }

    static func printObservation(service: ConfigService, panel: DockPanel) {
        let c = service.config
        print("--- Gozlem ---")
        print("  configPath        : \(service.url.path)")
        print("  edge / size       : \(c.edge.rawValue) / \(c.size.rawValue)")
        print("  layout / theme    : \(c.layout.rawValue) / \(c.theme.rawValue)")
        for (k, v) in panel.observation().sorted(by: { $0.key < $1.key }) {
            print("  \(k.padding(toLength: 18, withPad: " ", startingAt: 0)): \(v)")
        }
        print("  ogeler            :")
        for i in panel.model.items {
            let running = i.path.map { panel.model.runningPaths.contains($0) } ?? false
            print("      \(i.kind.rawValue.padding(toLength: 10, withPad: " ", startingAt: 0)) "
                  + "\((i.name ?? i.widget ?? i.path.map(AppCatalog.displayName(forAppAt:)) ?? "-").padding(toLength: 18, withPad: " ", startingAt: 0)) "
                  + "\(running ? "[calisiyor]" : "")")
        }
        // Widget kopyalari ve kendi ayarlari (ayni widget'in iki kopyasi ayri ayar tasir)
        let kompakt = JSONEncoder(); kompakt.outputFormatting = [.sortedKeys, .withoutEscapingSlashes]
        print("  widgetAyarlari    :")
        for i in panel.model.items where i.kind == .widget {
            let ayar = i.settings.flatMap { try? kompakt.encode($0) }.flatMap { String(data: $0, encoding: .utf8) } ?? "{}"
            print("      \("\(i.widget ?? "-")/\(i.effectiveVariant)".padding(toLength: 26, withPad: " ", startingAt: 0)) \(ayar)")
        }
        // Dunya saati: widget'in okudugu ayni ayar -> saat dilimi -> HH:mm
        for i in panel.model.items where i.widget == "world-clock" {
            guard case .array(let sehirler)? = i.setting("cities") else { continue }
            let satir = sehirler.compactMap { v -> String? in
                guard case .object(let o) = v, case .string(let ad)? = o["name"],
                      case .string(let tzid)? = o["timezone"], let tz = TimeZone(identifier: tzid) else { return nil }
                let f = DateFormatter(); f.timeZone = tz; f.dateFormat = "HH:mm"
                return "\(ad)=\(f.string(from: Date())) (\(tzid))"
            }
            print("  dunyaSaati        : \(satir.joined(separator: "  "))")
        }
        let ok = panel.panel.isVisible && panel.panel.occlusionState.contains(.visible)
        if let sc = NSScreen.main {
            print("  ekranVisibleFrame : y=\(Int(sc.visibleFrame.minY)) h=\(Int(sc.visibleFrame.height))"
                  + "  (tam ekran h=\(Int(sc.frame.height)))")
        }
        print("  bildirim          : \(Notifier.izinDurumu)  kullanilabilir=\(Notifier.kullanilabilir)")
        let cal = Calendar.current
        let simdi = Date()
        var oranlar: [String] = []
        for (ad, u) in [("gun", Calendar.Component.day), ("hafta", .weekOfYear),
                        ("ay", .month), ("yil", .year)] {
            if let a = cal.dateInterval(of: u, for: simdi) {
                let p = simdi.timeIntervalSince(a.start) / a.duration
                oranlar.append("\(ad)=%\(String(format: "%.2f", p * 100))")
            }
        }
        print("  zamanIlerlemesi   : \(oranlar.joined(separator: "  "))")
        let cop = TrashStore.shared.state
        print("  copKutusu         : " + (cop.accessible ? "\(cop.itemCount) oge  \(cop.totalBytes) bayt" : "OKUNAMADI (Tam Disk Erisimi gerekiyor)"))
        switch NowPlayingStore.shared.sonuc {
        case .calan(let n): print("  calanMedya        : \(n.app) - \(n.title) / \(n.artist)")
        case .hicbiriCalmiyor: print("  calanMedya        : hicbiri calmiyor")
        case .izinYok(let m): print("  calanMedya        : izin yok (\(m))")
        }
        switch AIUsageStore.shared.sonuc {
        case .veri(let u): print("  aiKullanimi       : oturum %\(u.sessionPercent) (\(u.sessionResets))  hafta %\(u.weekPercent) (\(u.weekResets))")
        case .cliYok: print("  aiKullanimi       : claude CLI bulunamadi")
        case .okunamadi(let m): print("  aiKullanimi       : okunamadi (\(m))")
        }
        let ag = NetworkStore.shared.sample
        print("  ag                : indirme \(NetworkMetrics.format(ag.downBytesPerSec))  yukleme \(NetworkMetrics.format(ag.upBytesPerSec))  toplamIn=\(ag.totalDown)")
        if let dk = DiskInfo.usage(), dk.total > 0 {
            print("  disk              : \(dk.used / 1_073_741_824) GB / \(dk.total / 1_073_741_824) GB = %\(String(format: "%.1f", Double(dk.used) / Double(dk.total) * 100))")
        }
        let au = AudioStore.shared
        print("  ses               : aygit=\(au.aygitlar.first(where: { $0.isDefault })?.name ?? "-")  seviye=%\(Int((au.seviye * 100).rounded()))  sessiz=\(au.sessiz)  aygitSayisi=\(au.aygitlar.count)")
        print("  cevreBirimPili    : \(PeripheralStore.shared.cihazlar.isEmpty ? "cihaz yok" : PeripheralStore.shared.cihazlar.map { "\($0.name) %\($0.percent)" }.joined(separator: ", "))")
        let b = BatteryMonitor.shared.state
        print("  pil               : " + (b.hasBattery
              ? "%\(b.percent)  sarj=\(b.isCharging)  fis=\(b.isPluggedIn)  kalan=\(b.minutesRemaining.map{String($0)+" dk"} ?? "bilinmiyor")"
              : "pil yok"))
        if let w = WeatherStore.shared.reading {
            print("  hava              : \(String(format: "%.1f", w.temperatureC))C  kod=\(w.weatherCode)  \(w.description)  yer=\(w.place ?? "-")")
        } else {
            print("  hava              : okunmadi -> \(WeatherStore.shared.hata ?? "yukleniyor")")
        }
        let s = SystemMonitor.shared.sample
        print("  sistemOlcumu      : CPU %\(String(format: "%.1f", s.cpuPercent))"
              + "  RAM %\(String(format: "%.1f", s.memoryPercent))"
              + "  (\(s.memoryUsedBytes / 1_048_576) MB / \(s.memoryTotalBytes / 1_048_576) MB)")
        print("  SONUC             : \(ok ? "DOCK EKRANDA" : "DOCK GORUNMUYOR")")
    }
}
