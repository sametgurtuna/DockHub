import AppKit
import SwiftUI
import Combine
import DockHubCore
import DockHubPlatform

/// Kucuk egilim cizgisi (Windows: Sparkline).
struct Sparkline: View {
    let values: [Double]
    var color: Color = .accentColor

    var body: some View {
        GeometryReader { geo in
            if values.count > 1, let lo = values.min(), let hi = values.max() {
                let span = max(hi - lo, 0.000_001)
                Path { p in
                    for (i, v) in values.enumerated() {
                        let x = geo.size.width * CGFloat(i) / CGFloat(values.count - 1)
                        let y = geo.size.height * (1 - CGFloat((v - lo) / span))
                        if i == 0 { p.move(to: CGPoint(x: x, y: y)) } else { p.addLine(to: CGPoint(x: x, y: y)) }
                    }
                }
                .stroke(color, style: StrokeStyle(lineWidth: 1.4, lineCap: .round, lineJoin: .round))
            }
        }
    }
}

/// "+0.42%" yesil, "-1.10%" kirmizi.
struct ChangeText: View {
    let percent: Double?
    var size: CGFloat = 11

    var body: some View {
        if let percent {
            Text(String(format: "%@%.2f%%", percent >= 0 ? "+" : "", percent))
                .font(.system(size: size, weight: .medium)).monospacedDigit()
                .foregroundStyle(percent >= 0 ? .green : .red)
        }
    }
}

enum NumberText {
    static func price(_ value: Double) -> String {
        let f = NumberFormatter()
        f.numberStyle = .decimal
        f.minimumFractionDigits = value >= 1000 ? 0 : (value >= 1 ? 2 : 4)
        f.maximumFractionDigits = value >= 1000 ? 0 : (value >= 1 ? 2 : 6)
        return f.string(from: NSNumber(value: value)) ?? String(value)
    }
}

// MARK: - Doviz ve kripto

@MainActor
final class QuoteLoader<Quote: Sendable>: ObservableObject {
    @Published var quotes: [Quote] = []
    @Published var error: String?
    private var timer: Timer?
    private var key = ""
    private var fetch: (@Sendable () async throws -> [Quote])?

    /// `key` degisince yeniden yukler; her `interval` saniyede bir tazeler.
    func start(key: String, interval: TimeInterval, fetch: @escaping @Sendable () async throws -> [Quote]) {
        let changed = key != self.key
        self.key = key
        self.fetch = fetch
        if timer == nil {
            timer = Timer.scheduledTimer(withTimeInterval: interval, repeats: true) { [weak self] _ in
                MainActor.assumeIsolated { self?.load() }
            }
        }
        if changed { load() }
    }

    func load() {
        guard let fetch else { return }
        Task { @MainActor in
            do {
                quotes = try await fetch()
                error = nil
            } catch {
                self.error = L.t("No connection")
            }
        }
    }
}

struct CurrencyWidget: View {
    let item: DockItem
    let style: DockStyle
    @ObservedObject var model: DockModel
    @StateObject private var loader = QuoteLoader<CurrencyQuote>()
    @State private var open = false

    private var base: String { item.stringSetting("base", default: "USD").trimmingCharacters(in: .whitespaces).uppercased() }
    private var targets: [String] { CurrencyData.targets(item.stringSetting("targets", default: "TRY, EUR"), base: base) }

    var body: some View {
        HStack(spacing: 6) {
            Image(systemName: "dollarsign.arrow.circlepath").font(.system(size: style.iconSize * 0.45)).foregroundStyle(.green)
            if item.effectiveVariant == "list" {
                VStack(alignment: .leading, spacing: 0) {
                    ForEach(Array(loader.quotes.prefix(2).enumerated()), id: \.offset) { _, q in
                        HStack(spacing: 4) {
                            Text(Self.pair(q)).font(.system(size: style.height * 0.14)).foregroundStyle(.secondary)
                            Text(NumberText.price(q.rate)).font(.system(size: style.height * 0.16, weight: .semibold)).monospacedDigit()
                        }
                    }
                }
            } else if let q = loader.quotes.first {
                VStack(alignment: .leading, spacing: 0) {
                    Text(NumberText.price(q.rate)).font(.system(size: style.height * 0.2, weight: .semibold)).monospacedDigit()
                    HStack(spacing: 4) {
                        Text(Self.pair(q)).font(.system(size: style.height * 0.13)).foregroundStyle(.secondary)
                        ChangeText(percent: q.changePercent, size: style.height * 0.13)
                    }
                }
            } else {
                TileText(value: loader.error ?? L.t("Loading…"), style: style)
            }
        }
        .contentShape(Rectangle())
        .onTapGesture { open = true }
        .onAppear(perform: start)
        .onChange(of: item.settings) { _, _ in start() }
        .popover(isPresented: $open, arrowEdge: .top) {
            WidgetPanel(title: L.t("Exchange rates"), symbol: "dollarsign.arrow.circlepath") {
                QuoteList(rows: loader.quotes.map { ($0.symbol, Self.pair($0), $0.rate, $0.changePercent, $0.history) })
            } footer: {
                SettingsFields(fields: [(L.t("Base currency"), "base", "USD"), (L.t("Currencies and coins"), "targets", "TRY, EUR")],
                               item: item, model: model,
                               note: L.t("Rates from the European Central Bank (working days), crypto prices from CoinGecko. For example: EUR, GBP, BTC, ETH."))
            }
        }
    }

