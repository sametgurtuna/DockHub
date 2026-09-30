import SwiftUI
import DockHubCore

/// Windows karsiligi: SettingsWindow.xaml "Appearance" sayfasi ve konum satirlari.
/// Yalniz macOS dock'unun gercekten uyguladigi ayarlar var.
struct AppearancePage: View {
    @ObservedObject var store: SettingsStore

    var body: some View {
        Form {
            Section(L.t("Style")) {
                Picker(L.t("Theme"), selection: store.binding(\.theme)) {
                    Text(L.t("Dark")).tag(ThemePreference.dark)
                    Text(L.t("Light")).tag(ThemePreference.light)
                    Text(L.t("System")).tag(ThemePreference.system)
                }
                .pickerStyle(.segmented)
                VStack(alignment: .leading, spacing: 4) {
                    Picker(L.t("Backdrop"), selection: store.binding(\.backdrop)) {
                        Text(L.t("Blur")).tag(BackdropKind.blur)
                        Text(L.t("Acrylic")).tag(BackdropKind.acrylic)
                        Text(L.t("Transparent")).tag(BackdropKind.transparent)
                        Text(L.t("Solid")).tag(BackdropKind.solid)
                    }
                    .pickerStyle(.segmented)
                    RowNote(L.t("Blur is a frosted glass, Acrylic a lighter material, Transparent has no blur (the desktop shows through), Solid has no transparency."))
                }
                LabeledContent {
                    CommitSlider(value: store.config.tintOpacity, range: 0...1, step: 0.05,
                                 label: { "\(Int(($0 * 100).rounded()))%" },
                                 commit: { v in store.update { $0.tintOpacity = v } })
                } label: {
                    Text(L.t("Tint opacity"))
                    RowNote(L.t("Color layer over the glass."))
                }
                VStack(alignment: .leading, spacing: 4) {
                    Picker(L.t("Widget style"), selection: store.binding(\.widgetStyle)) {
                        Text(L.t("Cards")).tag(WidgetStyle.cards)
                        Text(L.t("Seamless")).tag(WidgetStyle.seamless)
                    }
                    .pickerStyle(.segmented)
                    RowNote(L.t("Cards puts every widget on a card of its own. Seamless sets them right on the dock, with a thin line between two widgets."))
                }
                Toggle(isOn: store.binding(\.hoverEffect)) {
                    Text(L.t("Hover effect"))
                    RowNote(L.t("App icons grow slightly under the pointer."))
                }
            }

            Section(L.t("Size and shape")) {
                Picker(L.t("Size"), selection: store.binding(\.size)) {
                    Text(L.t("Small")).tag(DockSize.small)
                    Text(L.t("Medium")).tag(DockSize.medium)
                    Text(L.t("Large")).tag(DockSize.large)
                }
                .pickerStyle(.segmented)
                VStack(alignment: .leading, spacing: 4) {
                    Picker(L.t("Shape"), selection: store.binding(\.layout)) {
                        Text(L.t("Floating")).tag(DockLayout.floating)
                        Text(L.t("Attached")).tag(DockLayout.attached)
                    }
                    .pickerStyle(.segmented)
                    RowNote(L.t("Floating: detached with rounded corners and margins. Attached: sits flush against the screen edge."))
                }
                LabeledContent(L.t("Edge margin")) {
                    CommitSlider(value: store.config.edgeMargin, range: 0...24, step: 1,
                                 label: { "\(Int($0)) pt" },
                                 commit: { v in store.update { $0.edgeMargin = v } })
                }
                .disabled(store.config.layout == .attached)
                Picker(L.t("Width"), selection: store.binding(\.widthMode)) {
                    Text(L.t("Full width")).tag(DockWidthMode.full)
                    Text(L.t("Fit content")).tag(DockWidthMode.fit)
                }
                .pickerStyle(.segmented)
                Picker(L.t("Item alignment"), selection: store.binding(\.alignment)) {
                    Text(L.t("Start")).tag(DockAlignment.start)
                    Text(L.t("Center")).tag(DockAlignment.center)
                }
                .pickerStyle(.segmented)
            }

            Section(L.t("Position")) {
                Picker(L.t("Screen edge"), selection: store.binding(\.edge)) {
                    Text(L.t("Bottom")).tag(DockEdge.bottom)
                    Text(L.t("Top")).tag(DockEdge.top)
                    Text(L.t("Left")).tag(DockEdge.left)
                    Text(L.t("Right")).tag(DockEdge.right)
                }
                .pickerStyle(.segmented)
            }
        }
        .formStyle(.grouped)
    }
}
