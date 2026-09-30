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

    /// Ayarlar penceresi oz-testleri:
    ///   --test-settings              ayarlar penceresinin yazma yollarini (SettingsStore)
    ///                                calistirip dock'a canli yansimayi olcer
    ///   --snapshot-settings <klasor> her ayar sayfasini PNG olarak yazar
    static func startSettingsTests(store: SettingsStore,
                                   dock: @escaping @MainActor () -> DockPanel?,
                                   settings: @escaping @MainActor () -> SettingsWindowController?) {
        let args = CommandLine.arguments
        if args.contains("--test-settings") {
            Task { await settingsTest(store: store, dock: dock, settings: settings) }
        } else if let i = args.firstIndex(of: "--snapshot-settings"), i + 1 < args.count {
            Task { await snapshots(dir: args[i + 1], settings: settings) }
        }
    }

    private static func snapshots(dir: String, settings: () -> SettingsWindowController?) async {
        guard let c = settings() else { print("ayarlar penceresi kurulamadi"); NSApp.terminate(nil); return }
        try? FileManager.default.createDirectory(atPath: dir, withIntermediateDirectories: true)
        for page in SettingsPage.allCases {
            let url = URL(fileURLWithPath: dir).appendingPathComponent("settings-\(page.rawValue).png")
            let ok = await c.snapshot(page, to: url)
            print("  \(page.rawValue.padding(toLength: 11, withPad: " ", startingAt: 0)): \(ok ? url.path : "YAZILAMADI")")
        }
        NSApp.terminate(nil)
    }

    /// Ayarlar penceresinin kontrolleri SettingsStore.binding/update ve oge
    /// islemlerine baglidir; bu test ayni yollari fare olmadan calistirir.
    private static func settingsTest(store: SettingsStore, dock: () -> DockPanel?,
                                     settings: () -> SettingsWindowController?) async {
        var gecen = 0, toplam = 0
        func kontrol(_ ad: String, _ ok: Bool, _ detay: String) {
            toplam += 1; if ok { gecen += 1 }
            print("  \(ok ? "GECTI" : "KALDI")  \(ad.padding(toLength: 30, withPad: " ", startingAt: 0)) \(detay)")
        }
        func bekle() async { try? await Task.sleep(for: .milliseconds(500)) }
        func cerceve(_ d: DockPanel?) -> CGRect { d?.panel.frame ?? .zero }
        func f(_ r: CGRect) -> String { "x=\(Int(r.minX)) y=\(Int(r.minY)) w=\(Int(r.width)) h=\(Int(r.height))" }
        func diskte() -> [String: Any] {
            guard let d = try? Data(contentsOf: store.service.url),
                  let o = try? JSONSerialization.jsonObject(with: d) as? [String: Any] else { return [:] }
            return o
        }

        try? await Task.sleep(for: .milliseconds(600))
        let ilk = store.config
        print("--- Ayarlar testi ---")
        print("  baslangic: \(f(cerceve(dock())))  ogeler=\(dock()?.model.items.count ?? -1)")

        // Dock'un sag tik menusu: "Settings…" ogesi pencereyi acmali.
        func menuBul(_ v: NSView?) -> NSMenu? {
            guard let v else { return nil }
            if let m = v.menu { return m }
            for alt in v.subviews { if let m = menuBul(alt) { return m } }
            return nil
        }
        if let menu = menuBul(dock()?.panel.contentView),
           let i = menu.items.firstIndex(where: { $0.title == "Settings…" }) {
            menu.performActionForItem(at: i)
            await bekle()
            let w = settings()
            kontrol("menu 'Settings…' pencereyi acar", w?.window?.isVisible == true && w?.page == .general,
                    "menu: \(menu.items.map(\.title).filter { !$0.isEmpty }.joined(separator: " | "))")
            if let g = menu.items.firstIndex(where: { $0.title == "Add Widget…" }) {
                menu.performActionForItem(at: g); await bekle()
                kontrol("menu 'Add Widget…' galeriyi acar", settings()?.page == .gallery, "sayfa=\(settings()?.page.rawValue ?? "-")")
            }
            settings()?.window?.orderOut(nil)
        } else {
            kontrol("menu 'Settings…' pencereyi acar", false, "menu bulunamadi")
        }

        // Ana ekran secimi: dock secilen ekranin icinde kurulmali.
        if let son = NSScreen.screens.last {
            store.update { $0.monitorDevice = son.localizedName }; await bekle()
            let r0 = cerceve(dock())
            kontrol("ana ekran -> o ekranda", son.frame.contains(r0), "\(son.localizedName) (\(NSScreen.screens.count) ekran) \(f(r0))")
            store.update { $0.monitorDevice = ilk.monitorDevice }; await bekle()
        }

        // General / Appearance: yerlesim
        var once = dock()
        store.update { $0.edge = .left }; await bekle()
        var r = cerceve(dock())
        kontrol("kenar Left -> dikey dock", dock() !== once && r.width == 50 && r.height > r.width, f(r))

        once = dock()
        store.update { $0.size = .large }; await bekle()
        r = cerceve(dock())
        kontrol("boyut Large -> 64", dock() !== once && r.width == 64, f(r))

        store.update { $0.edge = .bottom }; await bekle()
        let tam = cerceve(dock())
        store.update { $0.widthMode = .fit }; await bekle()
        r = cerceve(dock())
        kontrol("Fit content -> icerik kadar", r.width < tam.width && r.width > 64, "tam w=\(Int(tam.width)) fit w=\(Int(r.width))")

        store.update { $0.alignment = .start }; await bekle()
        let bas = cerceve(dock())
        kontrol("hizalama Start -> sola yasli", bas.minX < r.minX, "center x=\(Int(r.minX)) start x=\(Int(bas.minX))")

        store.update { $0.layout = .attached }; await bekle()
        let yapisik = cerceve(dock())
        kontrol("sekil Attached -> bosluksuz", yapisik.minX < bas.minX, "floating x=\(Int(bas.minX)) attached x=\(Int(yapisik.minX))")

        store.update { $0.theme = .light; $0.backdrop = .solid; $0.tintOpacity = 0.2; $0.hoverEffect = false }
        await bekle()
        let gorunum = dock()?.panel.appearance?.name
        kontrol("tema Light -> panel aqua", gorunum == .aqua, gorunum?.rawValue ?? "nil")

        let o = diskte()
        let diskOk = o["edge"] as? String == "Bottom" && o["size"] as? String == "Large"
            && o["widthMode"] as? String == "Fit" && o["alignment"] as? String == "Start"
            && o["layout"] as? String == "Attached" && o["theme"] as? String == "Light"
            && o["backdrop"] as? String == "Solid" && o["tintOpacity"] as? Double == 0.2
            && o["hoverEffect"] as? Bool == false
        kontrol("config.json Windows adlariyla", diskOk,
                "edge=\(o["edge"] ?? "-") size=\(o["size"] ?? "-") widthMode=\(o["widthMode"] ?? "-") alignment=\(o["alignment"] ?? "-") layout=\(o["layout"] ?? "-") theme=\(o["theme"] ?? "-") backdrop=\(o["backdrop"] ?? "-") tintOpacity=\(o["tintOpacity"] ?? "-") hoverEffect=\(o["hoverEffect"] ?? "-")")

        // Dock items
        let n0 = dock()?.model.items.count ?? -1
        store.addSeparator()
        let saat = store.addWidget("clock", variant: "digital")
        let su = store.addWidget("hydration", variant: "timer")
        store.addApps(["/System/Applications/Calculator.app"])
        await bekle()
        let hesapId = store.config.items.first { $0.path == "/System/Applications/Calculator.app" }?.id ?? ""
        var m = dock()?.model.items ?? []
        kontrol("ekle: ayirici+2 widget+uygulama", m.count == n0 + 4 && m.last?.path == "/System/Applications/Calculator.app"
                && m.contains { $0.id == saat && $0.variant == "digital" }, "oge \(n0) -> \(m.count)")
        kontrol("ayni uygulama iki kez eklenmez", store.addApps(["/System/Applications/Calculator.app"]) == 0, "addApps tekrar = 0")

        store.setName(hesapId, "Hesap"); store.setArguments(hesapId, "--foo \"a b\"")
        store.setVariant(saat, "analog")
        await bekle()
        m = dock()?.model.items ?? []
        let hesap = m.first { $0.id == hesapId }
        kontrol("ad, argüman, varyant dock'ta", hesap?.name == "Hesap" && hesap?.arguments == "--foo \"a b\""
                && m.first { $0.id == saat }?.variant == "analog",
                "ad=\(hesap?.name ?? "-") arg=\(hesap?.arguments ?? "-") saat=\(m.first { $0.id == saat }?.variant ?? "-")")

        if let i = store.config.items.firstIndex(where: { $0.id == hesapId }) {
            store.moveItems(from: IndexSet(integer: i), to: 0)
        }
        await bekle()
        kontrol("siralama: en basa tasindi", dock()?.model.items.first?.id == hesapId, "ilk=\(dock()?.model.items.first?.name ?? "-")")

        // Widget'in kendi ayari dock'u yeniden kurmamali; yerlesim degisikligi kurmali.
        let simdiki = dock()
        simdiki?.model.setSetting(su, "count", .number(7))
        await bekle()
        kontrol("widget ayari -> yeniden kurulmaz", dock() === simdiki
                && store.service.config.items.first { $0.id == su }?.numberSetting("count", default: -1) == 7,
                "ayni panel=\(dock() === simdiki)")
        store.setVariant(su, "progress"); await bekle()
        kontrol("varyant -> yeniden kurulur", dock() !== simdiki, "yeni panel=\(dock() !== simdiki)")

        for id in [saat, su, hesapId] { store.removeItem(id) }
        if let sep = store.config.items.last(where: { $0.kind == .separator })?.id { store.removeItem(sep) }
        await bekle()
        kontrol("sil: oge sayisi geri dondu", dock()?.model.items.count == n0, "oge \(dock()?.model.items.count ?? -1)")

        // Replace / Show both: sistem Dock'unu gizler ve geri yukler.
        let dockOnce = SystemDock.isAutoHideEnabled()
        store.update { $0.taskbarMode = .replace }; await bekle()
        let gizli = SystemDock.isAutoHideEnabled()
        store.update { $0.taskbarMode = .showBoth }; await bekle()
        let geri = SystemDock.isAutoHideEnabled()
        kontrol("Replace/Show both", gizli == true && geri == dockOnce,
                "once=\(dockOnce.map(String.init) ?? "-") replace=\(gizli.map(String.init) ?? "-") showBoth=\(geri.map(String.init) ?? "-")")

        store.update { $0 = ilk }; await bekle()
        print("  bitis: \(f(cerceve(dock())))  config ilk haline dondu")
        print("  SONUC: \(gecen)/\(toplam) GECTI")
        NSApp.terminate(nil)
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
