import AppKit
import SwiftUI
import Combine
import UniformTypeIdentifiers
import DockHubCore
import DockHubPlatform

// Windows 0.8-1.0 widget'larinin Mac karsiliklari: takvim, pano gecmisi, klasor yigini,
// yapilacaklar ve ekran goruntusu. Kimlikler, duzenler ve ayar anahtarlari Windows'la ayni
// (config.json iki tarafta calisir); her panel ortak WidgetPanel sablonunda.

/// Dock kutucugunun ortak iki satiri: kalin deger ve soluk alt satir.
struct TileText: View {
    let value: String
    var detail: String?
    let style: DockStyle
    var width: CGFloat?

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            Text(value).font(.system(size: style.height * 0.2, weight: .semibold)).lineLimit(1)
            if let detail {
                Text(detail).font(.system(size: style.height * 0.14)).foregroundStyle(.secondary).lineLimit(1)
            }
        }
        .frame(maxWidth: width, alignment: .leading)
    }
}

// MARK: - Takvim

@MainActor
final class CalendarLoader: ObservableObject {
    @Published var entries: [CalendarEntry] = []
    @Published var error: String?
    @Published var loading = false
    private var timer: Timer?
    private var links: [String] = []
    private var remindMinutes = 5

    func start(links: [String], remindMinutes: Int) {
        let changed = links != self.links
        self.links = links
        self.remindMinutes = remindMinutes
        if timer == nil {
            timer = Timer.scheduledTimer(withTimeInterval: 15 * 60, repeats: true) { [weak self] _ in
                MainActor.assumeIsolated { self?.load() }
            }
        }
        if changed || entries.isEmpty { load() }
    }

    func load() {
        guard !links.isEmpty else { entries = []; error = nil; return }
        loading = true
        let links = self.links
        Task { @MainActor in
            do {
                entries = try await CalendarFeeds.upcoming(links)
                error = nil
                scheduleReminders()
            } catch {
                self.error = L.t("Couldn't read the calendar. Check the link.")
            }
            loading = false
        }
    }

    /// Baslamadan `remindMinutes` once bildirim (Windows: RemindMinutes). Ayni kimlik yeniden planlanirsa degisir.
    private func scheduleReminders() {
        guard remindMinutes > 0 else { return }
        for e in entries.prefix(10) where !e.allDay {
            let seconds = e.start.addingTimeInterval(-Double(remindMinutes) * 60).timeIntervalSinceNow
            guard seconds > 0 else { continue }
            Notifier.gonder(baslik: e.title, metin: L.t("Starts in {0} min", remindMinutes),
                            after: seconds, id: "calendar-\(e.title)-\(Int(e.start.timeIntervalSince1970))")
        }
    }
}

struct CalendarWidget: View {
    let item: DockItem
    let style: DockStyle
    @ObservedObject var model: DockModel
    @StateObject private var loader = CalendarLoader()
    @State private var open = false

    private var links: [String] {
        item.stringSetting("feeds", default: "").split(whereSeparator: \.isNewline)
            .map { $0.trimmingCharacters(in: .whitespaces) }.filter { !$0.isEmpty }
    }
    private var remind: Int { Int(item.numberSetting("remindMinutes", default: 5)) }

    var body: some View {
        TimelineView(.periodic(from: .now, by: 60)) { ctx in
            let next = loader.entries.first { $0.end > ctx.date && !$0.allDay } ?? loader.entries.first
            HStack(spacing: 5) {
                Image(systemName: "calendar").font(.system(size: style.iconSize * 0.5)).foregroundStyle(.red)
                if links.isEmpty {
                    TileText(value: L.t("Calendar"), detail: L.t("Add a link"), style: style)
                } else if let next {
                    if item.effectiveVariant == "compact" {
                        TileText(value: Self.until(next, now: ctx.date), style: style)
                    } else {
                        TileText(value: next.title, detail: Self.when(next, now: ctx.date), style: style, width: style.itemHeight * 2.6)
                    }
                } else {
                    TileText(value: loader.error == nil ? L.t("No events") : L.t("Error"), style: style)
                }
            }
        }
        .contentShape(Rectangle())
        .onTapGesture { open = true }
        .onAppear { loader.start(links: links, remindMinutes: remind) }
        .onChange(of: item.settings) { _, _ in loader.start(links: links, remindMinutes: remind) }
        .popover(isPresented: $open, arrowEdge: .top) {
            CalendarPanel(item: item, model: model, loader: loader)
        }
    }

