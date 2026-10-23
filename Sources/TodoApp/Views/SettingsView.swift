import SwiftUI

/// TodoApp ▸ Settings… (⌘,)
struct SettingsView: View {
    @EnvironmentObject private var state: AppState

    var body: some View {
        Form {
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
        .padding(24)
        .frame(width: 380)
    }
}
