import Foundation

/// Everything the app persists, in one value.
public struct AppData: Codable, Hashable, Sendable {
    public static let currentVersion = 1

    public var version: Int
    public var projects: [Project]
    public var tasks: [TaskItem]

    public init(version: Int = AppData.currentVersion, projects: [Project] = [], tasks: [TaskItem] = []) {
        self.version = version
        self.projects = projects
        self.tasks = tasks
    }

    /// The first-launch content: a small, realistic workspace.
    public static func sample(now: Date = Date(), calendar: Calendar = .current) -> AppData {
        let today = calendar.startOfDay(for: now)
        func day(_ offset: Int) -> Date { calendar.date(byAdding: .day, value: offset, to: today) ?? today }
        func at(_ offset: Int, _ hour: Int, _ minute: Int = 0) -> Date {
            calendar.date(bySettingHour: hour, minute: minute, second: 0, of: day(offset)) ?? day(offset)
        }

        let work = Project(name: "Work", icon: "folder", color: .blue)
        let personal = Project(name: "Personal", icon: "folder", color: .green)
        let projects = Project(name: "Projects", icon: "folder", color: .white)

        let gym = RecurrenceRule(frequency: .weekly, weekdays: [2, 4, 6])
        var order = 0.0
        func next() -> Double { order += 1; return order }

        let tasks: [TaskItem] = [
            TaskItem(
                title: "Review contract",
                notes: "Review the final agreement before sending.",
                priority: .high, projectID: work.id, dueDate: today,
                subtasks: [Subtask(title: "Check clauses"), Subtask(title: "Confirm payment terms")],
                sortOrder: next()
            ),
            TaskItem(
                title: "Send weekly report",
                priority: .medium, projectID: work.id, dueDate: at(0, 16), hasDueTime: true,
                reminderOffset: 900, recurrence: .weekly, sortOrder: next()
            ),
            TaskItem(title: "Call supplier", projectID: personal.id, dueDate: today, sortOrder: next()),
            TaskItem(
                title: "Review invoices", status: .done, projectID: work.id, dueDate: today,
                sortOrder: next(), completedAt: now
            ),
            TaskItem(title: "Book dentist appointment", priority: .low, projectID: personal.id, dueDate: today, sortOrder: next()),

            TaskItem(title: "Buy groceries", notes: "Oat milk, coffee beans, lemons.", sortOrder: next()),
            TaskItem(
                title: "Read article on interface typography",
                attachments: [Attachment(kind: .link, name: "developer.apple.com", location: "https://developer.apple.com/design/human-interface-guidelines/typography")],
                sortOrder: next()
            ),
            TaskItem(title: "Organize photo library", sortOrder: next()),

            TaskItem(title: "Quarterly planning", priority: .high, projectID: work.id, dueDate: at(2, 10), hasDueTime: true, sortOrder: next()),
            TaskItem(title: "Renew passport", priority: .medium, projectID: personal.id, dueDate: day(5), sortOrder: next()),
            TaskItem(title: "Prepare team offsite", projectID: projects.id, dueDate: day(9), sortOrder: next()),
            TaskItem(title: "Pay rent", projectID: personal.id, dueDate: day(8), recurrence: .monthly, sortOrder: next()),
            TaskItem(title: "Redesign portfolio site", status: .inProgress, projectID: projects.id, sortOrder: next()),
            TaskItem(title: "Gym", projectID: personal.id, dueDate: gym.nextDate(after: today, calendar: calendar), recurrence: gym, sortOrder: next())
        ]

        return AppData(projects: [work, personal, projects], tasks: tasks)
    }
}