    private func start() {
        let base = self.base
        let targets = self.targets
        loader.start(key: base + ":" + targets.joined(separator: ","), interval: 3600) {
            try await MarketService.currencies(base: base, targets: targets)
        }
    }

    static func pair(_ q: CurrencyQuote) -> String { "\(q.base)/\(q.target)" }
}

// MARK: - Hisseler

struct StocksWidget: View {
    let item: DockItem
    let style: DockStyle
    @ObservedObject var model: DockModel
    @StateObject private var loader = QuoteLoader<StockQuote>()
    @State private var open = false

    private var symbols: [String] { StockData.splitSymbols(item.stringSetting("symbols", default: "AAPL, MSFT")) }

    var body: some View {
        HStack(spacing: 6) {
            Image(systemName: "chart.line.uptrend.xyaxis").font(.system(size: style.iconSize * 0.45)).foregroundStyle(.green)
            if item.effectiveVariant == "list" {
                VStack(alignment: .leading, spacing: 0) {
                    ForEach(Array(loader.quotes.prefix(2).enumerated()), id: \.offset) { _, q in
                        HStack(spacing: 4) {
                            Text(q.symbol).font(.system(size: style.height * 0.14, weight: .semibold))
                            ChangeText(percent: q.changePercent, size: style.height * 0.14)
                        }
                    }
                }
            } else if let q = loader.quotes.first {
                VStack(alignment: .leading, spacing: 0) {
                    Text(NumberText.price(q.close)).font(.system(size: style.height * 0.2, weight: .semibold)).monospacedDigit()
                    HStack(spacing: 4) {
                        Text(q.symbol).font(.system(size: style.height * 0.13)).foregroundStyle(.secondary)
                        ChangeText(percent: q.changePercent, size: style.height * 0.13)
                    }
                }
            } else {
                TileText(value: loader.error ?? L.t("Loading…"), style: style)
            }
        }
        .contentShape(Rectangle())
        .onTapGesture { open = true }
        .onAppear(perform: start)
        .onChange(of: item.settings) { _, _ in start() }
        .popover(isPresented: $open, arrowEdge: .top) {
            WidgetPanel(title: L.t("Stocks"), symbol: "chart.line.uptrend.xyaxis") {
                QuoteList(rows: loader.quotes.map { ($0.symbol, $0.symbol, $0.close, $0.changePercent, $0.history) })
            } footer: {
                SettingsFields(fields: [(L.t("Symbols"), "symbols", "AAPL, MSFT")], item: item, model: model,
                               note: L.t("Delayed prices from Stooq. Symbols like AAPL (US market), thyao.tr or ^spx, separated by commas."))
            }
        }
    }

    private func start() {
        let symbols = self.symbols
        loader.start(key: symbols.joined(separator: ","), interval: 15 * 60) {
            try await MarketService.stocks(symbols)
        }
    }
}

/// Panelde fiyat listesi: ad, fiyat, degisim ve egilim.
private struct QuoteList: View {
    let rows: [(id: String, name: String, price: Double, change: Double?, history: [Double])]

    var body: some View {
        if rows.isEmpty {
            PanelEmptyState(symbol: "chart.line.uptrend.xyaxis", title: L.t("No prices yet"))
        } else {
            VStack(spacing: 6) {
                ForEach(rows, id: \.id) { row in
                    HStack(spacing: 8) {
                        Text(row.name).font(.callout.weight(.semibold)).frame(width: 90, alignment: .leading)
                        Sparkline(values: row.history, color: (row.change ?? 0) >= 0 ? .green : .red)
                            .frame(width: 70, height: 20)
                        Spacer()
                        VStack(alignment: .trailing, spacing: 0) {
                            Text(NumberText.price(row.price)).font(.callout).monospacedDigit()
                            ChangeText(percent: row.change)
                        }
                    }
                }
            }
        }
    }
}

