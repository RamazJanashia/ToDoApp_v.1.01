import AppKit
import Foundation
import SwiftUI
import TodoCore

enum SidebarItem: Hashable {
    case inbox
    case today
    case upcoming
    case completed
    case dashboard
    case project(UUID)
}

enum ViewMode: String, CaseIterable, Identifiable {
    case list
    case board
    case calendar

    var id: String { rawValue }

    var title: String {
        switch self {
        case .list: return "List"
        case .board: return "Board"
        case .calendar: return "Calendar"
        }
    }

    var icon: String {
        switch self {
        case .list: return "list.bullet"
        case .board: return "rectangle.split.3x1"
        case .calendar: return "calendar"
        }
    }
}

struct ProjectEditorRequest: Identifiable {
    let id = UUID()
    /// `nil` creates a new project.
    let projectID: UUID?
}

/// What the window is showing and the user actions that span several views.
/// Main-thread only.
final class AppState: ObservableObject {
    let store: TaskStore

    @Published var sidebarSelection: SidebarItem = .today
    @Published var selectedTaskID: UUID? {
        didSet {
            // Moving on from an untouched draft throws it away instead of leaving "Untitled" behind.
            if let draft = draftTaskID, draft != selectedTaskID {
                draftTaskID = nil
                store.discardIfBlank(draft)
            }
        }
    }
    /// A blank task created by "+" in the board or calendar, until it gets a title.
    private var draftTaskID: UUID?
    @Published var searchText = ""
    @Published var priorityFilter: Set<Priority> = []
    @Published var isSidebarVisible = true
    @Published var calendarMonth = Date()
    @Published var calendarSelectedDay: Date?
    @Published var projectEditor: ProjectEditorRequest?
    @Published var projectPendingDeletion: Project?
    /// The inline "New task" field at the bottom of the list is open.
    @Published var isComposing = false
    /// Bumped to move keyboard focus into the toolbar search field.
    @Published var searchFocusRequest = 0
    /// When set, the inspector focuses this task's title as soon as it shows it.
    @Published var titleFocusRequest: UUID?

    @Published var viewMode: ViewMode {
        didSet { defaults.set(viewMode.rawValue, forKey: Keys.viewMode) }
    }
    @Published var sort: TaskSort {
        didSet { defaults.set(sort.rawValue, forKey: Keys.sort) }
    }
    @Published var showCompleted: Bool {
        didSet { defaults.set(showCompleted, forKey: Keys.showCompleted) }
    }
    @Published var isInspectorVisible: Bool {
        didSet { defaults.set(isInspectorVisible, forKey: Keys.inspector) }
    }
    /// Follows macOS by default; can be pinned to Light or Dark.
    @Published var appearance: AppearancePreference {
        didSet {
            defaults.set(appearance.rawValue, forKey: Keys.appearance)
            NSApplication.shared.appearance = appearance.nsAppearance
        }
    }

    /// The app's main window, used to scope keyboard handling.
    weak var mainWindow: NSWindow?

    private var clockObservers: [NSObjectProtocol] = []
    private var minuteTimer: Timer?

    private let defaults = UserDefaults.standard
    private enum Keys {
        static let viewMode = "viewMode"
        static let sort = "sort"
        static let showCompleted = "showCompleted"
        static let inspector = "inspectorVisible"
        static let appearance = "appearance"
    }

    init(store: TaskStore) {
        self.store = store
        viewMode = defaults.string(forKey: Keys.viewMode).flatMap(ViewMode.init(rawValue:)) ?? .list
        sort = defaults.string(forKey: Keys.sort).flatMap(TaskSort.init(rawValue:)) ?? .manual
        showCompleted = defaults.object(forKey: Keys.showCompleted) as? Bool ?? true
        isInspectorVisible = defaults.object(forKey: Keys.inspector) as? Bool ?? true
        appearance = defaults.string(forKey: Keys.appearance).flatMap(AppearancePreference.init(rawValue:)) ?? .system
        NSApplication.shared.appearance = appearance.nsAppearance
        selectedTaskID = TaskQuery(scope: .today).run(on: store.tasks, projects: store.projects).first?.id
        store.reminders.onOpenTask = { [weak self] id in self?.reveal(id) }
        startClock()
    }

