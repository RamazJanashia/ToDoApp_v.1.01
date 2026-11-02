import SwiftUI
import TodoCore

/// The right-hand "Task details" panel. Every edit applies and saves immediately.
struct InspectorView: View {
    @EnvironmentObject private var state: AppState

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            HStack {
                Text("Task details")
                    .font(.system(size: 14, weight: .medium))
                    .foregroundColor(Theme.primaryText)
                Spacer()
                IconButton(systemName: "xmark", size: 13, help: "Close Inspector (⌥⌘I)") {
                    withAnimation(Theme.Motion.standard) { state.isInspectorVisible = false }
                }
            }
            .padding(.leading, 22)
            .padding(.trailing, 14)
            .padding(.top, 22)
            .padding(.bottom, 10)

            if let task = state.selectedTask {
                TaskDetailForm(taskID: task.id)
                    .id(task.id)
            } else {
                Spacer()
                EmptyStateView(icon: "sidebar.right", title: "No task selected", message: "Click a task to see and edit its details here.")
                    .frame(maxWidth: .infinity)
                Spacer()
                Spacer()
            }
        }
        .frame(maxHeight: .infinity, alignment: .top)
        .background(Theme.background)
    }
}

struct TaskDetailForm: View {
    @EnvironmentObject private var store: TaskStore
    @EnvironmentObject private var state: AppState

    let taskID: UUID

    @FocusState private var titleFocused: Bool
    @State private var showDue = false
    @State private var showReminder = false
    @State private var showPriority = false
    @State private var showProject = false
    @State private var showRepeat = false
    @State private var showStatus = false

    private var task: TaskItem { store.task(with: taskID) ?? TaskItem(title: "") }

    private func binding<Value>(_ keyPath: WritableKeyPath<TaskItem, Value>) -> Binding<Value> {
        Binding(
            get: { (store.task(with: taskID) ?? TaskItem(title: ""))[keyPath: keyPath] },
            set: { newValue in store.updateTask(taskID) { $0[keyPath: keyPath] = newValue } }
        )
    }

