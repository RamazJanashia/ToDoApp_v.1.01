import SwiftUI

/// TodoApp ▸ Settings… (⌘,)
struct SettingsView: View {
    @EnvironmentObject private var state: AppState

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 24) {
                VStack(alignment: .leading, spacing: 8) {
                    Picker("Appearance", selection: $state.appearance) {
                        ForEach(AppearancePreference.allCases) { option in
                            Text(option.title).tag(option)
                        }
                    }
                    .pickerStyle(.segmented)
                    Text("System follows the light or dark setting in macOS.")
                        .font(.system(size: 11))
                        .foregroundColor(.secondary)
                }

                ForEach(WorkspaceElement.Group.allCases) { group in
                    VStack(alignment: .leading, spacing: 8) {
                        Text(group.rawValue).font(.system(size: 13, weight: .semibold))
                        LazyVGrid(columns: [GridItem(.flexible(), alignment: .leading), GridItem(.flexible(), alignment: .leading)], alignment: .leading, spacing: 6) {
                            ForEach(group.elements) { element in
                                Toggle(element.title, isOn: Binding(
                                    get: { state.shows(element) },
                                    set: { state.setVisible($0, element) }
                                ))
                                .disabled(!state.canHide(element))
                            }
                        }
                    }
                }

                HStack {
                    Text("Hiding something removes it from view. Your tasks aren’t changed.")
                        .font(.system(size: 11))
                        .foregroundColor(.secondary)
                    Spacer()
                    Button("Show Everything") { state.showEverything() }
                        .disabled(state.hiddenElements.isEmpty)
                }

                Divider()

                VStack(alignment: .leading, spacing: 8) {
                    Text("Data").font(.system(size: 13, weight: .semibold))
                    Text("Tasks, daily backups and attachments all live in one folder. Copy it to back up everything.")
                        .font(.system(size: 11))
                        .foregroundColor(.secondary)
                        .fixedSize(horizontal: false, vertical: true)
                    Text(state.store.dataDirectory.path)
                        .font(.system(size: 11, design: .monospaced))
                        .textSelection(.enabled)
                        .fixedSize(horizontal: false, vertical: true)
                    Button("Show Data Folder") { state.showDataFolder() }
                }
            }
            .padding(24)
        }
        .frame(width: 480, height: 620)
    }
}