    /// "Now" ya da "in 12 min" / "in 2 h".
    static func until(_ e: CalendarEntry, now: Date) -> String {
        if e.start <= now { return L.t("Now") }
        let minutes = Int(e.start.timeIntervalSince(now) / 60)
        return minutes < 60 ? L.t("in {0} min", minutes) : L.t("in {0} h", minutes / 60)
    }

    static func when(_ e: CalendarEntry, now: Date) -> String {
        if e.allDay { return L.t("All day") }
        if e.start <= now { return L.t("Now") }
        let f = DateFormatter()
        f.dateStyle = Calendar.current.isDate(e.start, inSameDayAs: now) ? .none : .short
        f.timeStyle = .short
        return f.string(from: e.start)
    }
}

private struct CalendarPanel: View {
    let item: DockItem
    @ObservedObject var model: DockModel
    @ObservedObject var loader: CalendarLoader
    @State private var feeds = ""
    @State private var remind = 5
    @State private var editing = false

    var body: some View {
        WidgetPanel(title: L.t("Calendar"), symbol: "calendar", width: .wide) {
            Button { editing.toggle() } label: { Image(systemName: "gearshape") }.help(L.t("Settings"))
            Button { loader.load() } label: { Image(systemName: "arrow.clockwise") }.help(L.t("Refresh now"))
        } content: {
            if editing {
                VStack(alignment: .leading, spacing: 6) {
                    Text(L.t("Calendar links (iCal, one per line)")).font(.caption)
                    TextEditor(text: $feeds).font(.system(size: 11)).frame(height: 70)
                        .overlay(RoundedRectangle(cornerRadius: 5).stroke(Color.secondary.opacity(0.3)))
                    RowNote(L.t("Google Calendar: Settings › your calendar › Secret address in iCal format. Outlook: Settings › Calendar › Shared calendars › Publish a calendar › ICS link."))
                    Stepper(L.t("Reminder: {0} min before", remind), value: $remind, in: 0...60)
                    HStack { Spacer(); Button(L.t("Save")) { save() }.keyboardShortcut(.defaultAction) }
                }
            } else if loader.entries.isEmpty {
                PanelEmptyState(symbol: "calendar", title: loader.error ?? L.t("No events in the next 7 days"),
                                actionTitle: L.t("Add a calendar link")) { editing = true }
            } else {
                ScrollView {
                    VStack(alignment: .leading, spacing: 8) {
                        ForEach(Array(loader.entries.prefix(12).enumerated()), id: \.offset) { _, e in
                            HStack(alignment: .top, spacing: 8) {
                                Text(CalendarWidget.when(e, now: Date())).font(.caption.monospacedDigit())
                                    .foregroundStyle(.secondary).frame(width: 80, alignment: .leading)
                                VStack(alignment: .leading, spacing: 1) {
                                    Text(e.title).font(.callout).lineLimit(2)
                                    if let location = e.location {
                                        Text(location).font(.caption).foregroundStyle(.secondary).lineLimit(1)
                                    }
                                }
                                Spacer(minLength: 4)
                                if let join = e.joinURL, let url = URL(string: join) {
                                    Button(L.t("Join")) { NSWorkspace.shared.open(url) }.controlSize(.small)
                                }
                            }
                        }
                    }
                }
                .frame(maxHeight: 300)
            }
        }
        .onAppear {
            feeds = item.stringSetting("feeds", default: "")
            remind = Int(item.numberSetting("remindMinutes", default: 5))
            editing = feeds.isEmpty
        }
    }

    private func save() {
        model.setSetting(item.id, "feeds", .string(feeds))
        model.setSetting(item.id, "remindMinutes", .number(Double(remind)))
        editing = false
    }
}

// MARK: - Pano gecmisi

struct ClipboardWidget: View {
    let item: DockItem
    let style: DockStyle
    @ObservedObject private var monitor = ClipboardMonitor.shared
    @State private var open = false

    var body: some View {
        HStack(spacing: 5) {
            Image(systemName: "doc.on.clipboard").font(.system(size: style.iconSize * 0.5)).foregroundStyle(.cyan)
            if item.effectiveVariant != "icon" {
                let latest = monitor.history.entries.first { !$0.pinned } ?? monitor.history.entries.first
                TileText(value: latest.map(Self.label) ?? L.t("Clipboard history"),
                         detail: L.t("{0} items", monitor.history.entries.count), style: style, width: style.itemHeight * 2.4)
            }
        }
        .contentShape(Rectangle())
        .onTapGesture { open = true }
        .onAppear { monitor.start() }
        .popover(isPresented: $open, arrowEdge: .top) { ClipboardPanel(close: { open = false }) }
    }

