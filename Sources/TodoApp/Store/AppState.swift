import AppKit
import Combine
import Foundation
import SwiftUI
import TodoCore

enum SidebarItem: Hashable {
    case allTasks
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

/// The shortcuts offered wherever a due date can be set.
enum QuickDue {
    case today, tomorrow, nextWeek

    var day: Date {
        let calendar = Calendar.current
        let today = calendar.startOfDay(for: Date())
        switch self {
        case .today: return today
        case .tomorrow: return calendar.date(byAdding: .day, value: 1, to: today) ?? today
        case .nextWeek: return DueDateText.nextWeek(calendar: calendar)
        }
    }
}

struct ProjectEditorRequest: Identifiable {
    let id = UUID()
    /// `nil` creates a new project.
    let projectID: UUID?
    /// Where a new project goes; `nil` for the top level.
    var parentID: UUID?
}

/// What the window is showing and the user actions that span several views.
/// Main-thread only.
final class AppState: ObservableObject {
    let store: TaskStore

    @Published var sidebarSelection: SidebarItem = .today {
        didSet { defaults.set(sidebarSelection.storageKey, forKey: Keys.selection) }
    }
    @Published var selectedTaskID: UUID? {
        didSet {
            defaults.set(selectedTaskID?.uuidString, forKey: Keys.selectedTask)
            // Moving on from an untouched draft throws it away instead of leaving "Untitled" behind.
            if let draft = draftTaskID, draft != selectedTaskID {
                draftTaskID = nil
                store.discardIfBlank(draft)
            }
        }
    }
    /// A blank task created by "+" in the board or calendar, until it gets a title.
    private var draftTaskID: UUID?
    /// What's typed in the search field. Not published: the field keeps its own copy, so typing
    /// doesn't redraw the window.
    private(set) var searchText = ""
    /// What the tasks are filtered by: `searchText` once typing pauses, so each keystroke
    /// doesn't rebuild the list. Clearing applies at once.
    @Published private(set) var activeSearchText = ""
    /// Bumped when the search is cleared from outside the field, so the field can catch up.
    @Published private(set) var searchClearCount = 0
    private var pendingSearch: DispatchWorkItem?
    /// Bumped when the selected task should be scrolled into view: keyboard moves and reminders.
    /// A click needs no scrolling, and scrolling on every change cost a layout of the whole list.
    @Published private(set) var selectionScrollRequest = 0
    @Published var priorityFilter: Set<Priority> {
        didSet { defaults.set(priorityFilter.map(\.rawValue), forKey: Keys.priorityFilter) }
    }
    @Published var isSidebarVisible: Bool {
        didSet { defaults.set(isSidebarVisible, forKey: Keys.sidebar) }
    }
    /// What the user switched off in Settings.
    @Published private(set) var hiddenElements: Set<WorkspaceElement> {
        didSet { defaults.set(hiddenElements.map(\.rawValue), forKey: Keys.hidden) }
    }
    /// Projects whose sub-projects are folded away in the sidebar.
    @Published private(set) var collapsedProjects: Set<UUID> {
        didSet { defaults.set(collapsedProjects.map(\.uuidString), forKey: Keys.collapsed) }
    }
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
    private var projectWatcher: AnyCancellable?

    private let defaults = UserDefaults.standard
    private enum Keys {
        static let viewMode = "viewMode"
        static let sort = "sort"
        static let showCompleted = "showCompleted"
        static let inspector = "inspectorVisible"
        static let appearance = "appearance"
        static let selection = "sidebarSelection"
        static let selectedTask = "selectedTask"
        static let sidebar = "sidebarVisible"
        static let priorityFilter = "priorityFilter"
        static let hidden = "hiddenElements"
        static let collapsed = "collapsedProjects"
    }