    var body: some View {
        let task = task
        let project = store.project(with: task.projectID)

        ScrollView {
            VStack(alignment: .leading, spacing: 0) {
                HStack(alignment: .firstTextBaseline, spacing: 12) {
                    CheckCircle(isDone: task.isCompleted, size: 17) {
                        withAnimation(Theme.Motion.standard) { store.toggleCompleted(taskID) }
                    }
                    .alignmentGuide(.firstTextBaseline) { $0[VerticalAlignment.center] + 5 }
                    TextField("Task name", text: binding(\.title), axis: .vertical)
                        .textFieldStyle(.plain)
                        .font(.system(size: 17, weight: .semibold))
                        .foregroundColor(task.isCompleted ? Theme.secondaryText : Theme.primaryText)
                        .lineLimit(1...)
                        .focused($titleFocused)
                }
                .padding(.top, 10)
                .padding(.bottom, 22)

                VStack(alignment: .leading, spacing: 2) {
                    if state.shows(.detailDueDate) {
                        PropertyRow(
                            icon: "calendar",
                            iconColor: task.isOverdue() ? Theme.red : Theme.secondaryText,
                            title: task.dueDate.map { DueDateText.label(for: $0, hasTime: task.hasDueTime) } ?? "Add due date",
                            isPlaceholder: task.dueDate == nil,
                            isPresented: $showDue
                        ) {
                            DueDatePopover(taskID: taskID)
                        }
                    }

                    if state.shows(.detailReminder) {
                        PropertyRow(
                            icon: task.reminderOffset == nil ? "bell" : "bell.fill",
                            title: reminderTitle(task),
                            isPlaceholder: task.reminderOffset == nil || task.dueDate == nil,
                            isPresented: $showReminder
                        ) {
                            ReminderPopover(taskID: taskID) { showReminder = false }
                        }
                    }

                    if state.shows(.detailPriority) {
                        PropertyRow(
                            icon: "flag",
                            iconColor: task.priority == .none ? Theme.secondaryText : task.priority.tint,
                            title: task.priority == .none ? "Add priority" : task.priority.title,
                            isPlaceholder: task.priority == .none,
                            isPresented: $showPriority
                        ) {
                            PopoverSurface(width: 200) {
                                ForEach(Priority.descending) { priority in
                                    OptionRow(
                                        icon: "flag",
                                        iconColor: priority.tint,
                                        title: priority.title,
                                        isSelected: task.priority == priority,
                                        shortcut: "⌃⌘\(priority.rawValue)"
                                    ) {
                                        store.updateTask(taskID, undoName: "Set Priority") { $0.priority = priority }
                                        showPriority = false
                                    }
                                }
                            }
                        }
                    }

                    if state.shows(.detailProject) {
                        PropertyRow(
                            icon: project?.icon ?? "tray",
                            iconColor: project?.color.color ?? Theme.secondaryText,
                            title: project?.name ?? "No Project",
                            isPresented: $showProject
                        ) {
                            PopoverSurface(width: 220) {
                                OptionRow(icon: "tray", title: "No Project", isSelected: task.projectID == nil) {
                                    store.updateTask(taskID, undoName: "Move to Project") { $0.projectID = nil }
                                    showProject = false
                                }
                                ForEach(store.projects.outline()) { row in
                                    let option = row.project
                                    OptionRow(icon: option.icon, iconColor: option.color.color, title: option.name, isSelected: task.projectID == option.id) {
                                        store.updateTask(taskID, undoName: "Move to Project") { $0.projectID = option.id }
                                        showProject = false
                                    }
                                    .padding(.leading, CGFloat(row.depth) * 14)
                                }
                                Hairline().padding(.vertical, 4)
                                OptionRow(icon: "plus", title: "New Project…") {
                                    showProject = false
                                    state.projectEditor = ProjectEditorRequest(projectID: nil)
                                }
                            }
                        }
                    }

                    if state.shows(.detailRepeat) {
                        PropertyRow(
                            icon: "repeat",
                            title: task.recurrence?.summary ?? "Doesn’t repeat",
                            isPlaceholder: task.recurrence == nil,
                            isPresented: $showRepeat
                        ) {
                            RepeatPopover(taskID: taskID)
                        }
                    }

                    if state.shows(.detailStatus) {
                        PropertyRow(
                            icon: task.status.icon,
                            iconColor: task.status == .done ? Theme.green : Theme.secondaryText,
                            title: task.status.title,
                            isPresented: $showStatus
                        ) {
                            PopoverSurface(width: 200) {
                                ForEach(TaskStatus.allCases) { status in
                                    OptionRow(icon: status.icon, title: status.title, isSelected: task.status == status) {
                                        store.setStatus([taskID], to: status)
                                        showStatus = false
                                    }
                                }
                            }
                        }
                    }
                }
                .padding(.horizontal, -8)

                if state.shows(.detailNotes) {
                    SectionHeader("Notes")
                        .padding(.top, 30)
                        .padding(.bottom, 10)
                    TextField("Add notes", text: binding(\.notes), axis: .vertical)
                        .textFieldStyle(.plain)
                        .font(Theme.Font.body)
                        .foregroundColor(Theme.primaryText.opacity(0.85))
                        .lineSpacing(3)
                        .lineLimit(2...)
                }

                if state.shows(.detailSubtasks) {
                    SubtasksSection(taskID: taskID, subtasks: task.subtasks, progress: task.progress)
                }
                if state.shows(.detailAttachments) {
                    AttachmentsSection(taskID: taskID, attachments: task.attachments)
                }

                Hairline()
                    .padding(.top, 30)
                HStack(spacing: 2) {
                    Text("Created \(task.createdAt.formatted(.dateTime.month(.abbreviated).day().year()))")
                        .font(Theme.Font.small)
                        .foregroundColor(Theme.tertiaryText)
                    Spacer()
                    IconButton(systemName: "plus.square.on.square", size: 13, help: "Duplicate (⌘D)") {
                        if let copy = store.duplicateTask(taskID) { state.selectTask(copy.id) }
                    }
                    IconButton(systemName: "trash", size: 13, help: "Delete (⌘⌫)") {
                        state.delete([taskID])
                    }
                }
                .padding(.top, 10)
            }
            .padding(.leading, 22)
            .padding(.trailing, 18)
            .padding(.bottom, 24)
        }
        .scrollIndicators(.never)
        .onAppear(perform: consumeFocusRequest)
        .onChange(of: state.titleFocusRequest) { _ in consumeFocusRequest() }
    }

    private func reminderTitle(_ task: TaskItem) -> String {
        guard task.dueDate != nil else { return "Add reminder" }
        guard let offset = task.reminderOffset else { return "Add reminder" }
        return ReminderPreset.title(for: offset)
    }

    private func consumeFocusRequest() {
        guard state.titleFocusRequest == taskID else { return }
        state.titleFocusRequest = nil
        DispatchQueue.main.async { titleFocused = true }
    }
}

/// Icon + value; clicking opens a popover to change it.
struct PropertyRow<Popover: View>: View {
    let icon: String
    var iconColor: Color = Theme.secondaryText
    let title: String
    var isPlaceholder = false
    @Binding var isPresented: Bool
    @ViewBuilder let popover: () -> Popover

    @State private var hovering = false

    var body: some View {
        Button {
            isPresented.toggle()
        } label: {
            HStack(spacing: 13) {
                Image(systemName: icon)
                    .font(.system(size: 14))
                    .foregroundColor(iconColor)
                    .frame(width: 18)
                Text(title)
                    .font(Theme.Font.body)
                    .foregroundColor(isPlaceholder ? Theme.tertiaryText : Theme.primaryText)
                    .lineLimit(1)
                Spacer(minLength: 0)
            }
            .padding(.horizontal, 8)
            .frame(height: 38)
            .rowHighlight(selected: isPresented, hovering: hovering, cornerRadius: 7)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .onHover { hovering = $0 }
        .popover(isPresented: $isPresented, arrowEdge: .leading) {
            popover()
        }
    }
}