    static func label(_ e: ClipboardEntry) -> String {
        guard let text = e.text else { return L.t("Image") }
        return text.split(whereSeparator: \.isNewline).first.map(String.init) ?? text
    }
}

private struct ClipboardPanel: View {
    let close: () -> Void
    @ObservedObject private var monitor = ClipboardMonitor.shared

    var body: some View {
        WidgetPanel(title: L.t("Clipboard history"), symbol: "doc.on.clipboard") {
            Button(L.t("Clear")) { monitor.clear() }.disabled(monitor.history.entries.allSatisfy(\.pinned))
        } content: {
            if monitor.history.entries.isEmpty {
                PanelEmptyState(symbol: "doc.on.clipboard", title: L.t("Nothing copied yet"),
                                message: L.t("Kept only in memory; password managers are never recorded."))
            } else {
                ScrollView {
                    VStack(spacing: 4) {
                        ForEach(monitor.history.entries) { entry in
                            HStack(spacing: 8) {
                                if let data = entry.image, let image = NSImage(data: data) {
                                    Image(nsImage: image).resizable().scaledToFit().frame(height: 40)
                                } else {
                                    Text(entry.text ?? "").font(.callout).lineLimit(2)
                                        .frame(maxWidth: .infinity, alignment: .leading)
                                }
                                Spacer(minLength: 4)
                                Button { monitor.togglePin(entry.id) } label: {
                                    Image(systemName: entry.pinned ? "pin.fill" : "pin")
                                }
                                .buttonStyle(.borderless)
                                .help(entry.pinned ? L.t("Unpin") : L.t("Pin"))
                                Button { monitor.remove(entry.id) } label: { Image(systemName: "xmark") }
                                    .buttonStyle(.borderless).help(L.t("Remove"))
                            }
                            .padding(6)
                            .background(RoundedRectangle(cornerRadius: 6).fill(Color.primary.opacity(0.05)))
                            .contentShape(Rectangle())
                            .onTapGesture { monitor.copy(entry); close() }
                            .help(L.t("Click to copy again"))
                        }
                    }
                }
                .frame(maxHeight: 340)
            }
        }
    }
}

// MARK: - Klasor yigini

struct StackWidget: View {
    let item: DockItem
    let style: DockStyle
    @ObservedObject var model: DockModel
    @State private var open = false
    @State private var files: [StackFile] = []
    @State private var folder = FolderStackService.downloads

    var body: some View {
        HStack(spacing: 5) {
            Image(systemName: "folder.fill").font(.system(size: style.iconSize * 0.5)).foregroundStyle(.yellow)
            if item.effectiveVariant == "details" {
                TileText(value: files.first?.name ?? folder.lastPathComponent,
                         detail: folder.lastPathComponent, style: style, width: style.itemHeight * 2.4)
            } else {
                Text(verbatim: "\(files.count)").font(.system(size: style.height * 0.2, weight: .semibold)).monospacedDigit()
            }
        }
        .contentShape(Rectangle())
        .onTapGesture { reload(); open = true }
        .onAppear(perform: reload)
        .onChange(of: item.settings) { _, _ in reload() }
        .popover(isPresented: $open, arrowEdge: .top) {
            StackPanel(item: item, model: model, folder: folder, files: files, reload: reload)
        }
    }

    private func reload() {
        let result = FolderStackService.files(folder: item.stringSetting("folder", default: ""),
                                              sort: item.stringSetting("sort", default: "date"))
        folder = result.folder
        files = result.files
    }
}

private struct StackPanel: View {
    let item: DockItem
    @ObservedObject var model: DockModel
    let folder: URL
    let files: [StackFile]
    let reload: () -> Void

