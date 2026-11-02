import AppKit
import Combine
import Foundation
import os
import TodoCore

/// The single source of truth for tasks and projects.
///
/// Main-thread only. Every mutation goes through a method here so that saving,
/// reminder scheduling and undo stay in one place. Changes are written to disk
/// automatically, a moment after the last edit.
final class TaskStore: ObservableObject {
    /// Something the user needs to know that a log line wouldn't tell them.
    struct Alert {
        let title: String
        let message: String
    }

    @Published private(set) var tasks: [TaskItem] = []
    @Published private(set) var projects: [Project] = []
    /// Shown once to the user, then cleared.
    @Published var alert: Alert?

    /// The window's undo manager, handed over by the root view.
    weak var undoManager: UndoManager?

    let reminders: ReminderScheduler
    let attachments: AttachmentStorage

    private let fileStore: DataFileStore
    /// The one folder that holds everything: tasks, backups and attachments.
    var dataDirectory: URL { fileStore.directory }
    /// False when saving would overwrite a file that must be kept: one that couldn't be moved
    /// aside, or one from a newer version of the app.
    private var canSave = true
    /// One alert per run of failures, not one per save.
    private var saveFailureReported = false
    private var pendingSave: DispatchWorkItem?
    private var terminationObserver: NSObjectProtocol?
    private let logger = Logger(subsystem: "local.todoapp.TodoApp", category: "store")

    /// How long backups are kept, and how long a file attached but no longer used stays around.
    private static let retentionDays = 30

    /// `remindersEnabled` is off when trying the app on another data folder, because
    /// notifications belong to the app as a whole, not to one folder.
    init(directory: URL = DataFileStore.defaultDirectory, remindersEnabled: Bool = true) {
        fileStore = DataFileStore(directory: directory)
        reminders = ReminderScheduler(enabled: remindersEnabled)
        attachments = AttachmentStorage(directory: directory.appendingPathComponent("Attachments", isDirectory: true))
        // Activate first: it clears old pending reminders, which loading then schedules afresh.
        reminders.activate()
        load()
        terminationObserver = NotificationCenter.default.addObserver(
            forName: NSApplication.willTerminateNotification, object: nil, queue: .main
        ) { [weak self] _ in
            self?.saveNow()
        }
    }

    // MARK: - Lookup

    func task(with id: UUID?) -> TaskItem? {
        guard let id else { return nil }
        return tasks.first { $0.id == id }
    }

    func project(with id: UUID?) -> Project? {
        guard let id else { return nil }
        return projects.first { $0.id == id }
    }

    // MARK: - Tasks

    @discardableResult
    func createTask(
        title: String,
        projectID: UUID? = nil,
        dueDate: Date? = nil,
        status: TaskStatus = .todo,
        priority: Priority = Priority.none
    ) -> TaskItem {
        let task = TaskItem(
            title: title,
            status: status,
            priority: priority,
            projectID: projectID,
            dueDate: dueDate.map { Calendar.current.startOfDay(for: $0) },
            sortOrder: (tasks.map(\.sortOrder).max() ?? 0) + 1,
            completedAt: status == .done ? Date() : nil
        )
        undoable("New Task") {
            tasks.append(task)
        }
        return task
    }

    /// Applies `change` to one task. Pass an `undoName` for discrete edits (priority,
    /// dates…); leave it `nil` for typing, which text fields already make undoable.
    func updateTask(_ id: UUID, undoName: String? = nil, _ change: (inout TaskItem) -> Void) {
        guard let index = tasks.firstIndex(where: { $0.id == id }) else { return }
        var task = tasks[index]
        change(&task)
        guard task != tasks[index] else { return }
        if let undoName {
            undoable(undoName) { tasks[index] = task }
        } else {
            tasks[index] = task
            didChange()
        }
    }

    func updateTasks(_ ids: [UUID], undoName: String, _ change: (inout TaskItem) -> Void) {
        let targets = Set(ids)
        undoable(undoName) {
            for index in tasks.indices where targets.contains(tasks[index].id) {
                change(&tasks[index])
            }
        }
    }

    func setStatus(_ ids: [UUID], to status: TaskStatus) {
        let targets = Set(ids)
        let name = status == .done ? "Complete Task" : "Change Status"
        undoable(name) {
            TaskOperations.setStatus(status, for: targets, in: &tasks)
        }
    }

    func toggleCompleted(_ id: UUID) {
        guard let task = task(with: id) else { return }
        setStatus([id], to: task.isCompleted ? .todo : .done)
    }

    @discardableResult
    func duplicateTask(_ id: UUID) -> TaskItem? {
        guard let index = tasks.firstIndex(where: { $0.id == id }) else { return nil }
        let original = tasks[index]
        var copy = original.duplicated()
        let following = tasks.map(\.sortOrder).filter { $0 > original.sortOrder }.min()
        copy.sortOrder = following.map { (original.sortOrder + $0) / 2 } ?? original.sortOrder + 1
        undoable("Duplicate Task") {
            tasks.insert(copy, at: index + 1)
        }
        return copy
    }