/// Widget ayari metin alanlari; Return ya da Kaydet ile yazilir.
private struct SettingsFields: View {
    let fields: [(label: String, key: String, fallback: String)]
    let item: DockItem
    @ObservedObject var model: DockModel
    let note: String
    @State private var values: [String: String] = [:]

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            Divider()
            ForEach(fields, id: \.key) { field in
                TextField(field.label, text: Binding(get: { values[field.key] ?? "" }, set: { values[field.key] = $0 }))
                    .onSubmit(save)
            }
            RowNote(note)
            HStack { Spacer(); Button(L.t("Save"), action: save) }
        }
        .onAppear {
            for f in fields { values[f.key] = item.stringSetting(f.key, default: f.fallback) }
        }
    }

    private func save() {
        for f in fields {
            let v = (values[f.key] ?? "").trimmingCharacters(in: .whitespaces)
            model.setSetting(item.id, f.key, .string(v.isEmpty ? f.fallback : v))
        }
    }
}

// MARK: - Ping

/// Her hedef icin bir olcer; ayni hedefi izleyen widget'lar paylasir (Windows: PingService).
@MainActor
final class PingMonitor: ObservableObject {
    private static var monitors: [String: PingMonitor] = [:]

    static func shared(_ target: String) -> PingMonitor {
        if let m = monitors[target] { return m }
        let m = PingMonitor(target: target)
        monitors[target] = m
        return m
    }

    let target: String
    @Published private(set) var stats = PingStats()
    private var timer: Timer?
    private var busy = false

    private init(target: String) { self.target = target }

    func start() {
        guard timer == nil else { return }
        sample()
        timer = Timer.scheduledTimer(withTimeInterval: 5, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated { self?.sample() }
        }
    }

    private func sample() {
        guard !busy else { return }
        busy = true
        let target = self.target
        Task { @MainActor in
            let milliseconds = await PingRunner.ping(target)
            stats.add(milliseconds)
            busy = false
        }
    }
}

struct PingWidget: View {
    let item: DockItem
    let style: DockStyle
    @ObservedObject var model: DockModel
    @State private var open = false

    private var target: String { PingStats.target(item.stringSetting("target", default: PingStats.defaultTarget)) }

    var body: some View {
        PingTile(monitor: PingMonitor.shared(target), item: item, style: style)
            .contentShape(Rectangle())
            .onTapGesture { open = true }
            .popover(isPresented: $open, arrowEdge: .top) {
                PingPanel(monitor: PingMonitor.shared(target), item: item, model: model)
            }
    }
}

private struct PingTile: View {
    @ObservedObject var monitor: PingMonitor
    let item: DockItem
    let style: DockStyle

    var body: some View {
        HStack(spacing: 5) {
            Image(systemName: "wifi").font(.system(size: style.iconSize * 0.45)).foregroundStyle(Self.color(monitor.stats.latest))
            Text(monitor.stats.latest.map { L.t("{0} ms", Int($0.rounded())) } ?? (monitor.stats.samples.isEmpty ? "…" : L.t("Lost")))
                .font(.system(size: style.height * 0.2, weight: .semibold)).monospacedDigit()
            if item.effectiveVariant == "chart" {
                Sparkline(values: monitor.stats.history, color: Self.color(monitor.stats.average))
                    .frame(width: style.itemHeight * 1.2, height: style.itemHeight * 0.45)
            }
        }
        .onAppear { monitor.start() }
        .help(L.t("Ping to {0}", monitor.target))
    }

    static func color(_ ms: Double?) -> Color {
        switch PingStats.quality(ms) {
        case .good: .green
        case .fair: .orange
        case .poor: .red
        }
    }
}

private struct PingPanel: View {
    @ObservedObject var monitor: PingMonitor
    let item: DockItem
    @ObservedObject var model: DockModel
    @State private var target = ""

    var body: some View {
        WidgetPanel(title: L.t("Ping to {0}", monitor.target), symbol: "wifi", width: .narrow) {
            Sparkline(values: monitor.stats.history, color: PingTile.color(monitor.stats.average)).frame(height: 40)
            Grid(alignment: .leading, horizontalSpacing: 16, verticalSpacing: 4) {
                GridRow { Text(L.t("Average")).foregroundStyle(.secondary); Text(Self.ms(monitor.stats.average)) }
                GridRow { Text(L.t("Fastest")).foregroundStyle(.secondary); Text(Self.ms(monitor.stats.min)) }
                GridRow { Text(L.t("Slowest")).foregroundStyle(.secondary); Text(Self.ms(monitor.stats.max)) }
                GridRow { Text(L.t("Packet loss")).foregroundStyle(.secondary); Text(String(format: "%.0f%%", monitor.stats.loss)) }
            }
            .font(.callout.monospacedDigit())
        } footer: {
            HStack {
                TextField(L.t("Server"), text: $target, prompt: Text(verbatim: PingStats.defaultTarget))
                    .onSubmit(save)
                Button(L.t("Save"), action: save)
            }
        }
        .onAppear { target = item.stringSetting("target", default: "") }
    }

    private func save() {
        model.setSetting(item.id, "target", .string(PingStats.target(target)))
    }

    static func ms(_ value: Double?) -> String { value.map { L.t("{0} ms", Int($0.rounded())) } ?? "-" }
}