    /// "Today", overdue colouring and counts are worked out when views draw, so redraw when
    /// the day changes, after sleep, and once a minute for tasks with a time of day.
    private func startClock() {
        let refresh: (Notification) -> Void = { [weak self] _ in self?.objectWillChange.send() }
        for name in [Notification.Name.NSCalendarDayChanged, .NSSystemTimeZoneDidChange] {
            clockObservers.append(NotificationCenter.default.addObserver(forName: name, object: nil, queue: .main, using: refresh))
        }
        clockObservers.append(NSWorkspace.shared.notificationCenter.addObserver(
            forName: NSWorkspace.didWakeNotification, object: nil, queue: .main, using: refresh
        ))
        let timer = Timer(timeInterval: 60, repeats: true) { [weak self] _ in self?.objectWillChange.send() }
        timer.tolerance = 10
        RunLoop.main.add(timer, forMode: .common)
        minuteTimer = timer
    }

    // MARK: - Derived

    var isSearching: Bool { !searchText.trimmingCharacters(in: .whitespaces).isEmpty }

    var isDashboard: Bool { sidebarSelection == .dashboard && !isSearching }

    var scope: TaskScope {
        if isSearching { return .all }
        switch sidebarSelection {
        case .inbox: return .inbox
        case .today: return .today
        case .upcoming: return .upcoming
        case .completed: return .completed
        case .dashboard: return .all
        case .project(let id): return .project(id)
        }
    }

    var query: TaskQuery {
        TaskQuery(
            scope: scope,
            searchText: searchText,
            priorities: priorityFilter,
            showCompleted: showCompleted || isSearching,
            sort: sort
        )
    }

    /// The tasks the center column lists, in display order.
    var visibleTasks: [TaskItem] {
        query.run(on: store.tasks, projects: store.projects)
    }

    /// Tasks in the order they appear on screen, for the arrow keys and for choosing
    /// what to select after a delete.
    var navigationOrder: [TaskItem] {
        switch viewMode {
        case .list:
            return TaskSection.make(for: scope, tasks: visibleTasks).flatMap(\.tasks)
        case .board:
            let tasks = visibleTasks
            return TaskStatus.allCases.flatMap { status in tasks.filter { $0.status == status } }
        case .calendar:
            return calendarTasks.sorted { ($0.dueDate ?? .distantPast, $0.sortOrder) < ($1.dueDate ?? .distantPast, $1.sortOrder) }
        }
    }

    /// Scheduled tasks for the calendar: the selected project's, or everyone's.
    var calendarTasks: [TaskItem] {
        var calendarQuery = query
        if case .project = calendarQuery.scope {} else { calendarQuery.scope = .all }
        calendarQuery.showCompleted = showCompleted
        return calendarQuery.run(on: store.tasks, projects: store.projects).filter { $0.dueDate != nil }
    }

    var title: String {
        if isSearching { return "Search" }
        switch sidebarSelection {
        case .inbox: return "Inbox"
        case .today: return "Today"
        case .upcoming: return "Upcoming"
        case .completed: return "Completed"
        case .dashboard: return "Dashboard"
        case .project(let id): return store.project(with: id)?.name ?? "Project"
        }
    }

    var windowTitle: String { "Tasks — \(title)" }

    var selectedTask: TaskItem? { store.task(with: selectedTaskID) }

    // MARK: - Navigation

    func select(_ item: SidebarItem) {
        searchText = ""
        isComposing = false
        sidebarSelection = item
        if let id = selectedTaskID, !visibleTasks.contains(where: { $0.id == id }) {
            selectedTaskID = nil
        }
    }

    /// `endEditing` ends any text editing first, so clicking a row hands the keyboard back to the list.
    func selectTask(_ id: UUID?, endEditing: Bool = false) {
        if endEditing, let window = mainWindow, window.firstResponder is NSTextView {
            window.makeFirstResponder(nil)
        }
        selectedTaskID = id
    }