    init(store: TaskStore) {
        self.store = store
        viewMode = defaults.string(forKey: Keys.viewMode).flatMap(ViewMode.init(rawValue:)) ?? .list
        sort = defaults.string(forKey: Keys.sort).flatMap(TaskSort.init(rawValue:)) ?? .manual
        showCompleted = defaults.object(forKey: Keys.showCompleted) as? Bool ?? true
        isInspectorVisible = defaults.object(forKey: Keys.inspector) as? Bool ?? true
        appearance = defaults.string(forKey: Keys.appearance).flatMap(AppearancePreference.init(rawValue:)) ?? .system
        isSidebarVisible = defaults.object(forKey: Keys.sidebar) as? Bool ?? true
        priorityFilter = Set((defaults.array(forKey: Keys.priorityFilter) as? [Int] ?? []).compactMap(Priority.init(rawValue:)))
        hiddenElements = Set((defaults.stringArray(forKey: Keys.hidden) ?? []).compactMap(WorkspaceElement.init(rawValue:)))
        collapsedProjects = Set((defaults.stringArray(forKey: Keys.collapsed) ?? []).compactMap(UUID.init(uuidString:)))
        NSApplication.shared.appearance = appearance.nsAppearance
        restoreWorkspace()
        store.reminders.onOpenTask = { [weak self] id in self?.reveal(id) }
        startClock()
        // Undo can take away the project on screen. On the next turn, so the store has settled.
        // Titles show project names, so a renamed project redraws what depends on this state.
        projectWatcher = store.$projects
            .receive(on: DispatchQueue.main)
            .sink { [weak self] _ in
                self?.objectWillChange.send()
                self?.leaveMissingProject()
            }
    }

    /// From the search field, as the user types.
    func searchFieldChanged(_ text: String) {
        guard text != searchText else { return }
        searchText = text
        scheduleSearch()
    }

    /// Ends the search and empties the field.
    func clearSearch() {
        guard !searchText.isEmpty || !activeSearchText.isEmpty else { return }
        searchText = ""
        scheduleSearch()
        searchClearCount += 1
    }

    private func scheduleSearch() {
        pendingSearch?.cancel()
        pendingSearch = nil
        let text = searchText
        guard !text.trimmingCharacters(in: .whitespaces).isEmpty else {
            if activeSearchText != text { activeSearchText = text }
            return
        }
        let work = DispatchWorkItem { [weak self] in self?.activeSearchText = text }
        pendingSearch = work
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.15, execute: work)
    }

    /// Back to where the user left off, as far as that place and task still exist.
    private func restoreWorkspace() {
        let savedTask = defaults.string(forKey: Keys.selectedTask).flatMap(UUID.init(uuidString:))
        if let saved = defaults.string(forKey: Keys.selection).flatMap(SidebarItem.init(storageKey:)) {
            if case .project(let id) = saved, store.project(with: id) == nil {
                sidebarSelection = .today
            } else {
                sidebarSelection = saved
            }
        }
        leaveHiddenPlaces()
        let visible = visibleTasks
        selectedTaskID = visible.contains { $0.id == savedTask } ? savedTask : visible.first?.id
    }

    /// "Today", overdue colouring and counts are worked out when views draw, so redraw when
    /// the day changes, after sleep, and once a minute for tasks with a time of day.
    private func startClock() {
        let refresh: (Notification) -> Void = { [weak self] _ in self?.clockDidTick() }
        for name in [Notification.Name.NSCalendarDayChanged, .NSSystemTimeZoneDidChange] {
            clockObservers.append(NotificationCenter.default.addObserver(forName: name, object: nil, queue: .main, using: refresh))
        }
        clockObservers.append(NSWorkspace.shared.notificationCenter.addObserver(
            forName: NSWorkspace.didWakeNotification, object: nil, queue: .main, using: refresh
        ))
        let timer = Timer(timeInterval: 60, repeats: true) { [weak self] _ in self?.clockDidTick() }
        timer.tolerance = 10
        RunLoop.main.add(timer, forMode: .common)
        minuteTimer = timer
    }

