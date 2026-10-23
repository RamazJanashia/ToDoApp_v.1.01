import SwiftUI
import TodoCore

struct CenterView: View {
    @EnvironmentObject private var store: TaskStore
    @EnvironmentObject private var state: AppState

    var body: some View {
        if state.isDashboard {
            DashboardView()
        } else {
            let tasks = state.visibleTasks
            VStack(alignment: .leading, spacing: 0) {
                CenterHeader(tasks: tasks, maxWidth: state.viewMode == .list ? Theme.Layout.readingWidth : .infinity)
                switch state.viewMode {
                case .list:
                    TaskListView(tasks: tasks)
                case .board:
                    KanbanBoardView(tasks: tasks)
                case .calendar:
                    CalendarMonthView(tasks: state.calendarTasks)
                }
            }
            .background(Theme.background)
        }
    }
}

/// Large title, date and count, the view switcher, and the progress line.
struct CenterHeader: View {
    @EnvironmentObject private var state: AppState

    let tasks: [TaskItem]
    /// Matches the content below, so the title lines up with the list.
    var maxWidth: CGFloat = .infinity

    private var doneCount: Int { tasks.filter(\.isCompleted).count }

    private var subtitle: String {
        let count = tasks.count
        let noun = count == 1 ? "task" : "tasks"
        if state.isSearching { return "\(count) \(count == 1 ? "result" : "results") for “\(state.searchText.trimmingCharacters(in: .whitespaces))”" }
        switch state.sidebarSelection {
        case .today:
            return "\(Date().formatted(.dateTime.weekday(.wide).month(.wide).day())) · \(count) \(noun)"
        case .upcoming:
            return "\(count) scheduled"
        case .completed:
            return "\(count) completed"
        default:
            return doneCount > 0 ? "\(count) \(noun) · \(doneCount) done today" : "\(count) \(noun)"
        }
    }

    private var showsProgress: Bool {
        guard !state.isSearching, !tasks.isEmpty, state.viewMode != .calendar else { return false }
        switch state.sidebarSelection {
        case .upcoming, .completed: return false
        default: return true
        }
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            HStack(alignment: .center, spacing: 4) {
                Text(state.title)
                    .font(Theme.Font.pageTitle)
                    .foregroundColor(Theme.primaryText)
                    .lineLimit(1)
                Spacer(minLength: 16)
                ViewModeSwitcher()
                    .padding(.trailing, 6)
                SortFilterMenu()
                IconButton(systemName: "plus", size: 17, weight: .light, help: "New Task (⌘N)") {
                    state.beginNewTask()
                }
            }
            Text(subtitle)
                .font(Theme.Font.body)
                .foregroundColor(Theme.secondaryText)
                .lineLimit(1)
                .padding(.top, 10)
            ProgressLine(fraction: tasks.isEmpty ? 0 : Double(doneCount) / Double(tasks.count))
                .padding(.top, 16)
                .opacity(showsProgress ? 1 : 0)
                .frame(height: showsProgress ? nil : 0)
        }
        .frame(maxWidth: maxWidth)
        .padding(.horizontal, Theme.Layout.pageMargin)
        .frame(maxWidth: .infinity)
        .padding(.top, 26)
        .padding(.bottom, showsProgress ? 10 : 4)
    }
}

/// List · Board · Calendar as three quiet symbols.
struct ViewModeSwitcher: View {
    @EnvironmentObject private var state: AppState

    var body: some View {
        HStack(spacing: 2) {
            ForEach(Array(ViewMode.allCases.enumerated()), id: \.element) { index, mode in
                IconButton(
                    systemName: mode.icon,
                    size: 13,
                    isActive: state.viewMode == mode,
                    help: "\(mode.title) (⌘\(index + 1))"
                ) {
                    withAnimation(Theme.Motion.quick) { state.viewMode = mode }
                }
            }
        }
        .padding(2)
        .background(RoundedRectangle(cornerRadius: 9, style: .continuous).fill(Theme.panel))
    }
}

struct SortFilterMenu: View {
    @EnvironmentObject private var state: AppState

    private var isFiltering: Bool { !state.priorityFilter.isEmpty || !state.showCompleted || state.sort != .manual }

    var body: some View {
        Menu {
            Picker("Sort By", selection: $state.sort) {
                ForEach(TaskSort.allCases) { sort in
                    Text(sort.title).tag(sort)
                }
            }
            .pickerStyle(.inline)

            Divider()

            Section("Priority") {
                ForEach(Priority.descending) { priority in
                    Toggle(priority.title, isOn: Binding(
                        get: { state.priorityFilter.contains(priority) },
                        set: { isOn in
                            if isOn { state.priorityFilter.insert(priority) } else { state.priorityFilter.remove(priority) }
                        }
                    ))
                }
            }

            Divider()

            Toggle("Show Completed", isOn: $state.showCompleted)

            if isFiltering {
                Divider()
                Button("Reset") {
                    state.priorityFilter = []
                    state.showCompleted = true
                    state.sort = .manual
                }
            }
        } label: {
            Image(systemName: isFiltering ? "line.3.horizontal.decrease.circle.fill" : "line.3.horizontal.decrease")
        }
        .menuStyle(.borderlessButton)
        .menuIndicator(.hidden)
        .fixedSize()
        .frame(width: 28, height: 28)
        .foregroundColor(Theme.secondaryText)
        .help("Sort and Filter")
    }
}
