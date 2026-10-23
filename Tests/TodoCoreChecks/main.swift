import Foundation
import TodoCore

// A tiny check runner: the Command Line Tools ship without XCTest, so these run as
// `swift run TodoCoreChecks`. Each check prints its name; failures exit non-zero.

var failures = 0
var checks = 0

func check(_ name: String, _ condition: @autoclosure () -> Bool, _ detail: @autoclosure () -> String = "") {
    checks += 1
    if condition() {
        print("  ✓ \(name)")
    } else {
        failures += 1
        print("  ✗ \(name) \(detail())")
    }
}

var calendar = Calendar(identifier: .gregorian)
calendar.timeZone = TimeZone(identifier: "Europe/Berlin")!
calendar.firstWeekday = 2

func date(_ y: Int, _ m: Int, _ d: Int, _ h: Int = 0, _ min: Int = 0) -> Date {
    calendar.date(from: DateComponents(year: y, month: m, day: d, hour: h, minute: min))!
}

// Friday, October 23, 2026, mid-afternoon.
let now = date(2026, 10, 23, 15, 30)
let today = calendar.startOfDay(for: now)

print("Recurrence")
check("daily adds one day", RecurrenceRule.daily.nextDate(after: date(2026, 10, 23), calendar: calendar) == date(2026, 10, 24))
check("every 3 days", RecurrenceRule(frequency: .daily, interval: 3).nextDate(after: date(2026, 10, 23), calendar: calendar) == date(2026, 10, 26))
check("weekly adds seven days", RecurrenceRule.weekly.nextDate(after: date(2026, 10, 23), calendar: calendar) == date(2026, 10, 30))
check("weekdays skip the weekend", RecurrenceRule.weekdays.nextDate(after: date(2026, 10, 23), calendar: calendar) == date(2026, 10, 26))
check("weekdays mid-week", RecurrenceRule.weekdays.nextDate(after: date(2026, 10, 20), calendar: calendar) == date(2026, 10, 21))
let monWedFri = RecurrenceRule(frequency: .weekly, weekdays: [2, 4, 6])
check("Mon/Wed/Fri from Monday", monWedFri.nextDate(after: date(2026, 10, 19), calendar: calendar) == date(2026, 10, 21))
check("Mon/Wed/Fri from Friday", monWedFri.nextDate(after: date(2026, 10, 23), calendar: calendar) == date(2026, 10, 26))
let biweeklyMonday = RecurrenceRule(frequency: .weekly, interval: 2, weekdays: [2])
check("every 2 weeks on Monday skips a week", biweeklyMonday.nextDate(after: date(2026, 10, 19), calendar: calendar) == date(2026, 11, 2))
check("monthly clamps to month end", RecurrenceRule.monthly.nextDate(after: date(2026, 1, 31), calendar: calendar) == date(2026, 2, 28))
check("yearly", RecurrenceRule.yearly.nextDate(after: date(2026, 10, 23), calendar: calendar) == date(2027, 10, 23))
check("weekday summary", RecurrenceRule.weekdays.summary == "Every weekday", RecurrenceRule.weekdays.summary)
check("interval summary", RecurrenceRule(frequency: .monthly, interval: 3).summary == "Every 3 months")
check("interval floor is one", RecurrenceRule(frequency: .daily, interval: 0).interval == 1)

print("Next occurrence")
var daily = TaskItem(title: "Water plants", dueDate: date(2026, 10, 20), recurrence: .daily, subtasks: [Subtask(title: "Ferns", isDone: true)])
let afterOverdue = daily.nextOccurrence(now: now, calendar: calendar)
check("overdue daily task jumps past today", afterOverdue?.dueDate == date(2026, 10, 24), "\(String(describing: afterOverdue?.dueDate))")
check("next occurrence is a new, open task", afterOverdue.map { $0.id != daily.id && !$0.isCompleted } == true)
check("subtasks reset", afterOverdue?.subtasks.allSatisfy { !$0.isDone } == true)
daily.dueDate = date(2026, 10, 30)
check("early completion keeps the cadence", daily.nextOccurrence(now: now, calendar: calendar)?.dueDate == date(2026, 10, 31))
let timed = TaskItem(title: "Standup", dueDate: date(2026, 10, 23, 9), hasDueTime: true, recurrence: .daily)
check("timed task keeps its time", timed.nextOccurrence(now: now, calendar: calendar)?.dueDate == date(2026, 10, 24, 9))
check("non-repeating task has no next", TaskItem(title: "Once").nextOccurrence(now: now, calendar: calendar) == nil)