    private func clockDidTick() {
        objectWillChange.send()
        store.reminders.resync()
    }

    private func leaveMissingProject() {
        if case .project(let id) = sidebarSelection, store.project(with: id) == nil {
            select(homeForNewTasks)
        }
    }

    // MARK: - Derived

    var isSearching: Bool { !activeSearchText.trimmingCharacters(in: .whitespaces).isEmpty }

    var isDashboard: Bool { sidebarSelection == .dashboard && !isSearching }

    var scope: TaskScope {
        if isSearching { return .all }
        switch sidebarSelection {
        case .allTasks: return .allOpen
        case .today: return .today
        case .upcoming: return .upcoming
        case .completed: return .completed
        case .dashboard: return .all
        case .project(let id): return .projects(store.projects.subtreeIDs(of: id))
        }
    }

    var query: TaskQuery {
        TaskQuery(
            scope: scope,
            searchText: activeSearchText,
            priorities: priorityFilter,
            showCompleted: showCompleted || isSearching,
            sort: sort,
            includesCompletedHistory: viewMode == .list
        )
    }

    /// The tasks the center column lists, in display order.
    var visibleTasks: [TaskItem] {
        query.run(on: store.tasks, projects: store.projects)
    }

    /// What the header counts and measures: without a project's finished tasks from earlier days.
    var currentTasks: [TaskItem] {
        var current = query
        current.includesCompletedHistory = false
        return current.run(on: store.tasks, projects: store.projects)
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
        if case .projects = calendarQuery.scope {} else { calendarQuery.scope = .all }
        calendarQuery.showCompleted = showCompleted
        return calendarQuery.run(on: store.tasks, projects: store.projects).filter { $0.dueDate != nil }
    }

    var title: String {
        if isSearching { return "Search" }
        switch sidebarSelection {
        case .allTasks: return "All Tasks"
        case .today: return "Today"
        case .upcoming: return "Upcoming"
        case .completed: return "Completed"
        case .dashboard: return "Dashboard"
        case .project(let id): return store.project(with: id)?.name ?? "Project"
        }
    }

    var windowTitle: String { "Tasks — \(title)" }

    var selectedTask: TaskItem? { store.task(with: selectedTaskID) }

    // MARK: - Customising

    func shows(_ element: WorkspaceElement) -> Bool { !hiddenElements.contains(element) }

    var visibleViewModes: [ViewMode] { ViewMode.allCases.filter { shows($0.element) } }

    /// The one view left can't be switched off: the window would have nothing to show.
    func canHide(_ element: WorkspaceElement) -> Bool {
        element.group != .views || !shows(element) || visibleViewModes.count > 1
    }

    func setVisible(_ visible: Bool, _ element: WorkspaceElement) {
        guard visible || canHide(element) else { return }
        if visible { hiddenElements.remove(element) } else { hiddenElements.insert(element) }
        leaveHiddenPlaces()
    }

    func showEverything() {
        hiddenElements = []
    }

    /// Steps off a view or sidebar place that has just been hidden.
    private func leaveHiddenPlaces() {
        if !shows(viewMode.element), let mode = visibleViewModes.first { viewMode = mode }
        guard !shows(sidebarSelection.element) else { return }
        let places: [SidebarItem] = [.today, .allTasks, .upcoming, .completed, .dashboard]
        if let place = places.first(where: { shows($0.element) }) {
            select(place)
        } else if shows(.projects), let first = store.projects.outline().first {
            select(.project(first.id))
        }
    }

    func toggleCollapsed(_ projectID: UUID) {
        if collapsedProjects.remove(projectID) == nil { collapsedProjects.insert(projectID) }
    }

    func showDataFolder() {
        NSWorkspace.shared.open(store.dataDirectory)
    }

