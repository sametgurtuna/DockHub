import SwiftUI
import DockHubCore

/// Windows karsiligi: SettingsWindow.xaml "Profiles" sayfasi (Settings/SettingsWindow.Profiles.cs).
/// Her profil kendi dock ogelerini ve gorunumunu tasir; kurallar: ekran sayisi,
/// calisan uygulama, saat araligi.
struct ProfilesPage: View {
    @ObservedObject var store: SettingsStore
    @State private var newName = ""

    var body: some View {
        Form {
            Section {
                RowNote(L.t("Profiles keep their own dock items and look, for example Work, Gaming or Laptop. Switch from the dock's right-click menu or with a shortcut, or let one switch in by itself: when a number of displays is connected, while an app runs, or during set hours."))
                HStack {
                    TextField(L.t("Name"), text: $newName, prompt: Text(L.t("Profile {0}", store.config.profiles.count + 1)))
                        .onSubmit(save)
                    Button(L.t("Save current setup as a profile"), action: save)
                }
            }
            if store.config.profiles.isEmpty {
                Section {
                    Text(L.t("No profiles yet. Save your setup as one to switch between setups."))
                        .foregroundStyle(.secondary)
                }
            }
            ForEach(store.config.profiles) { profile in
                ProfileSection(store: store, profile: profile,
                               isActive: profile.id == store.config.activeProfileId)
            }
            if store.config.profiles.count > 1 {
                Section {
                    RowNote(L.t("When the app closes or the time ends, DockHub goes back to the profile you had. An app rule wins over a time rule."))
                }
            }
        }
        .formStyle(.grouped)
    }

    private func save() {
        store.saveProfile(newName)
        newName = ""
    }
}

/// Tek profilin satirlari: ad, gecis, silme ve kurallar.
private struct ProfileSection: View {
    @ObservedObject var store: SettingsStore
    let profile: DockProfile
    let isActive: Bool
    @State private var name = ""
    @State private var app = ""
    @State private var from = ""
    @State private var to = ""

    var body: some View {
        Section {
            HStack {
                TextField(L.t("Name"), text: $name)
                    .onSubmit { store.renameProfile(profile.id, name) }
                if isActive {
                    Text(L.t("Active")).font(.caption.weight(.semibold)).foregroundStyle(.green)
                } else {
                    Button(L.t("Switch")) { store.switchProfile(profile.id) }
                    Button(L.t("Delete"), role: .destructive) { store.deleteProfile(profile.id) }
                }
            }
            Picker(L.t("Displays"), selection: Binding(get: { profile.autoDisplayCount ?? 0 },
                                                       set: { store.setProfileDisplayCount(profile.id, $0 == 0 ? nil : $0) })) {
                Text(L.t("Off")).tag(0)
                ForEach(1...4, id: \.self) { Text(verbatim: "\($0)").tag($0) }
            }
            RowNote(L.t("Switch to this profile automatically when this many displays are connected."))
            VStack(alignment: .leading, spacing: 4) {
                TextField(L.t("App"), text: $app, prompt: Text(verbatim: "Steam"))
                    .onSubmit { store.setProfileApp(profile.id, app) }
                RowNote(L.t("App name, for example Steam or Xcode. The profile stays while the app runs. Press Return to save."))
            }
            HStack {
                TextField(L.t("From"), text: $from, prompt: Text(verbatim: "09:00"))
                TextField(L.t("To"), text: $to, prompt: Text(verbatim: "17:30"))
                Toggle(L.t("Weekdays only"), isOn: Binding(get: { profile.autoWeekdaysOnly },
                                                         set: { saveTime(weekdaysOnly: $0) }))
            }
            .onSubmit { saveTime(weekdaysOnly: profile.autoWeekdaysOnly) }
            if !from.isEmpty && ProfileRules.minutes(from) == nil || !to.isEmpty && ProfileRules.minutes(to) == nil {
                RowNote(L.t("Use the 24-hour form, for example 09:00."))
            }
        }
        .onAppear {
            name = profile.name
            app = profile.autoApp ?? ""
            from = profile.autoTimeFrom ?? ""
            to = profile.autoTimeTo ?? ""
        }
    }

    private func saveTime(weekdaysOnly: Bool) {
        store.setProfileTime(profile.id, from: from, to: to, weekdaysOnly: weekdaysOnly)
    }
}
