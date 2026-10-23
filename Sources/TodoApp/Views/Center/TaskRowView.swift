import SwiftUI
import TodoCore

struct TaskRowView: View {
    @EnvironmentObject private var store: TaskStore
    @EnvironmentObject private var state: AppState

    let task: TaskItem
    /// The list as displayed, used to place a dropped task.
    let siblings: [TaskItem]

    @State private var hovering = false
    @State private var isDropTarget = false

    private var isSelected: Bool { state.selectedTaskID == task.id }

    var body: some View {
        HStack(alignment: .center, spacing: 14) {
            CheckCircle(isDone: task.isCompleted) {
                withAnimation(Theme.Motion.standard) { state.toggleCompleted(task.id) }
            }

            VStack(alignment: .leading, spacing: 4) {
                Text(task.title.isEmpty ? "Untitled" : task.title)
                    .font(Theme.Font.rowTitle)
                    .foregroundColor(task.isCompleted || task.title.isEmpty ? Theme.tertiaryText : Theme.primaryText)
                    .strikethrough(task.isCompleted, color: Theme.tertiaryText)
                    .lineLimit(1)
                TaskMetaLine(task: task, hidesDueToday: state.scope == .today)
            }

            Spacer(minLength: 12)

            DetailsButton(taskID: task.id, isVisible: hovering)
            PriorityFlag(priority: task.priority, dimmed: task.isCompleted)
        }
        .padding(.vertical, 14)
        .background(
            RoundedRectangle(cornerRadius: 8, style: .continuous)
                .fill(isSelected ? Theme.selection.opacity(0.75) : (hovering ? Theme.hover : .clear))
                .padding(.horizontal, -12)
                .padding(.vertical, 2)
        )
        .overlay(alignment: .bottom) { Hairline() }
        .overlay(alignment: .top) {
            if isDropTarget {
                Capsule().fill(Theme.blue).frame(height: 2).offset(y: -1)
            }
        }
        .contentShape(Rectangle())
        .onTapGesture { state.handleClick(on: task.id) }
        .onHover { hovering = $0 }
        .draggable(task.id.uuidString) {
            TaskDragPreview(task: task)
        }
        .dropDestination(for: String.self) { items, _ in
            state.handleDrop(items, before: task.id, in: siblings)
        } isTargeted: { isDropTarget = $0 }
        .contextMenu { TaskContextMenu(task: task) }
        .animation(Theme.Motion.quick, value: isSelected)
    }
}

/// Project · due date · repeat · subtasks · attachments, in quiet grey.
struct TaskMetaLine: View {
    @EnvironmentObject private var store: TaskStore

    let task: TaskItem
    var hidesDueToday = false

    private var dueLabel: (text: String, overdue: Bool)? {
        guard let due = task.dueDate else { return nil }
        let isToday = Calendar.current.isDateInToday(due)
        if hidesDueToday && isToday {
            return task.hasDueTime ? (due.formatted(date: .omitted, time: .shortened), task.isOverdue()) : nil
        }
        return (DueDateText.label(for: due, hasTime: task.hasDueTime), task.isOverdue())
    }

    var body: some View {
        let project = store.project(with: task.projectID)
        let due = dueLabel
        let hasAnything = project != nil || due != nil || task.recurrence != nil
            || !task.subtasks.isEmpty || !task.attachments.isEmpty || !task.notes.isEmpty

        if hasAnything {
            // When space is tight, drop the project name, then the small icons, rather than
            // truncating the due date into "Ye…".
            ViewThatFits(in: .horizontal) {
                line(project: project, due: due, detail: .full)
                line(project: nil, due: due, detail: .full)
                line(project: nil, due: due, detail: .dueOnly)
            }
            .labelStyle(CompactLabelStyle())
            .font(Theme.Font.caption)
            .foregroundColor(Theme.secondaryText)
            .lineLimit(1)
        }
    }

