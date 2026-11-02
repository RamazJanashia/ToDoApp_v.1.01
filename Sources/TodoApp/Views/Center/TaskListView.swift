import SwiftUI
import TodoCore

struct TaskListView: View {
    @EnvironmentObject private var state: AppState

    let tasks: [TaskItem]

    private var sections: [TaskSection] {
        TaskSection.make(for: state.scope, tasks: tasks)
    }

    private var canCompose: Bool {
        state.isSearching == false && state.sidebarSelection != .completed
    }

    var body: some View {
        let sections = sections
        ScrollViewReader { proxy in
            ScrollView {
                VStack(alignment: .leading, spacing: 0) {
                    sectionList(sections.filter { !$0.isCompletedHistory })

                    if tasks.isEmpty && !state.isComposing {
                        emptyState
                            .frame(maxWidth: .infinity)
                    }

                    if canCompose {
                        NewTaskComposer()
                            .id("composer")
                    }

                    sectionList(sections.filter(\.isCompletedHistory))
                }
                .frame(maxWidth: Theme.Layout.readingWidth)
                .padding(.horizontal, Theme.Layout.pageMargin)
                .frame(maxWidth: .infinity)
                .padding(.top, 6)
                .padding(.bottom, 48)
            }
            .scrollIndicators(.automatic)
            .onChange(of: state.isComposing) { composing in
                if composing { withAnimation(Theme.Motion.quick) { proxy.scrollTo("composer", anchor: .bottom) } }
            }
            .onChange(of: state.selectedTaskID) { id in
                if let id, tasks.contains(where: { $0.id == id }) { proxy.scrollTo(id) }
            }
        }
    }

    private func sectionList(_ sections: [TaskSection]) -> some View {
        LazyVStack(alignment: .leading, spacing: 0) {
            ForEach(sections) { section in
                if let title = section.title {
                    ListSectionHeader(title: title, detail: section.detail, isWarning: section.isWarning)
                }
                ForEach(section.tasks) { task in
                    TaskRowView(task: task, siblings: tasks)
                        .id(task.id)
                }
            }
        }
    }

    @ViewBuilder
    private var emptyState: some View {
        if state.isSearching {
            EmptyStateView(icon: "magnifyingglass", title: "No matching tasks", message: "Try a different word, or clear the filters.")
        } else {
            switch state.sidebarSelection {
            case .today:
                EmptyStateView(icon: "sun.max", title: "Nothing due today", message: "Enjoy the quiet, or plan something new.")
            case .upcoming:
                EmptyStateView(icon: "calendar", title: "Nothing scheduled", message: "Tasks with a future due date appear here.")
            case .completed:
                EmptyStateView(icon: "checkmark.circle", title: "No completed tasks yet")
            default:
                EmptyStateView(icon: "tray", title: "All clear", message: "Press ⌘N to add a task.")
            }
        }
    }
}

struct ListSectionHeader: View {
    let title: String
    var detail: String?
    var isWarning = false

    var body: some View {
        HStack(alignment: .firstTextBaseline, spacing: 8) {
            Text(title)
                .font(.system(size: 13, weight: .semibold))
                .foregroundColor(isWarning ? Theme.red : Theme.primaryText.opacity(0.85))
            if let detail {
                Text(detail)
                    .font(Theme.Font.caption)
                    .foregroundColor(Theme.tertiaryText)
            }
        }
        .padding(.top, 22)
        .padding(.bottom, 4)
    }
}

/// The "+ New task" line; becomes a text field and stays open for quick entry.
struct NewTaskComposer: View {
    @EnvironmentObject private var state: AppState

    @State private var title = ""
    @State private var hovering = false
    @FocusState private var focused: Bool

    var body: some View {
        Group {
            if state.isComposing {
                HStack(spacing: 14) {
                    Circle()
                        .stroke(Theme.tertiaryText, lineWidth: 1.2)
                        .frame(width: 20, height: 20)
                    TextField("New task", text: $title, axis: .vertical)
                        .lineLimit(1...)
                        .textFieldStyle(.plain)
                        .font(Theme.Font.rowTitle)
                        .foregroundColor(Theme.primaryText)
                        .focused($focused)
                        .onSubmit(commit)
                        .onExitCommand(perform: close)
                    Text("Return to add · Esc to close")
                        .font(Theme.Font.small)
                        .foregroundColor(Theme.tertiaryText)
                }
                .padding(.vertical, 15)
                .overlay(alignment: .bottom) { Hairline() }
                .onAppear {
                    DispatchQueue.main.async { focused = true }
                }
                .onChange(of: focused) { isFocused in
                    if !isFocused && title.trimmingCharacters(in: .whitespaces).isEmpty { close() }
                }
            } else {
                Button {
                    state.isComposing = true
                } label: {
                    HStack(spacing: 12) {
                        Image(systemName: "plus")
                            .font(.system(size: 14, weight: .regular))
                            .frame(width: 20)
                        Text("New task")
                            .font(Theme.Font.body)
                    }
                    .foregroundColor(hovering ? Theme.primaryText : Theme.secondaryText)
                    .padding(.vertical, 18)
                    .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
                .onHover { hovering = $0 }
            }
        }
        .animation(Theme.Motion.quick, value: state.isComposing)
    }

    private func commit() {
        let trimmed = title.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty else {
            close()
            return
        }
        let task = state.createTask(title: trimmed)
        state.selectTask(task.id)
        title = ""
        focused = true
    }

    private func close() {
        title = ""
        focused = false
        state.isComposing = false
    }
}
