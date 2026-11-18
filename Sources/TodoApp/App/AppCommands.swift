import SwiftUI
import TodoCore

/// Menu bar items and their keyboard shortcuts.
struct AppCommands: Commands {
    @ObservedObject var state: AppState
    @ObservedObject var store: TaskStore

    private var selected: TaskItem? { state.selectedTask }

    var body: some Commands {
        CommandGroup(replacing: .newItem) {
            Button("New Task") { state.beginNewTask() }
                .keyboardShortcut("n", modifiers: .command)
            Button("New Project…") { state.projectEditor = ProjectEditorRequest(projectID: nil) }
                .keyboardShortcut("n", modifiers: [.command, .shift])
            Divider()
            Button("Show Data Folder") { state.showDataFolder() }
        }

        CommandGroup(replacing: .textEditing) {
            Button("Search Tasks") { state.focusSearch() }
                .keyboardShortcut("f", modifiers: .command)
                .disabled(!state.shows(.toolbarSearch))
        }

        CommandMenu("Task") {
            Button(selected?.isCompleted == true ? "Mark as Incomplete" : "Complete") {
                state.toggleSelectedCompleted()
            }
            .keyboardShortcut(.return, modifiers: .command)
            .disabled(selected == nil)

            Button("Edit Title") {
                if let id = state.selectedTaskID { state.revealInInspector(id, focusTitle: true) }
            }
            .keyboardShortcut("e", modifiers: .command)
            .disabled(selected == nil)

            Button("Duplicate") { state.duplicateSelected() }
                .keyboardShortcut("d", modifiers: .command)
                .disabled(selected == nil)

            Divider()

            Menu("Priority") {
                ForEach(Array(Priority.descending.enumerated()), id: \.element) { index, priority in
                    Button(priority.title) {
                        if let id = state.selectedTaskID { state.setPriority(priority, for: [id]) }
                    }
                    // ⌃⌘, because ⌥⌘1–5 belong to the Go menu.
                    .keyboardShortcut(KeyEquivalent(Character(String(3 - index))), modifiers: [.command, .control])
                }
            }
            .disabled(selected == nil)

            Menu("Due Date") {
                Button("Today") { setDue(.today) }
                    .keyboardShortcut("t", modifiers: [.command, .option])
                Button("Tomorrow") { setDue(.tomorrow) }
                    .keyboardShortcut("t", modifiers: [.command, .option, .shift])
                Button("Next Week") { setDue(.nextWeek) }
                Divider()
                Button("Remove Due Date") {
                    if let id = state.selectedTaskID { state.setDueDay(nil, for: [id]) }
                }
            }
            .disabled(selected == nil)

            Divider()

            Button("Delete") { state.deleteSelectedFromMenu() }
                .keyboardShortcut(.delete, modifiers: .command)
                .disabled(selected == nil)
        }

        CommandGroup(before: .toolbar) {
            ForEach(Array(ViewMode.allCases.enumerated()), id: \.element) { index, mode in
                Button("as \(mode.title)") { state.viewMode = mode }
                    .keyboardShortcut(KeyEquivalent(Character(String(index + 1))), modifiers: .command)
                    .disabled(!state.shows(mode.element))
            }
            Divider()
            Button(state.isSidebarVisible ? "Hide Sidebar" : "Show Sidebar") {
                withAnimation(Theme.Motion.standard) { state.isSidebarVisible.toggle() }
            }
            .keyboardShortcut("s", modifiers: [.command, .control])
            Button(state.isInspectorVisible ? "Hide Inspector" : "Show Inspector") {
                withAnimation(Theme.Motion.standard) { state.isInspectorVisible.toggle() }
            }
            .keyboardShortcut("i", modifiers: [.command, .option])
            Divider()
            Picker("Appearance", selection: $state.appearance) {
                ForEach(AppearancePreference.allCases) { option in
                    Text(option.title).tag(option)
                }
            }
            Button("Customize…") { state.openSettings() }
            Divider()
        }

        CommandMenu("Go") {
            goButton("All Tasks", .allTasks, key: "1")
            goButton("Today", .today, key: "2")
            goButton("Upcoming", .upcoming, key: "3")
            goButton("Completed", .completed, key: "4")
            goButton("Dashboard", .dashboard, key: "5")
            if !store.projects.isEmpty, state.shows(.projects) {
                Divider()
                ForEach(store.projects.outline()) { row in
                    Button(String(repeating: "    ", count: row.depth) + row.project.name) {
                        state.select(.project(row.id))
                    }
                }
            }
        }
    }

    private func goButton(_ title: String, _ item: SidebarItem, key: Character) -> some View {
        Button(title) { state.select(item) }
            .keyboardShortcut(KeyEquivalent(key), modifiers: [.command, .option])
            .disabled(!state.shows(item.element))
    }

    private func setDue(_ due: QuickDue) {
        guard let id = state.selectedTaskID else { return }
        state.setDue(due, for: [id])
    }
}
