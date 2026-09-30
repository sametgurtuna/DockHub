import SwiftUI

/// Widget'a tiklayinca acilan panelin ortak sablonu. Windows karsiligi:
/// Controls/WidgetFlyout (baslik, simge, baslik eylemleri, icerik, alt bilgi;
/// Narrow 280 / Standard 340 / Wide 420 genislik, 14 ic bosluk).
/// Acma/kapama SwiftUI `popover`'da; Esc ve disari tiklama onu kapatir.
struct WidgetPanel<Content: View, Actions: View, Footer: View>: View {
    enum Width {
        case narrow, standard, wide
        var points: CGFloat {
            switch self {
            case .narrow: 280
            case .standard: 340
            case .wide: 420
            }
        }
    }

    let title: String
    let symbol: String
    var width: Width = .standard
    @ViewBuilder var actions: () -> Actions
    @ViewBuilder var content: () -> Content
    @ViewBuilder var footer: () -> Footer

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack(spacing: 8) {
                Image(systemName: symbol)
                    .foregroundStyle(Color.accentColor)
                Text(title).font(.headline)
                Spacer(minLength: 8)
                actions()
                    .buttonStyle(.borderless)
            }
            content()
            footer()
        }
        .padding(14)
        .frame(width: width.points, alignment: .leading)
    }
}

extension WidgetPanel where Actions == EmptyView, Footer == EmptyView {
    init(title: String, symbol: String, width: Width = .standard, @ViewBuilder content: @escaping () -> Content) {
        self.init(title: title, symbol: symbol, width: width, actions: { EmptyView() }, content: content, footer: { EmptyView() })
    }
}

extension WidgetPanel where Actions == EmptyView {
    init(title: String, symbol: String, width: Width = .standard,
         @ViewBuilder content: @escaping () -> Content, @ViewBuilder footer: @escaping () -> Footer) {
        self.init(title: title, symbol: symbol, width: width, actions: { EmptyView() }, content: content, footer: footer)
    }
}

extension WidgetPanel where Footer == EmptyView {
    init(title: String, symbol: String, width: Width = .standard,
         @ViewBuilder actions: @escaping () -> Actions, @ViewBuilder content: @escaping () -> Content) {
        self.init(title: title, symbol: symbol, width: width, actions: actions, content: content, footer: { EmptyView() })
    }
}

/// Panelin bos durumu: simge, baslik, aciklama ve istege bagli eylem (Windows: EmptyState).
struct PanelEmptyState: View {
    let symbol: String
    let title: String
    var message: String = ""
    var actionTitle: String?
    var action: (() -> Void)?

    var body: some View {
        VStack(spacing: 6) {
            Image(systemName: symbol)
                .font(.system(size: 24))
                .foregroundStyle(.secondary)
            Text(title).font(.callout.weight(.semibold))
            if !message.isEmpty {
                Text(message).font(.caption).foregroundStyle(.secondary)
                    .multilineTextAlignment(.center)
                    .fixedSize(horizontal: false, vertical: true)
            }
            if let actionTitle, let action {
                Button(actionTitle, action: action).controlSize(.small)
            }
        }
        .frame(maxWidth: .infinity)
        .padding(.vertical, 12)
    }
}