    /// Shows a task wherever it lives — used when a reminder is clicked.
    func reveal(_ id: UUID) {
        guard let task = store.task(with: id) else { return }
        searchText = ""
        if let projectID = task.projectID, !TaskQuery.belongs(task, to: .today, now: Date(), calendar: .current) {
            sidebarSelection = .project(projectID)
        } else if TaskQuery.belongs(task, to: .today, now: Date(), calendar: .current) {
            sidebarSelection = .today
        } else {
            sidebarSelection = task.isCompleted ? .completed : (task.dueDate == nil ? .inbox : .upcoming)
        }
        selectedTaskID = id
        isInspectorVisible = true
    }

    func moveSelection(by offset: Int) {
        let ids = navigationOrder.map(\.id)
        guard !ids.isEmpty else { return }
        guard let current = selectedTaskID, let index = ids.firstIndex(of: current) else {
            selectedTaskID = offset > 0 ? ids.first : ids.last
            return
        }
        selectedTaskID = ids[min(max(index + offset, 0), ids.count - 1)]
    }

    func focusSearch() {
        searchFocusRequest += 1
    }

    // MARK: - Creating

    /// ⌘N and the "+" buttons: opens the inline field in list view; elsewhere creates
    /// an untitled task and puts the cursor in its title.
    func beginNewTask() {
        searchText = ""
        if sidebarSelection == .dashboard || sidebarSelection == .completed {
            sidebarSelection = .inbox
        }
        if viewMode == .list {
            isComposing = true
        } else {
            createDraftTask()
        }
    }

    /// Creates a blank task and puts the cursor in its title. If the user moves on
    /// without typing anything, the draft is discarded.
    func createDraftTask(dueDate: Date? = nil, status: TaskStatus = .todo) {
        let task = createTask(title: "", dueDate: dueDate, status: status)
        revealInInspector(task.id, focusTitle: true)
        draftTaskID = task.id
    }

    @discardableResult
    func createTask(title: String, dueDate: Date? = nil, status: TaskStatus = .todo) -> TaskItem {
        let defaults = newTaskDefaults()
        return store.createTask(
            title: title,
            projectID: defaults.projectID,
            dueDate: dueDate ?? defaults.dueDate,
            status: status
        )
    }

    /// A click on a task: selects it; a double-click also opens its details.
    func handleClick(on id: UUID) {
        if NSApp.currentEvent?.clickCount == 2 {
            withAnimation(Theme.Motion.standard) { revealInInspector(id, focusTitle: false) }
        } else {
            selectTask(id, endEditing: true)
        }
    }

    func revealInInspector(_ id: UUID, focusTitle: Bool) {
        selectedTaskID = id
        isInspectorVisible = true
        if focusTitle { titleFocusRequest = id }
    }

    /// New tasks land where the user is looking: in the open project, due today in Today, and so on.
    private func newTaskDefaults() -> (projectID: UUID?, dueDate: Date?) {
        let calendar = Calendar.current
        let today = calendar.startOfDay(for: Date())
        var projectID: UUID?
        if case .project(let id) = sidebarSelection { projectID = id }

        if viewMode == .calendar, let day = calendarSelectedDay {
            return (projectID, day)
        }
        switch sidebarSelection {
        case .today: return (nil, today)
        case .upcoming: return (nil, calendar.date(byAdding: .day, value: 1, to: today))
        default: return (projectID, nil)
        }
    }

    // MARK: - Acting on the selection

    func toggleCompleted(_ id: UUID) {
        store.toggleCompleted(id)
    }

    func toggleSelectedCompleted() {
        guard let id = selectedTaskID else { return }
        store.toggleCompleted(id)
    }

    func duplicateSelected() {
        guard let id = selectedTaskID, let copy = store.duplicateTask(id) else { return }
        selectedTaskID = copy.id
    }

    func delete(_ ids: Set<UUID>) {
        if let selected = selectedTaskID, ids.contains(selected) {
            let order = navigationOrder.map(\.id)
            let index = order.firstIndex(of: selected) ?? 0
            let remaining = order.filter { !ids.contains($0) }
            selectedTaskID = remaining.isEmpty ? nil : remaining[min(index, remaining.count - 1)]
        }
        store.deleteTasks(ids)
    }

    func deleteSelected() {
        guard let id = selectedTaskID else { return }
        delete([id])
    }

