import Foundation

/// Edits that touch more than one task, kept free of UI so they can be checked directly.
public enum TaskOperations {
    /// Changes the status of `ids`.
    ///
    /// Finishing a repeating task schedules its next occurrence and hands the repeat rule to it.
    /// Reopening that task takes the rule back and removes the occurrence again — unless the
    /// occurrence has been edited or finished since, in which case both are kept. If the
    /// occurrence has been deleted, the task simply takes the rule back.
    public static func setStatus(
        _ status: TaskStatus,
        for ids: Set<UUID>,
        in tasks: inout [TaskItem],
        now: Date = Date(),
        calendar: Calendar = .current
    ) {
        var spawned: [TaskItem] = []
        var retracted: Set<UUID> = []

        for index in tasks.indices where ids.contains(tasks[index].id) && tasks[index].status != status {
            let wasDone = tasks[index].isCompleted
            tasks[index].setStatus(status, at: now)

            if status == .done, !wasDone, let next = tasks[index].nextOccurrence(now: now, calendar: calendar) {
                tasks[index].recurrence = nil
                tasks[index].nextOccurrenceID = next.id
                tasks[index].nextOccurrenceDueDate = next.dueDate
                tasks[index].nextOccurrenceRule = next.recurrence
                spawned.append(next)
            } else if wasDone, status != .done, let nextID = tasks[index].nextOccurrenceID {
                let original = tasks[index]
                tasks[index].nextOccurrenceID = nil
                tasks[index].nextOccurrenceDueDate = nil
                tasks[index].nextOccurrenceRule = nil
                if let next = tasks.first(where: { $0.id == nextID }) {
                    if isUntouched(next, comparedWith: original) {
                        tasks[index].recurrence = next.recurrence
                        tasks[index].recurrenceDay = next.recurrenceDay
                        retracted.insert(nextID)
                    }
                } else {
                    tasks[index].recurrence = original.nextOccurrenceRule
                }
            }
        }

        tasks.removeAll { retracted.contains($0.id) }
        tasks.append(contentsOf: spawned)
    }

    /// Removes a project without losing anything under it: its sub-projects and tasks move up to
    /// its parent, or to the top level and "no project" when it has none.
    public static func deleteProject(_ id: UUID, projects: inout [Project], tasks: inout [TaskItem]) {
        let declared = projects.first { $0.id == id }?.parentID
        let parent = projects.contains { $0.id == declared && $0.id != id } ? declared : nil
        projects.removeAll { $0.id == id }
        for index in projects.indices where projects[index].parentID == id { projects[index].parentID = parent }
        for index in tasks.indices where tasks[index].projectID == id { tasks[index].projectID = parent }
    }

    /// The sort order that puts a task directly in front of `targetID` in `ordered`
    /// (the list as displayed), or `nil` when the move makes no sense.
    public static func sortOrder(placing id: UUID, before targetID: UUID, in ordered: [TaskItem]) -> Double? {
        guard id != targetID else { return nil }
        let others = ordered.filter { $0.id != id }
        guard let targetIndex = others.firstIndex(where: { $0.id == targetID }) else { return nil }
        let after = others[targetIndex].sortOrder
        guard targetIndex > 0 else { return after - 1 }
        let before = others[targetIndex - 1].sortOrder
        // Completed rows always sit last, so at the open/completed boundary the neighbours
        // aren't in sort-order sequence. Landing just after the row above keeps the task
        // where it was dropped.
        return before < after ? (before + after) / 2 : before + 1
    }

    /// Moves a task directly in front of `targetID` in `ordered` (the list as displayed).
    /// When there's no room between its new neighbours (equal orders, or so many drops into one
    /// gap that the numbers can't get closer), every task is renumbered first, keeping their order.
    public static func move(_ id: UUID, before targetID: UUID, in ordered: [TaskItem], tasks: inout [TaskItem]) {
        var shown = ordered
        if !hasRoom(placing: id, before: targetID, in: ordered) {
            let ranked = tasks.sorted { TaskQuery.precedes($0, $1, by: .manual) }
            var fresh: [UUID: Double] = [:]
            for (rank, task) in ranked.enumerated() { fresh[task.id] = Double(rank + 1) }
            for index in tasks.indices { tasks[index].sortOrder = fresh[tasks[index].id] ?? tasks[index].sortOrder }
            shown = ordered.map { task in
                var task = task
                task.sortOrder = fresh[task.id] ?? task.sortOrder
                return task
            }
        }
        guard let newOrder = sortOrder(placing: id, before: targetID, in: shown),
              let index = tasks.firstIndex(where: { $0.id == id }) else { return }
        tasks[index].sortOrder = newOrder
    }

    private static func hasRoom(placing id: UUID, before targetID: UUID, in ordered: [TaskItem]) -> Bool {
        let others = ordered.filter { $0.id != id }
        guard let targetIndex = others.firstIndex(where: { $0.id == targetID }), targetIndex > 0 else { return true }
        let above = others[targetIndex - 1], target = others[targetIndex]
        // Across the open/completed boundary the neighbours aren't in sequence; see `sortOrder`.
        guard above.isCompleted == target.isCompleted else { return true }
        let middle = (above.sortOrder + target.sortOrder) / 2
        return above.sortOrder < middle && middle < target.sortOrder
    }

    /// Whether an occurrence is still exactly as its completion created it.
    static func isUntouched(_ next: TaskItem, comparedWith original: TaskItem) -> Bool {
        next.status == .todo
            && next.nextOccurrenceID == nil
            && next.dueDate == original.nextOccurrenceDueDate
            && next.recurrence == original.nextOccurrenceRule
            && next.hasDueTime == original.hasDueTime
            && next.reminderOffset == original.reminderOffset
            && next.sortOrder == original.sortOrder
            && next.title == original.title
            && next.notes == original.notes
            && next.priority == original.priority
            && next.projectID == original.projectID
            && next.attachments == original.attachments
            && next.subtasks.map(\.title) == original.subtasks.map(\.title)
            && !next.subtasks.contains(where: \.isDone)
    }
}

public enum MonthGrid {
    /// Six full weeks covering the month that contains `date`, starting on the calendar's first weekday.
    public static func days(containing date: Date, calendar: Calendar = .current) -> [Date] {
        let monthStart = calendar.dateInterval(of: .month, for: date)?.start ?? calendar.startOfDay(for: date)
        let weekday = calendar.component(.weekday, from: monthStart)
        let leading = (weekday - calendar.firstWeekday + 7) % 7
        let gridStart = calendar.date(byAdding: .day, value: -leading, to: monthStart) ?? monthStart
        return (0..<42).compactMap { calendar.date(byAdding: .day, value: $0, to: gridStart) }
    }
}
