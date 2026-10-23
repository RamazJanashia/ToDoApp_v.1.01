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
    @Published private(set) var tasks: [TaskItem] = []
    @Published private(set) var projects: [Project] = []
    /// Set when the saved file couldn't be read at launch; shown once to the user.
    @Published var loadIssue: String?

    /// The window's undo manager, handed over by the root view.
    weak var undoManager: UndoManager?

    let reminders = ReminderScheduler()
    let attachments: AttachmentStorage

    private let fileStore: DataFileStore
    /// False when an unreadable data file couldn't be moved aside: saving would overwrite it.
    private var canSave = true
    private var pendingSave: DispatchWorkItem?
    private var terminationObserver: NSObjectProtocol?
    private let logger = Logger(subsystem: "local.todoapp.TodoApp", category: "store")

    init(directory: URL = DataFileStore.defaultDirectory) {
        fileStore = DataFileStore(directory: directory)
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
        var copy = original
        copy.id = UUID()
        copy.createdAt = Date()
        copy.subtasks = original.subtasks.map { Subtask(title: $0.title, isDone: $0.isDone) }
        copy.attachments = original.attachments.map { Attachment(kind: $0.kind, name: $0.name, location: $0.location) }
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
        let task = tasks[index]
        guard task.title.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty,
              task.notes.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty,
              task.subtasks.isEmpty, task.attachments.isEmpty else { return }
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
        guard let newOrder = TaskOperations.sortOrder(placing: id, before: targetID, in: ordered) else { return }
        updateTask(id, undoName: "Move Task") { $0.sortOrder = newOrder }
    }

    // MARK: - Attachments

    func attachFiles(_ urls: [URL], to taskID: UUID) {
        var added: [Attachment] = []
        for url in urls where url.isFileURL {
            do {
                added.append(try attachments.importFile(at: url))
            } catch {
                logger.error("Couldn't attach \(url.lastPathComponent, privacy: .public): \(error.localizedDescription, privacy: .public)")
            }
        }
        guard !added.isEmpty else { return }
        updateTask(taskID, undoName: "Add Attachment") { $0.attachments.append(contentsOf: added) }
    }

    /// Returns `false` when the text isn't a usable web address.
    @discardableResult
    func attachLink(_ text: String, to taskID: UUID) -> Bool {
        guard let attachment = AttachmentStorage.link(from: text) else { return false }
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
    func createProject(name: String, icon: String, color: ProjectColor) -> Project {
        let project = Project(name: name, icon: icon, color: color)
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

    /// Deletes the project; its tasks move to the Inbox rather than disappearing.
    func deleteProject(_ id: UUID) {
        undoable("Delete Project") {
            projects.removeAll { $0.id == id }
            for index in tasks.indices where tasks[index].projectID == id {
                tasks[index].projectID = nil
            }
        }
    }

    func moveProject(_ id: UUID, before targetID: UUID) {
        guard id != targetID,
              let from = projects.firstIndex(where: { $0.id == id }) else { return }
        undoable("Move Project") {
            let project = projects.remove(at: from)
            let to = projects.firstIndex(where: { $0.id == targetID }) ?? projects.endIndex
            projects.insert(project, at: to)
        }
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
            store.tasks = snapshot.tasks.map { Self.keepingText(of: live[$0.id], in: $0) }
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
        } catch {
            logger.error("Couldn't read saved tasks: \(error.localizedDescription, privacy: .public)")
            recoverFromUnreadableFile()
            // No attachment cleanup here: the unreadable file may still refer to every attachment.
            return
        }

        if let saved {
            tasks = saved.tasks
            projects = saved.projects
            reminders.sync(tasks: tasks, projects: projects)
        } else {
            let sample = AppData.sample()
            tasks = sample.tasks
            projects = sample.projects
            saveNow()
        }
        let referenced = Set(tasks.flatMap(\.attachments).filter { $0.kind == .file }.map(\.location))
        attachments.removeFiles(notIn: referenced)
    }

    /// Sets the unreadable file aside so a fresh list can be saved. If even that fails,
    /// saving is switched off for the session so the original is never overwritten.
    private func recoverFromUnreadableFile() {
        let folder = "~/Library/Application Support/TodoApp"
        do {
            let moved = try fileStore.quarantineDataFile()
            loadIssue = "Your saved tasks couldn’t be read, so a fresh list was started. "
                + "The original file was kept as “\(moved.lastPathComponent)” in \(folder)."
        } catch {
            logger.error("Couldn't move the unreadable file aside: \(error.localizedDescription, privacy: .public)")
            canSave = false
            loadIssue = "Your saved tasks couldn’t be read, and the file couldn’t be set aside. "
                + "To protect it, changes won’t be saved until TodoApp is restarted. "
                + "The file is tasks.json in \(folder)."
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
        guard canSave else { return }
        do {
            try fileStore.save(AppData(projects: projects, tasks: tasks))
        } catch {
            logger.error("Couldn't save tasks: \(error.localizedDescription, privacy: .public)")
        }
        reminders.sync(tasks: tasks, projects: projects)
    }
}