    /// ⌘⌫ from the menu: inside a text field it keeps its usual meaning.
    func deleteSelectedFromMenu() {
        if let editor = NSApp.keyWindow?.firstResponder as? NSTextView, editor.isEditable {
            editor.deleteToBeginningOfLine(nil)
            return
        }
        deleteSelected()
    }

    func setPriority(_ priority: Priority, for ids: [UUID]) {
        store.updateTasks(ids, undoName: "Set Priority") { $0.priority = priority }
    }

    func setDueDay(_ day: Date?, for ids: [UUID]) {
        store.updateTasks(ids, undoName: day == nil ? "Remove Due Date" : "Set Due Date") { task in
            if let day { task.reschedule(toDay: day) } else { task.clearDueDate() }
        }
    }

    func move(_ ids: [UUID], toProject projectID: UUID?) {
        store.updateTasks(ids, undoName: "Move to Project") { $0.projectID = projectID }
    }

    // MARK: - Drag and drop

    static func taskIDs(from items: [String]) -> [UUID] {
        items.compactMap { UUID(uuidString: $0) }
    }

    /// Tasks dropped on a sidebar row: file them there.
    func handleDrop(_ items: [String], on item: SidebarItem) -> Bool {
        let ids = Self.taskIDs(from: items)
        guard !ids.isEmpty else { return false }
        let today = Calendar.current.startOfDay(for: Date())
        switch item {
        case .inbox: move(ids, toProject: nil)
        case .today: setDueDay(today, for: ids)
        case .upcoming: setDueDay(Calendar.current.date(byAdding: .day, value: 1, to: today), for: ids)
        case .completed: store.setStatus(ids, to: .done)
        case .dashboard: return false
        case .project(let id): move(ids, toProject: id)
        }
        return true
    }

    /// Tasks dropped on a row in the list: reorder them in front of it.
    func handleDrop(_ items: [String], before targetID: UUID, in ordered: [TaskItem]) -> Bool {
        let ids = Self.taskIDs(from: items)
        guard let id = ids.first, id != targetID else { return false }
        var ordering = ordered
        if sort != .manual {
            sort = .manual
            ordering = ordered.sorted { $0.sortOrder < $1.sortOrder }
        }
        store.moveTask(id, before: targetID, in: ordering)
        return true
    }

    /// Cards dropped on a board column (optionally in front of a card).
    func handleDrop(_ items: [String], status: TaskStatus, before targetID: UUID? = nil, in ordered: [TaskItem] = []) -> Bool {
        let ids = Self.taskIDs(from: items)
        guard !ids.isEmpty else { return false }
        store.setStatus(ids, to: status)
        if let targetID, let id = ids.first {
            store.moveTask(id, before: targetID, in: ordered)
        }
        return true
    }

    /// Tasks dropped on a calendar day: reschedule them, keeping any time of day.
    func handleDrop(_ items: [String], onDay day: Date) -> Bool {
        let ids = Self.taskIDs(from: items)
        guard !ids.isEmpty else { return false }
        setDueDay(day, for: ids)
        return true
    }

    // MARK: - Keyboard

    /// Arrow keys, Delete, Return and Escape when no text field is being edited.
    func handleKeyDown(_ event: NSEvent) -> Bool {
        guard let window = event.window, window === mainWindow, window.attachedSheet == nil else { return false }
        if window.firstResponder is NSTextView { return false }
        let modifiers = event.modifierFlags.intersection([.command, .option, .control])
        guard modifiers.isEmpty, !isDashboard else { return false }

        switch event.keyCode {
        case 125: // ↓
            moveSelection(by: 1)
        case 126: // ↑
            moveSelection(by: -1)
        case 51, 117: // Delete, Forward Delete
            guard selectedTaskID != nil else { return false }
            deleteSelected()
        case 36, 76: // Return, Enter
            guard let id = selectedTaskID else { return false }
            revealInInspector(id, focusTitle: true)
        case 49: // Space
            guard selectedTaskID != nil else { return false }
            toggleSelectedCompleted()
        case 53: // Escape
            guard selectedTaskID != nil else { return false }
            selectedTaskID = nil
        default:
            return false
        }
        return true
    }
}