print("Month-end repeats")
func chain(_ task: TaskItem, _ count: Int) -> [Date] {
    var current = task
    var dates: [Date] = []
    for _ in 0..<count {
        // Complete each occurrence on its due day so catch-up logic doesn't skip ahead.
        guard let next = current.nextOccurrence(now: current.dueDate!, calendar: calendar) else { break }
        dates.append(next.dueDate!)
        current = next
    }
    return dates
}
let endOfJanuary = TaskItem(title: "Invoice", dueDate: date(2026, 1, 31), recurrence: .monthly)
check("monthly on the 31st returns to month end after February",
      chain(endOfJanuary, 3) == [date(2026, 2, 28), date(2026, 3, 31), date(2026, 4, 30)], "\(chain(endOfJanuary, 3))")
let leapDay = TaskItem(title: "Leap", dueDate: date(2028, 2, 29), recurrence: .yearly)
check("yearly on Feb 29 returns to Feb 29 in the next leap year",
      chain(leapDay, 4).last == date(2032, 2, 29), "\(chain(leapDay, 4))")
var movedByUser = TaskItem(title: "Moved", dueDate: date(2026, 2, 28), recurrence: .monthly)
movedByUser.dueDate = date(2026, 3, 15)
check("a day the user picked becomes the new target", chain(movedByUser, 1) == [date(2026, 4, 15)])

print("Completion & reminders")
var task = TaskItem(title: "Ship")
task.setCompleted(true, at: now)
check("completing sets status and date", task.status == .done && task.completedAt == now)
task.setStatus(.inProgress)
check("leaving done clears completedAt", task.completedAt == nil && !task.isCompleted)
let allDay = TaskItem(title: "All day", dueDate: today, reminderOffset: 3_600)
check("all-day reminder anchors at 9:00", allDay.reminderDate(calendar: calendar) == date(2026, 10, 23, 8))
let atTime = TaskItem(title: "Timed", dueDate: date(2026, 10, 23, 16), hasDueTime: true, reminderOffset: 900)
check("timed reminder subtracts offset", atTime.reminderDate(calendar: calendar) == date(2026, 10, 23, 15, 45))
check("past timed task is overdue", TaskItem(title: "x", dueDate: date(2026, 10, 23, 9), hasDueTime: true).isOverdue(now: now, calendar: calendar))
check("all-day task due today isn't overdue", !TaskItem(title: "x", dueDate: today).isOverdue(now: now, calendar: calendar))
var moving = TaskItem(title: "Move me", dueDate: date(2026, 10, 23, 16, 30), hasDueTime: true)
moving.reschedule(toDay: date(2026, 10, 27), calendar: calendar)
check("rescheduling keeps the time of day", moving.dueDate == date(2026, 10, 27, 16, 30))

print("Reopening repeating tasks")
var series: [TaskItem] = [TaskItem(title: "Gym", dueDate: today, recurrence: .daily, sortOrder: 1)]
let gymID = series[0].id
TaskOperations.setStatus(.done, for: [gymID], in: &series, now: now, calendar: calendar)
check("completing spawns the next occurrence", series.count == 2 && series[0].recurrence == nil && series[0].nextOccurrenceID == series[1].id)
TaskOperations.setStatus(.todo, for: [gymID], in: &series, now: now, calendar: calendar)
check("reopening removes the untouched occurrence and restores the rule",
      series.count == 1 && series[0].recurrence == .daily && series[0].nextOccurrenceID == nil && !series[0].isCompleted)
