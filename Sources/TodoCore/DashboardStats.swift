import Foundation

/// Numbers behind the dashboard, computed in one pass over the tasks.
public struct DashboardStats: Sendable {
    public struct DayCount: Identifiable, Hashable, Sendable {
        public var day: Date
        public var count: Int
        public var id: Date { day }
    }

    public struct ProjectProgress: Identifiable, Hashable, Sendable {
        public var project: Project
        public var done: Int
        public var total: Int
        public var id: UUID { project.id }
        public var fraction: Double { total == 0 ? 0 : Double(done) / Double(total) }
    }

    public var open: Int = 0
    public var done: Int = 0
    public var dueToday: Int = 0
    public var overdue: Int = 0
    public var completedToday: Int = 0
    public var completedThisWeek: Int = 0
    public var openByPriority: [Priority: Int] = [:]
    /// Completions per day, oldest first, ending today.
    public var activity: [DayCount] = []
    public var projects: [ProjectProgress] = []
    /// The next open tasks with a due date, soonest (including overdue) first.
    public var upNext: [TaskItem] = []

    public var completionRate: Double {
        let total = open + done
        return total == 0 ? 0 : Double(done) / Double(total)
    }

    public init(tasks: [TaskItem], projects allProjects: [Project], days: Int = 14, now: Date = Date(), calendar: Calendar = .current) {
        let startOfToday = calendar.startOfDay(for: now)
        let startOfTomorrow = calendar.date(byAdding: .day, value: 1, to: startOfToday) ?? now
        let weekAgo = calendar.date(byAdding: .day, value: -6, to: startOfToday) ?? startOfToday
        let firstDay = calendar.date(byAdding: .day, value: -(days - 1), to: startOfToday) ?? startOfToday

        var perDay: [Date: Int] = [:]
        var perProject: [UUID: (done: Int, total: Int)] = [:]

        for task in tasks {
            if let projectID = task.projectID {
                var entry = perProject[projectID] ?? (0, 0)
                entry.total += 1
                if task.isCompleted { entry.done += 1 }
                perProject[projectID] = entry
            }

            if task.isCompleted {
                done += 1
                if let completedAt = task.completedAt {
                    let day = calendar.startOfDay(for: completedAt)
                    if day >= firstDay { perDay[day, default: 0] += 1 }
                    if day == startOfToday { completedToday += 1 }
                    if day >= weekAgo { completedThisWeek += 1 }
                }
            } else {
                open += 1
                openByPriority[task.priority, default: 0] += 1
                if task.isOverdue(now: now, calendar: calendar) {
                    overdue += 1
                } else if let due = task.dueDate, due >= startOfToday, due < startOfTomorrow {
                    dueToday += 1
                }
            }
        }

        activity = (0..<days).compactMap { offset in
            guard let day = calendar.date(byAdding: .day, value: offset, to: firstDay) else { return nil }
            return DayCount(day: day, count: perDay[day] ?? 0)
        }

        projects = allProjects.map { project in
            // A project includes its sub-projects, as it does in the sidebar and on its own page.
            let branch = allProjects.subtreeIDs(of: project.id).compactMap { perProject[$0] }
            return ProjectProgress(project: project, done: branch.reduce(0) { $0 + $1.done }, total: branch.reduce(0) { $0 + $1.total })
        }

        upNext = Array(
            tasks
                .filter { !$0.isCompleted && $0.dueDate != nil }
                .sorted { ($0.dueDate ?? .distantFuture) < ($1.dueDate ?? .distantFuture) }
                .prefix(6)
        )
    }
}