    private enum Detail { case full, dueOnly }

    private func line(project: Project?, due: (text: String, overdue: Bool)?, detail: Detail) -> some View {
        HStack(spacing: 10) {
            if let project {
                Text(project.name).lineLimit(1)
            }
            if let due {
                Label(due.text, systemImage: "calendar")
                    .foregroundColor(due.overdue ? Theme.red.opacity(0.9) : Theme.secondaryText)
            }
            if detail == .full || due == nil {
                if task.recurrence != nil {
                    Image(systemName: "repeat").help(task.recurrence?.summary ?? "")
                }
                if task.reminderOffset != nil, task.dueDate != nil {
                    Image(systemName: "bell")
                }
                if !task.subtasks.isEmpty {
                    Label("\(task.completedSubtaskCount)/\(task.subtasks.count)", systemImage: "checklist")
                }
                if !task.attachments.isEmpty {
                    Label("\(task.attachments.count)", systemImage: "paperclip")
                }
                if !task.notes.isEmpty && project == nil && due == nil {
                    Image(systemName: "text.alignleft")
                }
            }
        }
    }
}

struct CompactLabelStyle: LabelStyle {
    func makeBody(configuration: Configuration) -> some View {
        HStack(spacing: 4) {
            configuration.icon.font(.system(size: 10.5))
            configuration.title
        }
    }
}

struct TaskDragPreview: View {
    let task: TaskItem

    var body: some View {
        HStack(spacing: 8) {
            Circle().fill(Theme.checkboxFill).frame(width: 12, height: 12)
            Text(task.title.isEmpty ? "Untitled" : task.title)
                .font(.system(size: 13))
                .foregroundColor(Theme.primaryText)
                .lineLimit(1)
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 8)
        .background(RoundedRectangle(cornerRadius: 8, style: .continuous).fill(Theme.raised))
    }
}

/// Right-click actions shared by list rows, board cards and calendar chips.
struct TaskContextMenu: View {
    @EnvironmentObject private var store: TaskStore
    @EnvironmentObject private var state: AppState

    let task: TaskItem

    var body: some View {
        Button("Show Details") {
            withAnimation(Theme.Motion.standard) { state.revealInInspector(task.id, focusTitle: false) }
        }
        Button(task.isCompleted ? "Mark as Incomplete" : "Complete") {
            state.toggleCompleted(task.id)
        }

        Divider()

        Menu("Due Date") {
            Button("Today") { setDue(0) }
            Button("Tomorrow") { setDue(1) }
            Button("Next Week") { setDue(7) }
            if task.dueDate != nil {
                Divider()
                Button("Remove Due Date") { state.setDueDay(nil, for: [task.id]) }
            }
        }

        Menu("Priority") {
            ForEach(Priority.descending) { priority in
                Button {
                    state.setPriority(priority, for: [task.id])
                } label: {
                    if task.priority == priority {
                        Label(priority.title, systemImage: "checkmark")
                    } else {
                        Text(priority.title)
                    }
                }
            }
        }

        Menu("Move to") {
            Button("Inbox") { state.move([task.id], toProject: nil) }
            if !store.projects.isEmpty { Divider() }
            ForEach(store.projects) { project in
                Button(project.name) { state.move([task.id], toProject: project.id) }
            }
        }

        Menu("Status") {
            ForEach(TaskStatus.allCases) { status in
                Button(status.title) { store.setStatus([task.id], to: status) }
            }
        }

        Divider()

        Button("Duplicate") {
            if let copy = store.duplicateTask(task.id) { state.selectTask(copy.id) }
        }
        Button("Delete", role: .destructive) {
            state.delete([task.id])
        }
    }

    private func setDue(_ offset: Int) {
        let today = Calendar.current.startOfDay(for: Date())
        state.setDueDay(Calendar.current.date(byAdding: .day, value: offset, to: today), for: [task.id])
    }
}