TaskOperations.setStatus(.done, for: [gymID], in: &series, now: now, calendar: calendar)
series[1].notes = "Leg day"
TaskOperations.setStatus(.inProgress, for: [gymID], in: &series, now: now, calendar: calendar)
check("an edited occurrence is kept", series.count == 2 && series[1].notes == "Leg day")

print("Drag ordering")
let open1 = TaskItem(title: "a", sortOrder: 5), open2 = TaskItem(title: "b", sortOrder: 10), open3 = TaskItem(title: "c", sortOrder: 20)
let done1 = TaskItem(title: "done", status: .done, sortOrder: 3, completedAt: now)
let shown = [open1, open2, open3, done1]
let boundary = TaskOperations.sortOrder(placing: open3.id, before: done1.id, in: shown)
check("dropping above the first completed row keeps the task last among open ones", (boundary ?? 0) > 10, "\(String(describing: boundary))")
check("dropping between two rows lands between them", TaskOperations.sortOrder(placing: open3.id, before: open2.id, in: shown) == 7.5)
check("dropping on the first row goes above it", TaskOperations.sortOrder(placing: open3.id, before: open1.id, in: shown) == 4)
check("dropping on itself does nothing", TaskOperations.sortOrder(placing: open1.id, before: open1.id, in: shown) == nil)

print("Month grid")
let grid = MonthGrid.days(containing: date(2026, 10, 15), calendar: calendar)
check("six weeks starting Monday 28 Sep", grid.count == 42 && grid.first == date(2026, 9, 28) && grid.last == date(2026, 11, 8))

print("Queries")
let work = Project(name: "Work")
let tasks: [TaskItem] = [
    TaskItem(title: "Inbox open", sortOrder: 1),
    TaskItem(title: "Inbox done today", status: .done, sortOrder: 2, completedAt: now),
    TaskItem(title: "Inbox done last week", status: .done, sortOrder: 3, completedAt: date(2026, 10, 15)),
    TaskItem(title: "Due today", priority: .low, projectID: work.id, dueDate: today, sortOrder: 4),
    TaskItem(title: "Overdue", priority: .high, projectID: work.id, dueDate: date(2026, 10, 21), sortOrder: 5),
    TaskItem(title: "Tomorrow", projectID: work.id, dueDate: date(2026, 10, 24), sortOrder: 6),
    TaskItem(title: "Next week", notes: "quarterly numbers", dueDate: date(2026, 10, 30), sortOrder: 7)
]
func titles(_ query: TaskQuery) -> [String] { query.run(on: tasks, projects: [work], now: now, calendar: calendar).map(\.title) }

check("inbox: open first, today's completions kept, old ones hidden",
      titles(TaskQuery(scope: .inbox)) == ["Inbox open", "Next week", "Inbox done today"], "\(titles(TaskQuery(scope: .inbox)))")
check("today includes overdue", Set(titles(TaskQuery(scope: .today))) == ["Due today", "Overdue"])
check("upcoming is strictly after today", titles(TaskQuery(scope: .upcoming)) == ["Tomorrow", "Next week"])
check("completed newest first", titles(TaskQuery(scope: .completed)) == ["Inbox done today", "Inbox done last week"])
check("project scope", Set(titles(TaskQuery(scope: .project(work.id)))) == ["Due today", "Overdue", "Tomorrow"])
check("hide completed", !titles(TaskQuery(scope: .inbox, showCompleted: false)).contains("Inbox done today"))
check("priority sort", titles(TaskQuery(scope: .project(work.id), sort: .priority)).first == "Overdue")
check("due date sort", titles(TaskQuery(scope: .project(work.id), sort: .dueDate)) == ["Overdue", "Due today", "Tomorrow"])
check("priority filter", titles(TaskQuery(scope: .all, priorities: [.high])) == ["Overdue"])
check("search matches notes", titles(TaskQuery(scope: .all, searchText: "QUARTERLY")) == ["Next week"])
check("search matches project name", titles(TaskQuery(scope: .all, searchText: "work")).count == 3)
check("search needs every word", titles(TaskQuery(scope: .all, searchText: "inbox nope")).isEmpty)
check("open counts", TaskQuery.openCount(in: .today, tasks: tasks, now: now, calendar: calendar) == 2)