    /// Removes a task made from a "+" that never got any content. Not undoable: it's
    /// tidying up after an abandoned draft, not an edit the user made.
    func discardIfBlank(_ id: UUID) {
        guard let index = tasks.firstIndex(where: { $0.id == id }) else { return }
        guard tasks[index].isBlank else { return }
        tasks.remove(at: index)
        didChange()
    }

    func deleteTasks(_ ids: Set<UUID>) {
        guard tasks.contains(where: { ids.contains($0.id) }) else { return }
        undoable(ids.count == 1 ? "Delete Task" : "Delete Tasks") {
            tasks.removeAll { ids.contains($0.id) }
        }
    }

    /// Places `id` directly before `targetID` within `ordered` (the list as the user sees it).
    func moveTask(_ id: UUID, before targetID: UUID, in ordered: [TaskItem]) {
        guard TaskOperations.sortOrder(placing: id, before: targetID, in: ordered) != nil else { return }
        undoable("Move Task") {
            TaskOperations.move(id, before: targetID, in: ordered, tasks: &tasks)
        }
    }

    // MARK: - Attachments

    /// Copies in the background, because attachments can be large enough to freeze the window.
    func attachFiles(_ urls: [URL], to taskID: UUID) {
        let files = urls.filter(\.isFileURL)
        guard !files.isEmpty else { return }
        let storage = attachments
        DispatchQueue.global(qos: .userInitiated).async { [weak self] in
            var added: [Attachment] = []
            var failed: [(name: String, reason: String)] = []
            for url in files {
                do {
                    added.append(try storage.importFile(at: url))
                } catch {
                    failed.append((url.lastPathComponent, error.localizedDescription))
                }
            }
            DispatchQueue.main.async {
                guard let self else { return }
                if !added.isEmpty {
                    self.updateTask(taskID, undoName: "Add Attachment") { $0.attachments.append(contentsOf: added) }
                }
                for failure in failed {
                    self.logger.error("Couldn't attach \(failure.name, privacy: .public): \(failure.reason, privacy: .public)")
                }
                if !failed.isEmpty {
                    self.show("Couldn’t attach a file", "\(failed.map(\.name).joined(separator: ", ")) couldn’t be copied. \(failed[0].reason)")
                }
            }
        }
    }

    /// Returns `false` when the text isn't a usable web or mail address.
    @discardableResult
    func attachLink(_ text: String, to taskID: UUID) -> Bool {
        guard let attachment = AttachmentLink.attachment(from: text) else { return false }
        updateTask(taskID, undoName: "Add Link") { $0.attachments.append(attachment) }
        return true
    }

    func removeAttachment(_ attachmentID: UUID, from taskID: UUID) {
        updateTask(taskID, undoName: "Remove Attachment") { task in
            task.attachments.removeAll { $0.id == attachmentID }
        }
    }

    // MARK: - Projects

    @discardableResult
    func createProject(name: String, icon: String, color: ProjectColor, parentID: UUID? = nil) -> Project {
        let project = Project(name: name, icon: icon, color: color, parentID: parentID)
        undoable("New Project") {
            projects.append(project)
        }
        return project
    }

    func updateProject(_ id: UUID, _ change: (inout Project) -> Void) {
        guard let index = projects.firstIndex(where: { $0.id == id }) else { return }
        var project = projects[index]
        change(&project)
        guard project != projects[index] else { return }
        undoable("Edit Project") { projects[index] = project }
    }

    /// Deletes the project; what's under it moves up rather than disappearing.
    func deleteProject(_ id: UUID) {
        undoable("Delete Project") {
            TaskOperations.deleteProject(id, projects: &projects, tasks: &tasks)
        }
    }

    func moveProject(_ id: UUID, before targetID: UUID) {
        var reordered = projects
        guard reordered.moveProject(id, before: targetID) else { return }
        undoable("Move Project") { projects = reordered }
    }

    // MARK: - Undo

    private struct Snapshot {
        var tasks: [TaskItem]
        var projects: [Project]
    }

    /// Runs `body`, records the previous state for undo, and saves.
    private func undoable(_ actionName: String, _ body: () -> Void) {
        let before = Snapshot(tasks: tasks, projects: projects)
        body()
        registerRestore(of: before, actionName: actionName)
        didChange()
    }

    private func registerRestore(of snapshot: Snapshot, actionName: String) {
        guard let undoManager else { return }
        undoManager.registerUndo(withTarget: self) { store in
            let current = Snapshot(tasks: store.tasks, projects: store.projects)
            let live = Dictionary(store.tasks.map { ($0.id, $0) }, uniquingKeysWith: { first, _ in first })
            // A blank task that's gone is a discarded draft, not something to bring back.
            store.tasks = snapshot.tasks
                .filter { !$0.isBlank || live[$0.id] != nil }
                .map { Self.keepingText(of: live[$0.id], in: $0) }
            store.projects = snapshot.projects
            store.registerRestore(of: current, actionName: actionName)
            store.didChange()
        }
        undoManager.setActionName(actionName)
    }