    func openSettings() {
        NSApp.activate(ignoringOtherApps: true)
        // macOS 14 ignores showSettingsWindow: sent from code, but the menu item always works.
        if let menu = NSApp.mainMenu?.items.first?.submenu,
           let index = menu.items.firstIndex(where: { $0.keyEquivalent == "," }) {
            menu.performActionForItem(at: index)
        } else {
            NSApp.sendAction(Selector(("showSettingsWindow:")), to: nil, from: nil)
        }
    }

    /// Where new tasks go from a place that can't list them, such as the Dashboard.
    private var homeForNewTasks: SidebarItem {
        if shows(.allTasks) { return .allTasks }
        return shows(.today) ? .today : .allTasks
    }

    /// Deletes a project. If it's open, moves on to its parent, or to the general list.
    func deleteProject(_ project: Project) {
        if sidebarSelection == .project(project.id) {
            if let parent = project.parentID, store.project(with: parent) != nil {
                select(.project(parent))
            } else {
                select(homeForNewTasks)
            }
        }
        store.deleteProject(project.id)
    }

    // MARK: - Navigation

    /// For shortcuts into a place, such as a dashboard tile: a place that's been hidden stays hidden.
    func selectIfShown(_ item: SidebarItem) {
        if shows(item.element) { select(item) }
    }

    func select(_ item: SidebarItem) {
        clearSearch()
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
        clearSearch()
        // Best place first, skipping any the user has hidden.
        var places: [SidebarItem] = []
        if TaskQuery.belongs(task, to: .today, now: Date(), calendar: .current) { places.append(.today) }
        if let projectID = task.projectID, store.project(with: projectID) != nil { places.append(.project(projectID)) }
        places.append(task.isCompleted ? .completed : (task.dueDate == nil ? .allTasks : .upcoming))
        if !task.isCompleted { places.append(.allTasks) }
        if let place = places.first(where: { shows($0.element) }) { sidebarSelection = place }
        selectedTaskID = id
        isInspectorVisible = true
        selectionScrollRequest += 1
    }

    func moveSelection(by offset: Int) {
        let ids = navigationOrder.map(\.id)
        guard !ids.isEmpty else { return }
        defer { selectionScrollRequest += 1 }
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
        clearSearch()
        if sidebarSelection == .dashboard || sidebarSelection == .completed {
            sidebarSelection = homeForNewTasks
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

    func setDue(_ due: QuickDue, for ids: [UUID]) {
        setDueDay(due.day, for: ids)
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
        case .allTasks, .dashboard: return false
        case .today: setDueDay(today, for: ids)
        case .upcoming: setDueDay(Calendar.current.date(byAdding: .day, value: 1, to: today), for: ids)
        case .completed: store.setStatus(ids, to: .done)
        case .project(let id): move(ids, toProject: id)
        }
        return true
    }

    /// Tasks dropped on a row in the list: reorder them in front of it.
    func handleDrop(_ items: [String], before targetID: UUID, in ordered: [TaskItem]) -> Bool {
        let ids = Self.taskIDs(from: items)
        guard let id = ids.first, id != targetID else { return false }
        place(id, before: targetID, in: ordered)
        return true
    }

    /// Placing a task by hand only means something in manual order, so that's what it switches to.
    private func place(_ id: UUID, before targetID: UUID, in ordered: [TaskItem]) {
        var ordering = ordered
        if sort != .manual {
            sort = .manual
            ordering = ordered.sorted { $0.sortOrder < $1.sortOrder }
        }
        store.moveTask(id, before: targetID, in: ordering)
    }

    /// Cards dropped on a board column (optionally in front of a card).
    func handleDrop(_ items: [String], status: TaskStatus, before targetID: UUID? = nil, in ordered: [TaskItem] = []) -> Bool {
        let ids = Self.taskIDs(from: items)
        guard !ids.isEmpty else { return false }
        store.setStatus(ids, to: status)
        if let targetID, let id = ids.first {
            place(id, before: targetID, in: ordered)
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
