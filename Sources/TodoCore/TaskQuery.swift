import Foundation

/// Which slice of the workspace a list shows.
public enum TaskScope: Hashable, Sendable {
    /// Open tasks from everywhere, plus what was finished today.
    case allOpen
    case today
    case upcoming
    case completed
    case all
    /// A project together with its sub-projects.
    case projects(Set<UUID>)

    /// One project on its own.
    public static func project(_ id: UUID) -> TaskScope { .projects([id]) }
}

public enum TaskSort: String, CaseIterable, Identifiable, Codable, Sendable {
    case manual
    case dueDate
    case priority
    case title
    case created

    public var id: String { rawValue }

    public var title: String {
        switch self {
        case .manual: return "Manual"
        case .dueDate: return "Due Date"
        case .priority: return "Priority"
        case .title: return "Title"
        case .created: return "Date Created"
        }
    }
}

public struct TaskQuery: Hashable, Sendable {
    public var scope: TaskScope
    public var searchText: String
    /// Empty means every priority.
    public var priorities: Set<Priority>
    /// Whether tasks finished today stay visible (struck through) in active lists.
    public var showCompleted: Bool
    public var sort: TaskSort
    /// In a project scope, also bring in tasks finished on earlier days.
    public var includesCompletedHistory: Bool

    public init(
        scope: TaskScope,
        searchText: String = "",
        priorities: Set<Priority> = [],
        showCompleted: Bool = true,
        sort: TaskSort = .manual,
        includesCompletedHistory: Bool = false
    ) {
        self.scope = scope
        self.searchText = searchText
        self.priorities = priorities
        self.showCompleted = showCompleted
        self.sort = sort
        self.includesCompletedHistory = includesCompletedHistory
    }

    public func run(on tasks: [TaskItem], projects: [Project], now: Date = Date(), calendar: Calendar = .current) -> [TaskItem] {
        let names = Dictionary(projects.map { ($0.id, $0.name) }, uniquingKeysWith: { first, _ in first })
        let tokens = searchText
            .split(whereSeparator: \.isWhitespace)
            .map(String.init)

        let bounds = DayBounds(now: now, calendar: calendar)
        let matching = tasks.filter { task in
            guard Self.belongs(task, to: scope, bounds: bounds) || isCompletedHistory(task) else { return false }
            if task.isCompleted && !showCompleted && scope != .completed { return false }
            if !priorities.isEmpty && !priorities.contains(task.priority) { return false }
            if !tokens.isEmpty {
                let projectName = task.projectID.flatMap { names[$0] }
                return tokens.allSatisfy { Self.task(task, projectName: projectName, contains: $0) }
            }
            return true
        }

        if scope == .completed {
            return matching.sorted { ($0.completedAt ?? .distantPast) > ($1.completedAt ?? .distantPast) }
        }
        return matching.sorted { lhs, rhs in
            if lhs.isCompleted != rhs.isCompleted { return !lhs.isCompleted }
            return Self.precedes(lhs, rhs, by: sort)
        }
    }

    private func isCompletedHistory(_ task: TaskItem) -> Bool {
        guard includesCompletedHistory, task.isCompleted, case .projects(let ids) = scope, let projectID = task.projectID else { return false }
        return ids.contains(projectID)
    }

    /// Incomplete tasks in a scope, for sidebar counters.
    public static func openCount(in scope: TaskScope, tasks: [TaskItem], now: Date = Date(), calendar: Calendar = .current) -> Int {
        let bounds = DayBounds(now: now, calendar: calendar)
        return tasks.reduce(0) { count, task in
            count + (!task.isCompleted && belongs(task, to: scope, bounds: bounds) ? 1 : 0)
        }
    }

    public static func belongs(_ task: TaskItem, to scope: TaskScope, now: Date, calendar: Calendar) -> Bool {
        belongs(task, to: scope, bounds: DayBounds(now: now, calendar: calendar))
    }

    /// Worked out once per pass: calendar arithmetic is the expensive part of running over many tasks.
    private struct DayBounds {
        let startOfToday: Date
        let startOfTomorrow: Date

