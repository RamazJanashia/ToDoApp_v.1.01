import SwiftUI
import TodoCore

/// A month grid of scheduled tasks. Click a day to select it, double-click to add a task,
/// drag tasks between days to reschedule.
struct CalendarMonthView: View {
    @EnvironmentObject private var state: AppState

    let tasks: [TaskItem]

    private let calendar = Calendar.current

    private var monthStart: Date {
        calendar.dateInterval(of: .month, for: state.calendarMonth)?.start ?? calendar.startOfDay(for: state.calendarMonth)
    }

    private var days: [Date] { MonthGrid.days(containing: monthStart, calendar: calendar) }

    private var weekdaySymbols: [String] {
        let symbols = calendar.shortWeekdaySymbols
        let first = calendar.firstWeekday - 1
        return Array(symbols[first...] + symbols[..<first])
    }

    private var tasksByDay: [Date: [TaskItem]] {
        Dictionary(grouping: tasks) { calendar.startOfDay(for: $0.dueDate ?? .distantPast) }
            .mapValues { dayTasks in
                dayTasks.sorted { lhs, rhs in
                    if lhs.isCompleted != rhs.isCompleted { return !lhs.isCompleted }
                    if lhs.hasDueTime != rhs.hasDueTime { return lhs.hasDueTime }
                    return (lhs.dueDate ?? .distantPast, lhs.sortOrder) < (rhs.dueDate ?? .distantPast, rhs.sortOrder)
                }
            }
    }

    var body: some View {
        let byDay = tasksByDay
        let grid = days

        VStack(spacing: 0) {
            HStack(spacing: 4) {
                Text(monthStart.formatted(.dateTime.month(.wide).year()))
                    .font(.system(size: 17, weight: .semibold))
                    .foregroundColor(Theme.primaryText)
                Spacer()
                IconButton(systemName: "chevron.left", size: 12, weight: .medium, help: "Previous Month") { shiftMonth(-1) }
                Button("Today") {
                    withAnimation(Theme.Motion.quick) {
                        state.calendarMonth = Date()
                        state.calendarSelectedDay = calendar.startOfDay(for: Date())
                    }
                }
                .buttonStyle(.plain)
                .font(.system(size: 12.5, weight: .medium))
                .foregroundColor(Theme.secondaryText)
                .padding(.horizontal, 6)
                IconButton(systemName: "chevron.right", size: 12, weight: .medium, help: "Next Month") { shiftMonth(1) }
            }
            .padding(.horizontal, 32)
            .padding(.vertical, 10)

            VStack(spacing: 0) {
                HStack(spacing: 0) {
                    ForEach(weekdaySymbols, id: \.self) { symbol in
                        Text(symbol.uppercased())
                            .font(.system(size: 10.5, weight: .medium))
                            .tracking(0.8)
                            .foregroundColor(Theme.tertiaryText)
                            .frame(maxWidth: .infinity, alignment: .leading)
                            .padding(.leading, 8)
                    }
                }
                .padding(.bottom, 8)

                Hairline()

                GeometryReader { proxy in
                    let rowHeight = proxy.size.height / 6
                    let isCompact = proxy.size.width / 7 < 100
                    VStack(spacing: 0) {
                        ForEach(0..<6, id: \.self) { week in
                            HStack(spacing: 0) {
                                ForEach(0..<7, id: \.self) { column in
                                    let day = grid[week * 7 + column]
                                    CalendarDayCell(
                                        day: day,
                                        tasks: byDay[day] ?? [],
                                        height: rowHeight,
                                        isCompact: isCompact,
                                        isInMonth: calendar.isDate(day, equalTo: monthStart, toGranularity: .month),
                                        isLastColumn: column == 6
                                    )
                                }
                            }
                            .frame(height: rowHeight)
                        }
                    }
                }
            }
            .padding(.horizontal, 32)
            .padding(.bottom, 20)
        }
    }

    private func shiftMonth(_ delta: Int) {
        withAnimation(Theme.Motion.quick) {
            state.calendarMonth = calendar.date(byAdding: .month, value: delta, to: monthStart) ?? state.calendarMonth
        }
    }
}

struct CalendarDayCell: View {
    @EnvironmentObject private var store: TaskStore
    @EnvironmentObject private var state: AppState

    let day: Date
    let tasks: [TaskItem]
    let height: CGFloat
    /// Narrow cells show coloured dots instead of titles that would truncate to "…".
    let isCompact: Bool
    let isInMonth: Bool
    let isLastColumn: Bool

    @State private var isDropTarget = false
    @State private var hovering = false

    /// Chips that fit under the day number (~36pt), each ~21pt tall; when they don't
    /// all fit, room is kept for the "+N more" line (~16pt).
    private var maxVisible: Int {
        let all = Int((height - 36) / 21)
        return tasks.count <= all ? all : max(Int((height - 52) / 21), 0)
    }

