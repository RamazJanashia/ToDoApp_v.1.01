import SwiftUI
import TodoCore

/// To Do · In Progress · Done. Drag cards between columns to change their status.
struct KanbanBoardView: View {
    let tasks: [TaskItem]

    var body: some View {
        GeometryReader { proxy in
            let spacing: CGFloat = 14
            let horizontalPadding = Theme.Layout.pageMargin
            let available = proxy.size.width - horizontalPadding * 2 - spacing * 2
            let columnWidth = max(210, available / 3)
            // On narrow windows the board scrolls sideways; a soft fade on the right edge says so.
            let overflows = columnWidth * 3 + spacing * 2 + horizontalPadding * 2 > proxy.size.width + 1

            ScrollView(.horizontal) {
                HStack(alignment: .top, spacing: spacing) {
                    ForEach(TaskStatus.allCases) { status in
                        KanbanColumn(status: status, tasks: tasks.filter { $0.status == status })
                            .frame(width: columnWidth)
                    }
                }
                .padding(.horizontal, horizontalPadding)
                .padding(.top, 14)
                .padding(.bottom, 24)
                .frame(height: proxy.size.height, alignment: .top)
            }
            .scrollIndicators(overflows ? .visible : .automatic)
            .overlay(alignment: .trailing) {
                if overflows {
                    LinearGradient(colors: [Theme.background.opacity(0), Theme.background], startPoint: .leading, endPoint: .trailing)
                        .frame(width: 40)
                        .allowsHitTesting(false)
                }
            }
        }
    }
}

struct KanbanColumn: View {
    @EnvironmentObject private var state: AppState

    let status: TaskStatus
    let tasks: [TaskItem]

    @State private var isDropTarget = false
    @State private var hovering = false

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            HStack(spacing: 8) {
                Image(systemName: status.icon)
                    .font(.system(size: 12))
                    .foregroundColor(status == .done ? Theme.green : Theme.secondaryText)
                Text(status.title)
                    .font(.system(size: 13, weight: .semibold))
                    .foregroundColor(Theme.primaryText.opacity(0.88))
                Text("\(tasks.count)")
                    .font(Theme.Font.caption)
                    .monospacedDigit()
                    .foregroundColor(Theme.tertiaryText)
                Spacer()
                IconButton(systemName: "plus", size: 12, weight: .medium, help: "Add to \(status.title)") {
                    state.createDraftTask(status: status)
                }
                .opacity(hovering ? 1 : 0)
            }
            .padding(.leading, 14)
            .padding(.trailing, 8)
            .frame(height: 44)

            ScrollView {
                LazyVStack(spacing: 8) {
                    ForEach(tasks) { task in
                        KanbanCard(task: task, columnTasks: tasks)
                    }
                    if tasks.isEmpty {
                        Text(status == .done ? "Drop finished work here" : "No tasks")
                            .font(Theme.Font.caption)
                            .foregroundColor(Theme.tertiaryText)
                            .frame(maxWidth: .infinity)
                            .padding(.vertical, 28)
                    }
                }
                .padding(.horizontal, 8)
                .padding(.bottom, 10)
            }
            .scrollIndicators(.never)
        }
        .frame(maxHeight: .infinity, alignment: .top)
        .background(RoundedRectangle(cornerRadius: 12, style: .continuous).fill(Theme.panel))
        .overlay(
            RoundedRectangle(cornerRadius: 12, style: .continuous)
                .stroke(isDropTarget ? Theme.blue.opacity(0.6) : Theme.separator, lineWidth: 1)
        )
        .onHover { hovering = $0 }
        .dropDestination(for: String.self) { items, _ in
            withAnimation(Theme.Motion.standard) { state.handleDrop(items, status: status) }
        } isTargeted: { isDropTarget = $0 }
    }
}

struct KanbanCard: View {
    @EnvironmentObject private var store: TaskStore
    @EnvironmentObject private var state: AppState

    let task: TaskItem
    let columnTasks: [TaskItem]

    @State private var hovering = false
    @State private var isDropTarget = false

    private var isSelected: Bool { state.selectedTaskID == task.id }

    var body: some View {
        VStack(alignment: .leading, spacing: 9) {
            HStack(alignment: .top, spacing: 10) {
                CheckCircle(isDone: task.isCompleted, size: 16) {
                    withAnimation(Theme.Motion.standard) { state.toggleCompleted(task.id) }
                }
                .padding(.top, 1)
                Text(task.title.isEmpty ? "Untitled" : task.title)
                    .font(.system(size: 13.5))
                    .foregroundColor(task.isCompleted || task.title.isEmpty ? Theme.tertiaryText : Theme.primaryText)
                    .strikethrough(task.isCompleted, color: Theme.tertiaryText)
                    .lineLimit(3)
                    .frame(maxWidth: .infinity, alignment: .leading)
                if hovering {
                    DetailsButton(taskID: task.id, isVisible: true)
                        .frame(width: 20, height: 18)
                } else {
                    PriorityFlag(priority: task.priority, dimmed: task.isCompleted)
                }
            }
            TaskMetaLine(task: task)
                .padding(.leading, 26)
        }
        .padding(12)
        .background(
            RoundedRectangle(cornerRadius: 10, style: .continuous)
                .fill(isSelected ? Theme.raisedSelected : (hovering ? Theme.raisedHover : Theme.raised))
        )
        .overlay(
            RoundedRectangle(cornerRadius: 10, style: .continuous)
                .stroke(isSelected ? Theme.selectedOutline : Theme.separator, lineWidth: 1)
        )
        .overlay(alignment: .top) {
            if isDropTarget {
                Capsule().fill(Theme.blue).frame(height: 2).offset(y: -5)
            }
        }
        .contentShape(Rectangle())
        .onTapGesture { state.handleClick(on: task.id) }
        .onHover { hovering = $0 }
        .draggable(task.id.uuidString) { TaskDragPreview(task: task) }
        .dropDestination(for: String.self) { items, _ in
            withAnimation(Theme.Motion.standard) {
                state.handleDrop(items, status: task.status, before: task.id, in: columnTasks)
            }
        } isTargeted: { isDropTarget = $0 }
        .contextMenu { TaskContextMenu(task: task) }
    }
}