    var body: some View {
        WidgetPanel(title: folder.lastPathComponent, symbol: "folder", width: .wide) {
            Picker("", selection: Binding(get: { item.stringSetting("sort", default: "date") },
                                          set: { model.setSetting(item.id, "sort", .string($0)); reload() })) {
                Text(L.t("Newest first")).tag("date")
                Text(L.t("By name")).tag("name")
            }
            .labelsHidden().fixedSize()
            Button { choose() } label: { Image(systemName: "folder.badge.gearshape") }.help(L.t("Choose folder…"))
            Button { NSWorkspace.shared.open(folder) } label: { Image(systemName: "arrow.up.forward.app") }
                .help(L.t("Open folder"))
        } content: {
            if files.isEmpty {
                PanelEmptyState(symbol: "folder", title: L.t("This folder is empty"))
            } else {
                ScrollView {
                    LazyVGrid(columns: [GridItem(.adaptive(minimum: 88), spacing: 8)], spacing: 8) {
                        ForEach(files, id: \.url) { file in
                            VStack(spacing: 3) {
                                Image(nsImage: NSWorkspace.shared.icon(forFile: file.url.path))
                                    .resizable().frame(width: 40, height: 40)
                                Text(file.name).font(.caption2).lineLimit(2).multilineTextAlignment(.center)
                            }
                            .frame(width: 88)
                            .contentShape(Rectangle())
                            .onTapGesture { NSWorkspace.shared.open(file.url) }
                            // Dosyayi baska uygulamaya surukle (Windows'ta da oyle).
                            .onDrag { NSItemProvider(contentsOf: file.url) ?? NSItemProvider() }
                            .help(file.name)
                        }
                    }
                }
                .frame(maxHeight: 320)
            }
        }
    }

    private func choose() {
        let panel = NSOpenPanel()
        panel.canChooseDirectories = true
        panel.canChooseFiles = false
        panel.directoryURL = folder
        panel.prompt = L.t("Choose")
        guard panel.runModal() == .OK, let url = panel.url else { return }
        model.setSetting(item.id, "folder", .string(url.path))
        reload()
    }
}

// MARK: - Yapilacaklar

@MainActor
final class TodoLoader: ObservableObject {
    @Published var tasks: [TodoTask] = []
    @Published var error: String?
    @Published var loading = false
    private(set) var itemId = ""
    private(set) var todoist = false

    func start(itemId: String, todoist: Bool) {
        self.itemId = itemId
        self.todoist = todoist
        load()
    }

    var token: String? { KeychainStore.get("todoist-\(itemId)") }

    func load() {
        guard todoist else {
            tasks = TodoService.loadLocal(itemId).items.map { TodoTask(id: $0.id, text: $0.text, due: nil) }
            error = nil
            return
        }
        guard let token, !token.isEmpty else {
            tasks = []
            error = L.t("Add your Todoist API token in the settings.")
            return
        }
        loading = true
        Task { @MainActor in
            do {
                tasks = try await TodoService.todoistToday(token: token)
                error = nil
            } catch is TodoService.Unauthorized {
                error = L.t("Todoist didn't accept the API token.")
            } catch {
                self.error = L.t("Couldn't reach Todoist.")
            }
            loading = false
        }
    }

    func complete(_ task: TodoTask) {
        tasks.removeAll { $0.id == task.id }
        if todoist, let token {
            Task { @MainActor in
                do { try await TodoService.todoistClose(token: token, id: task.id) } catch { load() }
            }
        } else {
            var list = TodoService.loadLocal(itemId)
            list.items.removeAll { $0.id == task.id }
            TodoService.saveLocal(list, itemId)
        }
    }

    func add(_ text: String) {
        let trimmed = text.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty else { return }
        if todoist, let token {
            Task { @MainActor in
                do { try await TodoService.todoistAdd(token: token, text: trimmed) } catch { self.error = L.t("Couldn't reach Todoist.") }
                load()
            }
        } else {
            var list = TodoService.loadLocal(itemId)
            list.items.append(LocalTodoList.Item(text: trimmed))
            TodoService.saveLocal(list, itemId)
            load()
        }
    }
}

struct TodoWidget: View {
    let item: DockItem
    let style: DockStyle
    @ObservedObject var model: DockModel
    @StateObject private var loader = TodoLoader()
    @State private var open = false

    private var usesTodoist: Bool { item.stringSetting("source", default: "Local") == "Todoist" }

    var body: some View {
        HStack(spacing: 5) {
            Image(systemName: "checklist").font(.system(size: style.iconSize * 0.5)).foregroundStyle(.green)
            if item.effectiveVariant == "count" {
                Text(verbatim: "\(loader.tasks.count)").font(.system(size: style.height * 0.24, weight: .semibold)).monospacedDigit()
            } else {
                TileText(value: loader.tasks.first?.text ?? (loader.error == nil ? L.t("All done") : L.t("To do")),
                         detail: loader.tasks.count > 1 ? L.t("+{0} more", loader.tasks.count - 1) : nil,
                         style: style, width: style.itemHeight * 2.6)
            }
        }
        .contentShape(Rectangle())
        .onTapGesture { open = true }
        .onAppear { loader.start(itemId: item.id, todoist: usesTodoist) }
        .onChange(of: item.settings) { _, _ in loader.start(itemId: item.id, todoist: usesTodoist) }
        .popover(isPresented: $open, arrowEdge: .top) { TodoPanel(item: item, model: model, loader: loader) }
    }
}