    /// Typing has its own undo in the text system, so restoring a snapshot keeps the
    /// current wording of tasks that still exist rather than rolling it back too.
    private static func keepingText(of live: TaskItem?, in restored: TaskItem) -> TaskItem {
        guard let live else { return restored }
        var task = restored
        task.title = live.title
        task.notes = live.notes
        let liveTitles = Dictionary(live.subtasks.map { ($0.id, $0.title) }, uniquingKeysWith: { first, _ in first })
        for index in task.subtasks.indices {
            if let title = liveTitles[task.subtasks[index].id] { task.subtasks[index].title = title }
        }
        return task
    }

    // MARK: - Persistence

    private func load() {
        let saved: AppData?
        do {
            saved = try fileStore.load()
        } catch DataFileError.newerFormat(let version) {
            logger.error("Data file has newer version \(version, privacy: .public)")
            canSave = false
            show(
                "Couldn’t open your tasks",
                "Your tasks were saved by a newer version of TodoApp. To protect them, changes won’t be saved here. "
                    + "Please update TodoApp. The file is tasks.json in \(folderDescription)."
            )
            return
        } catch {
            logger.error("Couldn't read saved tasks: \(error.localizedDescription, privacy: .public)")
            recoverFromUnreadableFile()
            return
        }

        guard let saved else {
            let sample = AppData.sample()
            tasks = sample.tasks
            projects = sample.projects
            saveNow()
            // No attachment cleanup: with no data file there's no telling what is still needed.
            return
        }
        // A blank task here is a draft left behind when the app quit.
        tasks = saved.tasks.filter { !$0.isBlank }
        projects = saved.projects
        reminders.sync(tasks: tasks, projects: projects)
        // Cleaning up only at launch keeps undo of a removed attachment safe for the whole session.
        if backUpIfNewDay() { removeUnusedAttachments() }
    }

    /// Makes today's backup if there isn't one yet, from the file as it stands. Returns whether it did.
    @discardableResult
    private func backUpIfNewDay() -> Bool {
        do {
            guard try fileStore.backUpDataFile() != nil else { return false }
            fileStore.pruneBackups(keeping: Self.retentionDays)
            return true
        } catch {
            logger.error("Couldn't back up tasks: \(error.localizedDescription, privacy: .public)")
            return false
        }
    }

    /// Removes stored files nothing needs any more: not the data file, not a backup, and not
    /// attached recently. When what's needed can't be established, removes nothing.
    private func removeUnusedAttachments() {
        guard let referenced = fileStore.referencedAttachmentPaths() else { return }
        let cutoff = Calendar.current.date(byAdding: .day, value: -Self.retentionDays, to: Date()) ?? Date()
        attachments.removeFiles(notIn: referenced, keepingFoldersFrom: DatedFolder.name(for: cutoff))
    }

    private var folderDescription: String {
        (fileStore.directory.path as NSString).abbreviatingWithTildeInPath
    }

    /// Keeps whatever is already showing: the first problem is usually the one that matters.
    private func show(_ title: String, _ message: String) {
        if alert == nil { alert = Alert(title: title, message: message) }
    }

    /// Sets the unreadable file aside so a fresh list can be saved. If even that fails,
    /// saving is switched off for the session so the original is never overwritten.
    private func recoverFromUnreadableFile() {
        let backups = "Copies from recent days are in its Backups folder."
        do {
            let moved = try fileStore.quarantineDataFile()
            show(
                "Couldn’t open your tasks",
                "Your saved tasks couldn’t be read, so a fresh list was started. "
                    + "The original file was kept as “\(moved.lastPathComponent)” in \(folderDescription). \(backups)"
            )
        } catch {
            logger.error("Couldn't move the unreadable file aside: \(error.localizedDescription, privacy: .public)")
            canSave = false
            show(
                "Couldn’t open your tasks",
                "Your saved tasks couldn’t be read, and the file couldn’t be set aside. "
                    + "To protect it, changes won’t be saved until TodoApp is restarted. "
                    + "The file is tasks.json in \(folderDescription). \(backups)"
            )
        }
    }

    /// Asks for notification permission when the user sets a reminder, and says so if it's off.
    func confirmRemindersAllowed() {
        reminders.requestAuthorizationIfNeeded { [weak self] in
            self?.show("Reminders are turned off", "Allow notifications for TodoApp in System Settings ▸ Notifications, or this reminder won’t appear.")
        }
    }

    private func didChange() {
        pendingSave?.cancel()
        let work = DispatchWorkItem { [weak self] in self?.saveNow() }
        pendingSave = work
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.4, execute: work)
    }

    func saveNow() {
        pendingSave?.cancel()
        pendingSave = nil
        if canSave {
            // Before writing, so the backup holds the data as the day began, even if the app
            // has been open since yesterday.
            backUpIfNewDay()
            do {
                try fileStore.save(AppData(projects: projects, tasks: tasks))
                saveFailureReported = false
            } catch {
                logger.error("Couldn't save tasks: \(error.localizedDescription, privacy: .public)")
                if !saveFailureReported {
                    saveFailureReported = true
                    show("Couldn’t save your tasks", "Recent changes aren’t on disk yet. TodoApp will keep trying as you make changes. \(error.localizedDescription)")
                }
            }
        }
        reminders.sync(tasks: tasks, projects: projects)
    }
}
