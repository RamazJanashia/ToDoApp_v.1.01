import SwiftUI
import TodoCore

@main
struct TodoApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) private var appDelegate
    @StateObject private var store: TaskStore
    @StateObject private var state: AppState

    init() {
        // TODOAPP_DATA_DIR points the app at another data folder (handy for trying things out).
        // Reminders stay off there, so trying things out can't disturb the real ones.
        let trial = ProcessInfo.processInfo.environment["TODOAPP_DATA_DIR"]
            .map { URL(fileURLWithPath: $0, isDirectory: true) }
        let store = TaskStore(directory: trial ?? DataFileStore.defaultDirectory, remindersEnabled: trial == nil)
        _store = StateObject(wrappedValue: store)
        _state = StateObject(wrappedValue: AppState(store: store))
    }

    var body: some Scene {
        WindowGroup("Tasks") {
            RootView()
                .environmentObject(store)
                .environmentObject(state)
        }
        .defaultSize(width: 1320, height: 820)
        .windowToolbarStyle(.unified(showsTitle: false))
        .commands {
            AppCommands(state: state, store: store)
        }

        Settings {
            SettingsView()
                .environmentObject(state)
        }
    }
}

final class AppDelegate: NSObject, NSApplicationDelegate {
    /// One window, so closing it means leaving. Otherwise menu commands would keep acting on
    /// tasks nobody can see, and clicking a reminder would open nothing.
    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { true }

    func applicationWillFinishLaunching(_ notification: Notification) {
        NSWindow.allowsAutomaticWindowTabbing = false
    }

    func applicationDidFinishLaunching(_ notification: Notification) {
        // Lets the bare binary behave like an app too (Dock icon, focus) when run outside a bundle.
        NSApp.setActivationPolicy(.regular)
        NSApp.activate(ignoringOtherApps: true)
    }
}