    private var isToday: Bool { Calendar.current.isDateInToday(day) }
    private var isSelected: Bool {
        state.calendarSelectedDay.map { Calendar.current.isDate($0, inSameDayAs: day) } ?? false
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 3) {
            Text(day.formatted(.dateTime.day()))
                .font(.system(size: 12, weight: isToday ? .semibold : .regular))
                .monospacedDigit()
                .foregroundColor(isToday ? Theme.background : (isInMonth ? Theme.primaryText.opacity(0.85) : Theme.tertiaryText.opacity(0.7)))
                .frame(minWidth: 22, minHeight: 22)
                .background(Circle().fill(isToday ? Theme.primaryText : .clear))
                .padding(.bottom, 2)

            if isCompact {
                compactMarkers
            } else {
                ForEach(tasks.prefix(maxVisible)) { task in
                    CalendarTaskChip(task: task)
                }
                if tasks.count > maxVisible {
                    Text("+\(tasks.count - maxVisible) more")
                        .font(.system(size: 10.5))
                        .foregroundColor(Theme.tertiaryText)
                        .lineLimit(1)
                        .padding(.leading, 5)
                }
            }
            Spacer(minLength: 0)
        }
        .padding(6)
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
        .clipped()
        .background(
            Rectangle().fill(
                isDropTarget ? Theme.blue.opacity(0.12)
                    : isSelected ? Theme.selection.opacity(0.6)
                    : hovering ? Theme.hover.opacity(0.6) : .clear
            )
        )
        .overlay(alignment: .bottom) { Hairline() }
        .overlay(alignment: .trailing) {
            if !isLastColumn { VerticalHairline() }
        }
        .contentShape(Rectangle())
        .onTapGesture(count: 2) {
            state.calendarSelectedDay = Calendar.current.startOfDay(for: day)
            state.createDraftTask(dueDate: day)
        }
        .onTapGesture {
            state.calendarSelectedDay = Calendar.current.startOfDay(for: day)
        }
        .onHover { hovering = $0 }
        .dropDestination(for: String.self) { items, _ in
            state.handleDrop(items, onDay: day)
        } isTargeted: { isDropTarget = $0 }
        .help(helpText)
    }

    private var helpText: String {
        let date = day.formatted(date: .complete, time: .omitted)
        guard isCompact, !tasks.isEmpty else { return date }
        return ([date] + tasks.map { "• " + ($0.title.isEmpty ? "Untitled" : $0.title) }).joined(separator: "\n")
    }

    /// Up to three dots (project or priority colour) and a count for the rest.
    @ViewBuilder
    private var compactMarkers: some View {
        if !tasks.isEmpty {
            HStack(spacing: 3) {
                ForEach(tasks.prefix(3)) { task in
                    Circle()
                        .fill(task.isCompleted ? Theme.green : (task.priority == .high ? Theme.red : (store.project(with: task.projectID)?.color.color ?? Theme.secondaryText)))
                        .frame(width: 6, height: 6)
                }
                if tasks.count > 3 {
                    Text("+\(tasks.count - 3)")
                        .font(.system(size: 10))
                        .foregroundColor(Theme.tertiaryText)
                        .fixedSize()
                }
            }
            .padding(.leading, 4)
        }
    }
}

struct CalendarTaskChip: View {
    @EnvironmentObject private var store: TaskStore
    @EnvironmentObject private var state: AppState

    let task: TaskItem

    @State private var hovering = false

    private var isSelected: Bool { state.selectedTaskID == task.id }

    private var dotColor: Color {
        if task.isCompleted { return Theme.green }
        if task.priority == .high { return Theme.red }
        return store.project(with: task.projectID)?.color.color ?? Theme.secondaryText
    }

    var body: some View {
        HStack(spacing: 5) {
            Circle().fill(dotColor).frame(width: 5, height: 5)
            Text(task.title.isEmpty ? "Untitled" : task.title)
                .font(.system(size: 11))
                .foregroundColor(task.isCompleted ? Theme.tertiaryText : Theme.primaryText.opacity(0.88))
                .strikethrough(task.isCompleted, color: Theme.tertiaryText)
                .lineLimit(1)
            Spacer(minLength: 0)
        }
        .padding(.horizontal, 5)
        .padding(.vertical, 2.5)
        .background(
            RoundedRectangle(cornerRadius: 4, style: .continuous)
                .fill(isSelected ? Theme.chipSelected : (hovering ? Theme.chipHover : Theme.chip))
        )
        .contentShape(Rectangle())
        .onTapGesture { state.handleClick(on: task.id) }
        .onHover { hovering = $0 }
        .draggable(task.id.uuidString) { TaskDragPreview(task: task) }
        .contextMenu { TaskContextMenu(task: task) }
    }
}
