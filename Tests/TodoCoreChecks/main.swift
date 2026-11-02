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

let allOpen = titles(TaskQuery(scope: .allOpen))
check("all tasks: open ones from everywhere, then today's completions, old ones hidden",
      allOpen == ["Inbox open", "Due today", "Overdue", "Tomorrow", "Next week", "Inbox done today"], "\(allOpen)")
check("today includes overdue", Set(titles(TaskQuery(scope: .today))) == ["Due today", "Overdue"])
check("upcoming is strictly after today", titles(TaskQuery(scope: .upcoming)) == ["Tomorrow", "Next week"])
check("completed newest first", titles(TaskQuery(scope: .completed)) == ["Inbox done today", "Inbox done last week"])
check("project scope", Set(titles(TaskQuery(scope: .project(work.id)))) == ["Due today", "Overdue", "Tomorrow"])
check("hide completed", !titles(TaskQuery(scope: .allOpen, showCompleted: false)).contains("Inbox done today"))
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

print("Subtask progress")
let half = TaskItem(title: "Half", subtasks: [Subtask(title: "a", isDone: true), Subtask(title: "b")])
check("progress follows subtasks", half.progress == 0.5)
check("no subtasks, not done, is zero", TaskItem(title: "x").progress == 0)
check("a finished task counts in full", TaskItem(title: "x", status: .done, subtasks: [Subtask(title: "a")]).progress == 1)
check("list progress averages tasks", TaskItem.progress(of: [half, TaskItem(title: "y", status: .done)]) == 0.75)
check("empty list has no progress", TaskItem.progress(of: []) == 0)

print("Sub-projects")
let parent = Project(name: "Parent")
let child = Project(name: "Child", parentID: parent.id)
let grandchild = Project(name: "Grandchild", parentID: child.id)
let loner = Project(name: "Loner")
let tree = [grandchild, loner, child, parent]
check("outline puts parents above children", tree.outline().map(\.project.name) == ["Loner", "Parent", "Child", "Grandchild"], "\(tree.outline().map(\.project.name))")
check("outline depth", tree.outline().map(\.depth) == [0, 0, 1, 2])
check("outline marks parents", tree.outline().map(\.hasChildren) == [false, true, true, false])
check("collapsing hides everything below", tree.outline(collapsed: [parent.id]).map(\.project.name) == ["Loner", "Parent"])
check("collapsing a leaf does nothing", tree.outline(collapsed: [loner.id]).count == 4)
check("subtree includes itself and descendants", tree.subtreeIDs(of: parent.id) == [parent.id, child.id, grandchild.id])
check("a project can't move inside itself", !tree.parentChoices(for: parent.id).contains { $0.id == grandchild.id } && tree.parentChoices(for: parent.id).map(\.id) == [loner.id])
let orphan = Project(name: "Orphan", parentID: UUID())
check("a missing parent means top level", [orphan].outline().first?.depth == 0)
var loopA = Project(name: "A"), loopB = Project(name: "B", parentID: loopA.id)
loopA.parentID = loopB.id
check("a parent loop still shows every project once", [loopA, loopB].outline().count == 2)
check("a parent loop ends", [loopA, loopB].subtreeIDs(of: loopA.id).count == 2)

var moved = tree
check("dropping beside a sub-project joins its parent", moved.moveProject(loner.id, before: child.id) && moved.first { $0.id == loner.id }?.parentID == parent.id)
var cyclic = tree
check("dropping a parent into its own subtree is refused", !cyclic.moveProject(parent.id, before: grandchild.id) && cyclic == tree)

let inChild = TaskItem(title: "In child", projectID: child.id)
let inParent = TaskItem(title: "In parent", projectID: parent.id)
let elsewhere = TaskItem(title: "Elsewhere", projectID: loner.id)
let nested = TaskScope.projects(tree.subtreeIDs(of: parent.id))
check("a parent lists its sub-projects' tasks", Set(TaskQuery(scope: nested).run(on: [inChild, inParent, elsewhere], projects: tree, now: now, calendar: calendar).map(\.title)) == ["In child", "In parent"])
check("a parent counts its sub-projects' tasks", TaskQuery.openCount(in: nested, tasks: [inChild, inParent, elsewhere], now: now, calendar: calendar) == 2)

var deletingProjects = tree
var deletingTasks = [inChild, elsewhere]
TaskOperations.deleteProject(child.id, projects: &deletingProjects, tasks: &deletingTasks)
check("deleting moves sub-projects up", deletingProjects.first { $0.id == grandchild.id }?.parentID == parent.id && deletingProjects.count == 3)
check("deleting moves tasks to the parent", deletingTasks[0].projectID == parent.id)
TaskOperations.deleteProject(loner.id, projects: &deletingProjects, tasks: &deletingTasks)
check("deleting a top-level project leaves tasks without one", deletingTasks[1].projectID == nil)