private struct TodoPanel: View {
    let item: DockItem
    @ObservedObject var model: DockModel
    @ObservedObject var loader: TodoLoader
    @State private var newTask = ""
    @State private var settings = false
    @State private var token = ""

    var body: some View {
        WidgetPanel(title: L.t("To do"), symbol: "checklist", width: .standard) {
            Button { settings.toggle() } label: { Image(systemName: "gearshape") }.help(L.t("Settings"))
            Button { loader.load() } label: { Image(systemName: "arrow.clockwise") }.help(L.t("Refresh now"))
        } content: {
            if settings {
                VStack(alignment: .leading, spacing: 6) {
                    Picker(L.t("Tasks from"), selection: Binding(
                        get: { item.stringSetting("source", default: "Local") },
                        set: { model.setSetting(item.id, "source", .string($0)) })) {
                        Text(L.t("On this Mac")).tag("Local")
                        Text(verbatim: "Todoist").tag("Todoist")
                    }
                    if item.stringSetting("source", default: "Local") == "Todoist" {
                        SecureField(L.t("Todoist API token"), text: $token)
                            .onSubmit { saveToken() }
                        RowNote(L.t("Todoist › Settings › Integrations › Developer. The token stays in your Mac's keychain."))
                        HStack { Spacer(); Button(L.t("Save")) { saveToken() } }
                    }
                }
            } else {
                if let error = loader.error {
                    Text(error).font(.caption).foregroundStyle(.red)
                }
                if loader.tasks.isEmpty && loader.error == nil {
                    PanelEmptyState(symbol: "checkmark.circle", title: L.t("All done"))
                }
                ScrollView {
                    VStack(alignment: .leading, spacing: 4) {
                        ForEach(loader.tasks) { task in
                            HStack(spacing: 8) {
                                Button { loader.complete(task) } label: { Image(systemName: "circle") }
                                    .buttonStyle(.borderless).help(L.t("Complete"))
                                Text(task.text).font(.callout).lineLimit(2)
                                Spacer()
                                if let due = task.due, due < Calendar.current.startOfDay(for: Date()) {
                                    Text(L.t("Overdue")).font(.caption2).foregroundStyle(.red)
                                }
                            }
                        }
                    }
                }
                .frame(maxHeight: 260)
                TextField(L.t("Add a task"), text: $newTask)
                    .onSubmit { loader.add(newTask); newTask = "" }
            }
        }
        .onAppear { token = loader.token ?? "" }
    }

    private func saveToken() {
        KeychainStore.set(token.trimmingCharacters(in: .whitespaces), for: "todoist-\(item.id)")
        settings = false
        loader.load()
    }
}

// MARK: - Ekran goruntusu

struct ScreenshotWidget: View {
    let item: DockItem
    let style: DockStyle
    @ObservedObject var model: DockModel

    private var delay: Int { Int(item.numberSetting("delaySeconds", default: 0)) }
    private var copy: Bool { item.boolSetting("copyToClipboard", default: true) }

    var body: some View {
        HStack(spacing: 6) {
            Button { ScreenshotService.interactive() } label: {
                Image(systemName: "camera.viewfinder").font(.system(size: style.iconSize * 0.5))
            }
            .buttonStyle(.plain)
            .help(L.t("Take a screenshot of an area or a window"))
            if item.effectiveVariant == "buttons" {
                Button { ScreenshotService.captureAll(delay: delay, copy: copy) } label: {
                    Image(systemName: "rectangle.on.rectangle").font(.system(size: style.iconSize * 0.45))
                }
                .buttonStyle(.plain)
                .help(L.t("Save every screen to Pictures/Screenshots"))
            }
        }
        .foregroundStyle(.pink)
        .contextMenu {
            Button(L.t("Save every screen to Pictures/Screenshots")) { ScreenshotService.captureAll(delay: delay, copy: copy) }
            Menu(L.t("Delay")) {
                ForEach([0, 3, 5, 10], id: \.self) { s in
                    Button(s == 0 ? L.t("No delay") : L.t("{0} seconds", s)) {
                        model.setSetting(item.id, "delaySeconds", .number(Double(s)))
                    }
                }
            }
            Toggle(L.t("Also copy to the clipboard"), isOn: Binding(
                get: { copy }, set: { model.setSetting(item.id, "copyToClipboard", .bool($0)) }))
        }
    }
}