        init(now: Date, calendar: Calendar) {
            startOfToday = calendar.startOfDay(for: now)
            startOfTomorrow = calendar.date(byAdding: .day, value: 1, to: startOfToday) ?? now
        }
    }

    private static func belongs(_ task: TaskItem, to scope: TaskScope, bounds: DayBounds) -> Bool {
        let startOfTomorrow = bounds.startOfTomorrow
        let finishedToday = task.completedAt.map { $0 >= bounds.startOfToday && $0 < startOfTomorrow } ?? false

        switch scope {
        case .allOpen:
            return !task.isCompleted || finishedToday
        case .today:
            guard let due = task.dueDate, due < startOfTomorrow else { return false }
            return !task.isCompleted || finishedToday
        case .upcoming:
            guard let due = task.dueDate else { return false }
            return due >= startOfTomorrow && !task.isCompleted
        case .completed:
            return task.isCompleted
        case .all:
            return true
        case .projects(let ids):
            guard let projectID = task.projectID, ids.contains(projectID) else { return false }
            return !task.isCompleted || finishedToday
        }
    }

    static func task(_ task: TaskItem, projectName: String?, contains token: String) -> Bool {
        if task.title.localizedStandardContains(token) { return true }
        if task.notes.localizedStandardContains(token) { return true }
        if let projectName, projectName.localizedStandardContains(token) { return true }
        if task.subtasks.contains(where: { $0.title.localizedStandardContains(token) }) { return true }
        return task.attachments.contains(where: { $0.name.localizedStandardContains(token) })
    }

    static func precedes(_ lhs: TaskItem, _ rhs: TaskItem, by sort: TaskSort) -> Bool {
        switch sort {
        case .manual:
            break
        case .dueDate:
            switch (lhs.dueDate, rhs.dueDate) {
            case let (l?, r?) where l != r: return l < r
            case (.some, .none): return true
            case (.none, .some): return false
            default: break
            }
        case .priority:
            if lhs.priority != rhs.priority { return lhs.priority > rhs.priority }
            switch (lhs.dueDate, rhs.dueDate) {
            case let (l?, r?) where l != r: return l < r
            case (.some, .none): return true
            case (.none, .some): return false
            default: break
            }
        case .title:
            let order = lhs.title.localizedStandardCompare(rhs.title)
            if order != .orderedSame { return order == .orderedAscending }
        case .created:
            if lhs.createdAt != rhs.createdAt { return lhs.createdAt > rhs.createdAt }
        }
        if lhs.sortOrder != rhs.sortOrder { return lhs.sortOrder < rhs.sortOrder }
        return lhs.createdAt < rhs.createdAt
    }
}

/// A titled run of tasks in a list, e.g. "Overdue" or one day in Upcoming.
public struct TaskSection: Identifiable, Hashable, Sendable {
    public var id: String
    /// `nil` for the single untitled section of a flat list.
    public var title: String?
    public var detail: String?
    public var isWarning: Bool
    public var tasks: [TaskItem]
    /// One day's finished tasks below a project's open ones.
    public var isCompletedHistory: Bool

    public init(id: String, title: String? = nil, detail: String? = nil, isWarning: Bool = false, isCompletedHistory: Bool = false, tasks: [TaskItem]) {
        self.id = id
        self.title = title
        self.detail = detail
        self.isWarning = isWarning
        self.isCompletedHistory = isCompletedHistory
        self.tasks = tasks
    }