print("Completed by date in a project")
let history: [TaskItem] = [
    TaskItem(title: "Open", projectID: work.id, sortOrder: 1),
    TaskItem(title: "Done today", status: .done, projectID: work.id, sortOrder: 2, completedAt: now),
    TaskItem(title: "Done earlier", status: .done, projectID: work.id, sortOrder: 3, completedAt: date(2026, 10, 15, 9)),
    TaskItem(title: "Done earlier later", status: .done, projectID: work.id, sortOrder: 4, completedAt: date(2026, 10, 15, 18)),
    TaskItem(title: "Other project", status: .done, projectID: UUID(), sortOrder: 5, completedAt: now)
]
func historyTitles(_ query: TaskQuery) -> [String] { query.run(on: history, projects: [work], now: now, calendar: calendar).map(\.title) }
check("old completions stay out by default", !historyTitles(TaskQuery(scope: .project(work.id))).contains("Done earlier"))
let withHistory = TaskQuery(scope: .project(work.id), includesCompletedHistory: true).run(on: history, projects: [work], now: now, calendar: calendar)
check("history brings them in, for this project only", withHistory.count == 4 && !withHistory.contains { $0.title == "Other project" })
check("hide completed also hides history", TaskQuery(scope: .project(work.id), showCompleted: false, includesCompletedHistory: true).run(on: history, projects: [work], now: now, calendar: calendar).map(\.title) == ["Open"])
let historySections = TaskSection.make(for: .project(work.id), tasks: withHistory, now: now, calendar: calendar)
check("open first, then one section a day, newest first", historySections.map(\.isCompletedHistory) == [false, true, true], "\(historySections.map(\.title))")
check("sections say what they are", historySections[1].title == "Completed · Today", "\(String(describing: historySections[1].title))")
check("newest first inside a day", historySections[2].tasks.map(\.title) == ["Done earlier later", "Done earlier"])
check("a project with nothing finished stays one section", TaskSection.make(for: .project(work.id), tasks: [history[0]], now: now, calendar: calendar).map(\.id) == ["all"])
check("everything finished has no empty open section", TaskSection.make(for: .project(work.id), tasks: [history[1]], now: now, calendar: calendar).map(\.isCompletedHistory) == [true])

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

print("Data folder")
let dataFolder = FileManager.default.temporaryDirectory.appendingPathComponent("TodoCoreChecks-\(UUID().uuidString)")
do {
    let fm = FileManager.default
    check("dated folder name", DatedFolder.name(for: date(2026, 3, 7, 23, 59), calendar: calendar) == "2026-03-07")
    check("dated names", DatedFolder.isDatedName("2026-03-07") && !DatedFolder.isDatedName("notes") && !DatedFolder.isDatedName("2026-3-7") && !DatedFolder.isDatedName("ABCD-EF-GH"))

    let dataStore = DataFileStore(directory: dataFolder)
    let nothingYet = try dataStore.backUpDataFile(now: now, calendar: calendar)
    check("nothing to back up yet", nothingYet == nil)
    try dataStore.save(AppData(tasks: [TaskItem(title: "First")]))
    let backup = try dataStore.backUpDataFile(now: now, calendar: calendar)
    check("backup lands in a dated folder", backup?.path.hasSuffix("Backups/2026-10-23/tasks.json") == true, backup?.path ?? "nil")
    try dataStore.save(AppData(tasks: [TaskItem(title: "Second")]))
    let second = try dataStore.backUpDataFile(now: now, calendar: calendar)
    check("only one backup a day", second == nil)
    let kept = try DataFileStore(directory: backup!.deletingLastPathComponent()).load()
    check("the day's backup keeps the morning's data", kept?.tasks.first?.title == "First")
    let nextDay = try dataStore.backUpDataFile(now: date(2026, 10, 24, 9), calendar: calendar)
    check("the next day gets its own", nextDay != nil)

    let files = dataFolder.appendingPathComponent("Attachments")
    func touch(_ path: String) throws {
        let url = files.appendingPathComponent(path)
        try fm.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        try Data("x".utf8).write(to: url)
    }
    func present(_ path: String) -> Bool { fm.fileExists(atPath: files.appendingPathComponent(path).path) }
    try touch("old-kept.pdf"); try touch("old-unused.pdf")
    try touch("2026-10-23/kept.pdf"); try touch("2026-10-23/unused.pdf")
    try touch("2026-10-22/unused.pdf"); try touch("2026-10-21/.hidden")
    AttachmentFiles.removeUnreferenced(in: files, keeping: ["old-kept.pdf", "2026-10-23/kept.pdf"])
    check("cleanup keeps files still in use, flat and dated", present("old-kept.pdf") && present("2026-10-23/kept.pdf"))
    check("cleanup removes unused files, flat and dated", !present("old-unused.pdf") && !present("2026-10-23/unused.pdf"))
    check("cleanup removes emptied folders only", !present("2026-10-22") && present("2026-10-23") && present("2026-10-21/.hidden"))
} catch {
    check("data folder threw \(error)", false)
}
try? FileManager.default.removeItem(at: dataFolder)

print("")
if failures == 0 {
    print("All \(checks) checks passed.")
} else {
    print("\(failures) of \(checks) checks failed.")
    exit(1)
}
