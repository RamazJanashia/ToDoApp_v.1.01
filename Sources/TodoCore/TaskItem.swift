import Foundation

public struct Subtask: Identifiable, Codable, Hashable, Sendable {
    public var id: UUID
    public var title: String
    public var isDone: Bool

    public init(id: UUID = UUID(), title: String, isDone: Bool = false) {
        self.id = id
        self.title = title
        self.isDone = isDone
    }
}

public struct Attachment: Identifiable, Codable, Hashable, Sendable {
    public enum Kind: String, Codable, Sendable {
        case file
        case link
    }

    public var id: UUID
    public var kind: Kind
    /// What the user sees: the original file name, or the link's host.
    public var name: String
    /// Files: the name of the copy inside the attachments folder. Links: the absolute URL string.
    public var location: String
    public var addedAt: Date

    public init(id: UUID = UUID(), kind: Kind, name: String, location: String, addedAt: Date = Date()) {
        self.id = id
        self.kind = kind
        self.name = name
        self.location = location
        self.addedAt = addedAt
    }
}

public struct TaskItem: Identifiable, Codable, Hashable, Sendable {
    public var id: UUID
    public var title: String
    public var notes: String
    public var status: TaskStatus
    public var priority: Priority
    public var projectID: UUID?
    /// Start of day for all-day tasks; the exact moment when `hasDueTime` is true.
    public var dueDate: Date?
    public var hasDueTime: Bool
    /// Seconds before the due moment to send a reminder. `nil` means no reminder.
    public var reminderOffset: TimeInterval?
    public var recurrence: RecurrenceRule?
    /// The day of the month a repeating series aims for (e.g. 31), carried from one
    /// occurrence to the next so short months don't pull the series earlier.
    public var recurrenceDay: Int?
    /// Set on a finished repeating task: the occurrence its completion created. Reopening
    /// the task removes that occurrence again if it hasn't been touched.
    public var nextOccurrenceID: UUID?
    public var subtasks: [Subtask]
    public var attachments: [Attachment]
    /// Manual ordering; lower comes first.
    public var sortOrder: Double
    public var createdAt: Date
    public var completedAt: Date?

    public init(
        id: UUID = UUID(),
        title: String,
        notes: String = "",
        status: TaskStatus = .todo,
        priority: Priority = Priority.none,
        projectID: UUID? = nil,
        dueDate: Date? = nil,
        hasDueTime: Bool = false,
        reminderOffset: TimeInterval? = nil,
        recurrence: RecurrenceRule? = nil,
        recurrenceDay: Int? = nil,
        nextOccurrenceID: UUID? = nil,
        subtasks: [Subtask] = [],
        attachments: [Attachment] = [],
        sortOrder: Double = 0,
        createdAt: Date = Date(),
        completedAt: Date? = nil
    ) {
        self.id = id
        self.title = title
        self.notes = notes
        self.status = status
        self.priority = priority
        self.projectID = projectID
        self.dueDate = dueDate
        self.hasDueTime = hasDueTime
        self.reminderOffset = reminderOffset
        self.recurrence = recurrence
        self.recurrenceDay = recurrenceDay
        self.nextOccurrenceID = nextOccurrenceID
        self.subtasks = subtasks
        self.attachments = attachments
        self.sortOrder = sortOrder
        self.createdAt = createdAt
        self.completedAt = completedAt
    }