    /// Splits an already sorted list into sections appropriate for the scope.
    public static func make(for scope: TaskScope, tasks: [TaskItem], now: Date = Date(), calendar: Calendar = .current) -> [TaskSection] {
        switch scope {
        case .today:
            let overdue = tasks.filter { $0.isOverdue(now: now, calendar: calendar) && !$0.isDue(onSameDayAs: now, calendar: calendar) }
            guard !overdue.isEmpty else { return [TaskSection(id: "all", tasks: tasks)] }
            let overdueIDs = Set(overdue.map(\.id))
            let rest = tasks.filter { !overdueIDs.contains($0.id) }
            var sections = [TaskSection(id: "overdue", title: "Overdue", isWarning: true, tasks: overdue)]
            if !rest.isEmpty {
                sections.append(TaskSection(id: "today", title: "Today", detail: now.formatted(.dateTime.weekday(.wide).month(.wide).day()), tasks: rest))
            }
            return sections

        case .upcoming:
            return grouped(tasks, by: { $0.dueDate }, ascending: true, now: now, calendar: calendar)

        case .completed:
            return grouped(tasks, by: { $0.completedAt }, ascending: false, now: now, calendar: calendar)

        case .projects:
            let open = tasks.filter { !$0.isCompleted }
            let finished = tasks
                .filter(\.isCompleted)
                .sorted { ($0.completedAt ?? .distantPast) > ($1.completedAt ?? .distantPast) }
            guard !finished.isEmpty else { return [TaskSection(id: "all", tasks: tasks)] }
            let history = grouped(finished, by: { $0.completedAt }, ascending: false, now: now, calendar: calendar).map { day in
                TaskSection(
                    id: "completed-\(day.id)",
                    title: "Completed · \(day.title ?? "")",
                    detail: day.detail,
                    isCompletedHistory: true,
                    tasks: day.tasks
                )
            }
            return (open.isEmpty ? [] : [TaskSection(id: "all", tasks: open)]) + history

        default:
            return [TaskSection(id: "all", tasks: tasks)]
        }
    }

    private static func grouped(
        _ tasks: [TaskItem],
        by date: (TaskItem) -> Date?,
        ascending: Bool,
        now: Date,
        calendar: Calendar
    ) -> [TaskSection] {
        let buckets = Dictionary(grouping: tasks) { task in date(task).map { calendar.startOfDay(for: $0) } ?? .distantPast }
        let days = buckets.keys.sorted(by: ascending ? (<) : (>))
        return days.map { day in
            let title = day == .distantPast ? "No Date" : DueDateText.dayLabel(for: day, now: now, calendar: calendar)
            let detail = day == .distantPast ? nil : day.formatted(.dateTime.month(.abbreviated).day())
            return TaskSection(
                id: "\(day.timeIntervalSinceReferenceDate)",
                title: title,
                detail: title == detail ? nil : detail,
                tasks: buckets[day] ?? []
            )
        }
    }
}

public enum DueDateText {
    /// "Today", "Tomorrow, 4:00 PM", "Friday", "Oct 30".
    public static func label(for date: Date, hasTime: Bool, now: Date = Date(), calendar: Calendar = .current) -> String {
        let day = dayLabel(for: date, now: now, calendar: calendar)
        guard hasTime else { return day }
        return "\(day), \(date.formatted(date: .omitted, time: .shortened))"
    }

    public static func dayLabel(for date: Date, now: Date = Date(), calendar: Calendar = .current) -> String {
        switch dayDifference(from: now, to: date, calendar: calendar) {
        case 0: return "Today"
        case 1: return "Tomorrow"
        case -1: return "Yesterday"
        case 2...6: return date.formatted(.dateTime.weekday(.wide))
        default:
            if calendar.isDate(date, equalTo: now, toGranularity: .year) {
                return date.formatted(.dateTime.month(.abbreviated).day())
            }
            return date.formatted(.dateTime.month(.abbreviated).day().year())
        }
    }

    /// The coming Monday (or the locale's first weekday, where that isn't Sunday).
    public static func nextWeek(from now: Date = Date(), calendar: Calendar = .current) -> Date {
        let today = calendar.startOfDay(for: now)
        let weekday = calendar.firstWeekday == 1 ? 2 : calendar.firstWeekday
        return calendar.nextDate(after: today, matching: DateComponents(weekday: weekday), matchingPolicy: .nextTime) ?? today
    }

    public static func dayDifference(from start: Date, to end: Date, calendar: Calendar = .current) -> Int {
        calendar.dateComponents([.day], from: calendar.startOfDay(for: start), to: calendar.startOfDay(for: end)).day ?? 0
    }
}