print("Sections")
let todayTasks = TaskQuery(scope: .today).run(on: tasks, projects: [work], now: now, calendar: calendar)
let todaySections = TaskSection.make(for: .today, tasks: todayTasks, now: now, calendar: calendar)
check("today splits overdue", todaySections.map(\.id) == ["overdue", "today"], "\(todaySections.map(\.id))")
let upcomingSections = TaskSection.make(for: .upcoming, tasks: TaskQuery(scope: .upcoming).run(on: tasks, projects: [], now: now, calendar: calendar), now: now, calendar: calendar)
check("upcoming groups by day", upcomingSections.map(\.title) == ["Tomorrow", date(2026, 10, 30).formatted(.dateTime.month(.abbreviated).day())], "\(upcomingSections.map(\.title))")
check("relative labels", DueDateText.dayLabel(for: date(2026, 10, 22), now: now, calendar: calendar) == "Yesterday")
// A reminder for a task due on the 24th, firing that morning, must say "Today" — not "Tomorrow"
// as it would if worked out on the 23rd when the reminder was scheduled.
check("reminder wording is relative to when it fires",
      DueDateText.label(for: date(2026, 10, 24), hasTime: false, now: date(2026, 10, 24, 9), calendar: calendar) == "Today")

print("Dashboard")
let stats = DashboardStats(tasks: tasks, projects: [work], now: now, calendar: calendar)
check("open / done", stats.open == 5 && stats.done == 2)
check("overdue / due today", stats.overdue == 1 && stats.dueToday == 1)
check("completed today and this week", stats.completedToday == 1 && stats.completedThisWeek == 1)
check("activity ends today", stats.activity.last?.day == today && stats.activity.count == 14)
check("project progress", stats.projects.first?.total == 3)
check("up next starts with overdue", stats.upNext.first?.title == "Overdue")

print("Persistence")
let directory = FileManager.default.temporaryDirectory.appendingPathComponent("TodoCoreChecks-\(UUID().uuidString)")
let store = DataFileStore(directory: directory)
let sample = AppData.sample(now: now, calendar: calendar)
do {
    let initial = try store.load()
    check("missing file loads as nil", initial == nil)
    try store.save(sample)
    let loaded = try store.load()
    check("round trip keeps every task", loaded?.tasks.map(\.id) == sample.tasks.map(\.id))
    check("round trip keeps recurrence and subtasks", loaded?.tasks.first?.subtasks.count == 2 && loaded?.tasks[1].recurrence == .weekly)

    let legacy = #"{"version":1,"projects":[],"tasks":[{"id":"6F2C1C1E-0E59-4C38-9D57-5B0B7C1F2A10","title":"Old"}]}"#
    try legacy.data(using: .utf8)!.write(to: store.fileURL)
    let migrated = try store.load()
    check("missing keys decode with defaults", migrated?.tasks.first?.status == .todo && migrated?.tasks.first?.priority == Priority.none)

    try "not json".data(using: .utf8)!.write(to: store.fileURL)
    var threw = false
    do { _ = try store.load() } catch { threw = true }
    check("corrupt file throws", threw)
    let moved = try store.quarantineDataFile(now: now)
    check("corrupt file is moved aside, not deleted", FileManager.default.fileExists(atPath: moved.path) && !FileManager.default.fileExists(atPath: store.fileURL.path))
    try "again".data(using: .utf8)!.write(to: store.fileURL)
    let movedAgain = try store.quarantineDataFile(now: now)
    check("two failures in the same second don't collide", movedAgain != moved && FileManager.default.fileExists(atPath: moved.path))
} catch {
    check("persistence threw \(error)", false)
}
try? FileManager.default.removeItem(at: directory)

print("")
if failures == 0 {
    print("All \(checks) checks passed.")
} else {
    print("\(failures) of \(checks) checks failed.")
    exit(1)
}