    // Decoding tolerates missing keys so files written by older versions keep loading.
    private enum CodingKeys: String, CodingKey {
        case id, title, notes, status, priority, projectID, dueDate, hasDueTime, reminderOffset
        case recurrence, recurrenceDay, nextOccurrenceID, subtasks, attachments, sortOrder, createdAt, completedAt
    }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        id = try c.decode(UUID.self, forKey: .id)
        title = try c.decodeIfPresent(String.self, forKey: .title) ?? ""
        notes = try c.decodeIfPresent(String.self, forKey: .notes) ?? ""
        status = try c.decodeIfPresent(TaskStatus.self, forKey: .status) ?? TaskStatus.todo
        priority = try c.decodeIfPresent(Priority.self, forKey: .priority) ?? Priority.none
        projectID = try c.decodeIfPresent(UUID.self, forKey: .projectID)
        dueDate = try c.decodeIfPresent(Date.self, forKey: .dueDate)
        hasDueTime = try c.decodeIfPresent(Bool.self, forKey: .hasDueTime) ?? false
        reminderOffset = try c.decodeIfPresent(TimeInterval.self, forKey: .reminderOffset)
        recurrence = try c.decodeIfPresent(RecurrenceRule.self, forKey: .recurrence)
        recurrenceDay = try c.decodeIfPresent(Int.self, forKey: .recurrenceDay)
        nextOccurrenceID = try c.decodeIfPresent(UUID.self, forKey: .nextOccurrenceID)
        subtasks = try c.decodeIfPresent([Subtask].self, forKey: .subtasks) ?? []
        attachments = try c.decodeIfPresent([Attachment].self, forKey: .attachments) ?? []
        sortOrder = try c.decodeIfPresent(Double.self, forKey: .sortOrder) ?? 0
        createdAt = try c.decodeIfPresent(Date.self, forKey: .createdAt) ?? Date()
        completedAt = try c.decodeIfPresent(Date.self, forKey: .completedAt)
    }

    public var isCompleted: Bool { status == .done }

    public var completedSubtaskCount: Int { subtasks.filter(\.isDone).count }

    public mutating func setCompleted(_ completed: Bool, at date: Date = Date()) {
        setStatus(completed ? .done : .todo, at: date)
    }

    public mutating func setStatus(_ newStatus: TaskStatus, at date: Date = Date()) {
        status = newStatus
        completedAt = newStatus == .done ? (completedAt ?? date) : nil
    }

    /// Sets the due day, keeping the time of day when the task has one.
    public mutating func reschedule(toDay day: Date, calendar: Calendar = .current) {
        let start = calendar.startOfDay(for: day)
        guard hasDueTime, let current = dueDate else {
            dueDate = start
            return
        }
        let time = calendar.dateComponents([.hour, .minute], from: current)
        dueDate = calendar.date(bySettingHour: time.hour ?? 9, minute: time.minute ?? 0, second: 0, of: start) ?? start
    }

    public mutating func clearDueDate() {
        dueDate = nil
        hasDueTime = false
        reminderOffset = nil
    }

    /// When the reminder fires. All-day tasks anchor their reminder at 9:00 on the due day.
    public func reminderDate(calendar: Calendar = .current) -> Date? {
        guard let dueDate, let reminderOffset else { return nil }
        let anchor = hasDueTime
            ? dueDate
            : calendar.date(bySettingHour: 9, minute: 0, second: 0, of: dueDate) ?? dueDate
        return anchor.addingTimeInterval(-reminderOffset)
    }

    public func isOverdue(now: Date = Date(), calendar: Calendar = .current) -> Bool {
        guard !isCompleted, let dueDate else { return false }
        if hasDueTime { return dueDate < now }
        return dueDate < calendar.startOfDay(for: now)
    }

    public func isDue(onSameDayAs day: Date, calendar: Calendar = .current) -> Bool {
        guard let dueDate else { return false }
        return calendar.isDate(dueDate, inSameDayAs: day)
    }

    /// A fresh, incomplete copy scheduled for the next occurrence, or `nil` if the task doesn't repeat.
    /// The next occurrence always lands after today, so finishing an overdue task doesn't
    /// immediately produce another overdue one.
    public func nextOccurrence(now: Date = Date(), calendar: Calendar = .current) -> TaskItem? {
        guard let recurrence else { return nil }
        let startOfToday = calendar.startOfDay(for: now)
        let base = dueDate ?? startOfToday
        let threshold = hasDueTime ? now : (calendar.date(byAdding: .day, value: 1, to: startOfToday) ?? now)
        let preferredDay = Self.preferredDay(for: base, remembered: recurrenceDay, calendar: calendar)

        var next = recurrence.nextDate(after: base, preferredDay: preferredDay, calendar: calendar)
        var steps = 0
        while next < threshold && steps < 5_000 {
            next = recurrence.nextDate(after: next, preferredDay: preferredDay, calendar: calendar)
            steps += 1
        }

        var copy = self
        copy.id = UUID()
        copy.recurrenceDay = preferredDay
        copy.nextOccurrenceID = nil
        copy.status = .todo
        copy.completedAt = nil
        copy.createdAt = now
        copy.dueDate = next
        copy.subtasks = subtasks.map { Subtask(title: $0.title) }
        return copy
    }
}

extension TaskItem {
    /// The remembered day wins only while `base` sits on a clamped month end (Feb 28 standing
    /// in for the 31st). Any other day means the user moved the task, and that day becomes the target.
    static func preferredDay(for base: Date, remembered: Int?, calendar: Calendar) -> Int {
        let day = calendar.component(.day, from: base)
        guard let remembered, remembered > day,
              let length = calendar.range(of: .day, in: .month, for: base)?.count,
              day == length else { return day }
        return remembered
    }
}

public enum ReminderPreset {
    /// Offsets offered in the reminder picker, in seconds before the due moment.
    public static let offsets: [TimeInterval] = [0, 300, 900, 1_800, 3_600, 7_200, 86_400, 172_800]

    public static func title(for offset: TimeInterval) -> String {
        switch offset {
        case 0: return "At time of due"
        case 300: return "5 minutes before"
        case 900: return "15 minutes before"
        case 1_800: return "30 minutes before"
        case 3_600: return "1 hour before"
        case 7_200: return "2 hours before"
        case 86_400: return "1 day before"
        case 172_800: return "2 days before"
        default:
            let formatter = DateComponentsFormatter()
            formatter.unitsStyle = .full
            formatter.maximumUnitCount = 2
            return "\(formatter.string(from: offset) ?? "\(Int(offset / 60)) minutes") before"
        }
    }
}
