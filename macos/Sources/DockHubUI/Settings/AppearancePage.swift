import SwiftUI
import DockHubCore

/// Windows karsiligi: SettingsWindow.xaml "Appearance" sayfasi ve konum satirlari.
/// Yalniz macOS dock'unun gercekten uyguladigi ayarlar var.
struct AppearancePage: View {
    @ObservedObject var store: SettingsStore

    var body: some View {
        Form {
            Section("Style") {
                Picker("Theme", selection: store.binding(\.theme)) {
                    Text("Dark").tag(ThemePreference.dark)
                    Text("Light").tag(ThemePreference.light)
                    Text("System").tag(ThemePreference.system)
                }
                .pickerStyle(.segmented)
                VStack(alignment: .leading, spacing: 4) {
                    Picker("Backdrop", selection: store.binding(\.backdrop)) {
                        Text("Blur").tag(BackdropKind.blur)
                        Text("Acrylic").tag(BackdropKind.acrylic)
                        Text("Solid").tag(BackdropKind.solid)
                    }
                    .pickerStyle(.segmented)
                    RowNote("Blur is a frosted glass, Acrylic a lighter material, Solid has no transparency.")
                }
                LabeledContent {
                    CommitSlider(value: store.config.tintOpacity, range: 0...1, step: 0.05,
                                 label: { "\(Int(($0 * 100).rounded()))%" },
                                 commit: { v in store.update { $0.tintOpacity = v } })
                } label: {
                    Text("Tint opacity")
                    RowNote("Color layer over the glass.")
                }
                Toggle(isOn: store.binding(\.hoverEffect)) {
                    Text("Hover effect")
                    RowNote("App icons grow slightly under the pointer.")
                }
            }

            Section("Size and shape") {
                Picker("Size", selection: store.binding(\.size)) {
                    Text("Small").tag(DockSize.small)
                    Text("Medium").tag(DockSize.medium)
                    Text("Large").tag(DockSize.large)
                }
                .pickerStyle(.segmented)
                VStack(alignment: .leading, spacing: 4) {
                    Picker("Shape", selection: store.binding(\.layout)) {
                        Text("Floating").tag(DockLayout.floating)
                        Text("Attached").tag(DockLayout.attached)
                    }
                    .pickerStyle(.segmented)
                    RowNote("Floating: detached with rounded corners and margins. Attached: sits flush against the screen edge.")
                }
                LabeledContent("Edge margin") {
                    CommitSlider(value: store.config.edgeMargin, range: 0...24, step: 1,
                                 label: { "\(Int($0)) pt" },
                                 commit: { v in store.update { $0.edgeMargin = v } })
                }
                .disabled(store.config.layout == .attached)
                Picker("Width", selection: store.binding(\.widthMode)) {
                    Text("Full width").tag(DockWidthMode.full)
                    Text("Fit content").tag(DockWidthMode.fit)
                }
                .pickerStyle(.segmented)
                Picker("Item alignment", selection: store.binding(\.alignment)) {
                    Text("Start").tag(DockAlignment.start)
                    Text("Center").tag(DockAlignment.center)
                }
                .pickerStyle(.segmented)
            }

            Section("Position") {
                Picker("Screen edge", selection: store.binding(\.edge)) {
                    Text("Bottom").tag(DockEdge.bottom)
                    Text("Top").tag(DockEdge.top)
                    Text("Left").tag(DockEdge.left)
                    Text("Right").tag(DockEdge.right)
                }
                .pickerStyle(.segmented)
            }
        }
        .formStyle(.grouped)
    }
}
