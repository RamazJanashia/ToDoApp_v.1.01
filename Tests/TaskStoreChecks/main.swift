import Foundation
import TodoCore

// Checks for the store and the launch sequence, compiled together with the app's own
// Store and Services files by `./build.sh --check`. Runs unbundled, so reminders stay off.

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

let fm = FileManager.default

func freshFolder() -> URL {
    let url = fm.temporaryDirectory.appendingPathComponent("TaskStoreChecks-\(UUID().uuidString)")
    try! fm.createDirectory(at: url, withIntermediateDirectories: true)
    return url
}

func put(_ text: String, at path: String, in folder: URL) {
    let url = folder.appendingPathComponent(path)
    try! fm.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
    try! Data(text.utf8).write(to: url)
}

func exists(_ path: String, in folder: URL) -> Bool {
    fm.fileExists(atPath: folder.appendingPathComponent(path).path)
}

func open(_ folder: URL) -> TaskStore { TaskStore(directory: folder, remindersEnabled: false) }

/// Lets queued main-thread and background work finish.
func spin(until done: () -> Bool) {
    let deadline = Date().addingTimeInterval(5)
    while !done() && Date() < deadline {
        RunLoop.main.run(until: Date().addingTimeInterval(0.02))
    }
}

let oldFile = "2020-01-01/AAAA-contract.pdf"

print("Attachments survive a bad start")
do {
    let folder = freshFolder()
    put("pdf", at: "Attachments/\(oldFile)", in: folder)
    put("not json", at: "tasks.json", in: folder)
    let first = open(folder)
    check("a corrupt file is reported", first.alert != nil)
    check("the alert points at the backups", first.alert?.message.contains("Backups") == true, first.alert?.message ?? "")
    check("the first launch keeps the file", exists("Attachments/\(oldFile)", in: folder))
    let second = open(folder)
    check("a second launch with nothing saved keeps it too", exists("Attachments/\(oldFile)", in: folder))
    second.createTask(title: "New")
    second.saveNow()
    // Stands in for the next day: cleanup only runs on the launch that makes a new backup.
    try! fm.removeItem(at: folder.appendingPathComponent("Backups"))
    _ = open(folder)
    check("that launch did reach cleanup", exists("Backups/\(DatedFolder.name(for: Date()))/tasks.json", in: folder))
    check("so does a later launch with a fresh list", exists("Attachments/\(oldFile)", in: folder))
}
do {
    let folder = freshFolder()
    put("pdf", at: "Attachments/\(oldFile)", in: folder)
    _ = open(folder)
    check("a missing data file doesn't wipe attachments", exists("Attachments/\(oldFile)", in: folder))
    check("the sample list was saved", exists("tasks.json", in: folder))
}

print("Cleaning up")
do {
    let folder = freshFolder()
    let data = AppData(tasks: [TaskItem(title: "x", attachments: [Attachment(kind: .file, name: "kept.pdf", location: "2020-01-01/kept.pdf")])])
    try! DataFileStore(directory: folder).save(data)
    put("pdf", at: "Attachments/2020-01-01/kept.pdf", in: folder)
    put("pdf", at: "Attachments/2020-01-01/orphan.pdf", in: folder)
    let recent = DatedFolder.name(for: Date())
    put("pdf", at: "Attachments/\(recent)/just-added.pdf", in: folder)
    let backedUp = AppData(tasks: [TaskItem(title: "y", attachments: [Attachment(kind: .file, name: "b.pdf", location: "2020-01-01/in-backup.pdf")])])
    try! DataFileStore(directory: folder.appendingPathComponent("Backups/2021-01-01")).save(backedUp)
    put("pdf", at: "Attachments/2020-01-01/in-backup.pdf", in: folder)
    _ = open(folder)
    check("referenced files stay", exists("Attachments/2020-01-01/kept.pdf", in: folder))
    check("files a backup needs stay", exists("Attachments/2020-01-01/in-backup.pdf", in: folder))
    check("recent files stay", exists("Attachments/\(recent)/just-added.pdf", in: folder))
    check("true orphans go", !exists("Attachments/2020-01-01/orphan.pdf", in: folder))
}

print("Newer file format")
do {
    let folder = freshFolder()
    let future = #"{"version":99,"projects":[],"tasks":[]}"#
    put(future, at: "tasks.json", in: folder)
    put("pdf", at: "Attachments/\(oldFile)", in: folder)
    let store = open(folder)
    check("it's reported", store.alert?.message.contains("newer") == true, store.alert?.message ?? "")
    store.createTask(title: "Nope")
    store.saveNow()
    check("it isn't overwritten or moved", (try? String(contentsOf: folder.appendingPathComponent("tasks.json"))) == future)
    check("attachments are left alone", exists("Attachments/\(oldFile)", in: folder))
}

print("Saving")
do {
    let folder = freshFolder()
    let store = open(folder)
    check("no alert when all is well", store.alert == nil)
    try! fm.setAttributes([.posixPermissions: 0o500], ofItemAtPath: folder.path)
    store.createTask(title: "Unsaved")
    store.saveNow()
    check("a failed save is reported", store.alert?.title.contains("save") == true, store.alert?.title ?? "nil")
    store.alert = nil
    store.saveNow()
    check("repeated failures don't nag", store.alert == nil)
    try! fm.setAttributes([.posixPermissions: 0o700], ofItemAtPath: folder.path)
    store.saveNow()
    check("the next save succeeds", (try? DataFileStore(directory: folder).load())?.tasks.contains { $0.title == "Unsaved" } == true)
    try! fm.setAttributes([.posixPermissions: 0o500], ofItemAtPath: folder.path)
    store.createTask(title: "Unsaved again")
    store.saveNow()
    check("a new failure after a success is reported again", store.alert != nil)
    try! fm.setAttributes([.posixPermissions: 0o700], ofItemAtPath: folder.path)
}

print("Backups during a long session")
do {
    let folder = freshFolder()
    let store = open(folder)
    let today = DatedFolder.name(for: Date())
    let backup = "Backups/\(today)/tasks.json"
    store.createTask(title: "Before midnight")
    store.saveNow()
    // Stands in for the date changing while the app stays open: today has no backup yet.
    try? fm.removeItem(at: folder.appendingPathComponent("Backups/\(today)"))
    store.createTask(title: "After midnight")
    store.saveNow()
    let kept = try? DataFileStore(directory: folder.appendingPathComponent("Backups/\(today)")).load()
    check("the first save of a day makes that day's backup", exists(backup, in: folder))
    check("it holds the data as the day began", kept?.tasks.contains { $0.title == "Before midnight" } == true && kept?.tasks.contains { $0.title == "After midnight" } == false)
}

print("Drafts")
do {
    let folder = freshFolder()
    try! DataFileStore(directory: folder).save(AppData(tasks: [TaskItem(title: ""), TaskItem(title: "Real")]))
    check("a blank draft left by a quit is tidied away", open(folder).tasks.map(\.title) == ["Real"])

    let store = open(freshFolder())
    let undo = UndoManager()
    undo.groupsByEvent = false
    store.undoManager = undo
    undo.beginUndoGrouping()
    let draft = store.createTask(title: "")
    undo.endUndoGrouping()
    undo.undo()
    check("undo removes the draft", !store.tasks.contains { $0.id == draft.id })
    undo.redo()
    check("redo doesn't bring a blank draft back", !store.tasks.contains { $0.id == draft.id })
}

print("Store helpers")
do {
    let store = open(freshFolder())
    let task = store.createTask(title: "Repeat", dueDate: Date())
    store.updateTask(task.id) { $0.recurrence = .daily }
    store.setStatus([task.id], to: .done)
    let copy = store.duplicateTask(task.id)
    check("duplicating drops the occurrence link", copy?.nextOccurrenceID == nil)
}

print("Attaching files")
do {
    let folder = freshFolder()
    let store = open(folder)
    let task = store.createTask(title: "With file")
    let source = folder.appendingPathComponent("report.pdf")
    try! Data("pdf".utf8).write(to: source)
    store.attachFiles([source], to: task.id)
    spin { store.task(with: task.id)?.attachments.isEmpty == false }
    let added = store.task(with: task.id)?.attachments.first
    check("a file is copied in", added.flatMap { store.attachments.url(for: $0) }.map { fm.fileExists(atPath: $0.path) } == true)
    check("no error for a good file", store.alert == nil)

    store.attachFiles([folder.appendingPathComponent("missing.pdf")], to: task.id)
    spin { store.alert != nil }
    check("a failed copy is reported", store.alert?.message.contains("missing.pdf") == true, store.alert?.message ?? "nil")
    check("a failed copy adds nothing", store.task(with: task.id)?.attachments.count == 1)

    let hostile = Attachment(kind: .file, name: "x", location: "../../../../../../Applications/Calculator.app")
    check("a path outside the folder has no URL", store.attachments.url(for: hostile) == nil)
    check("nor can it be previewed", store.attachments.thumbnail(for: hostile) == nil)
    check("a file link has no URL", store.attachments.url(for: Attachment(kind: .link, name: "x", location: "file:///etc/passwd")) == nil)
    check("a web link has one", store.attachments.url(for: Attachment(kind: .link, name: "x", location: "https://a.b")) != nil)
}

print("")
if failures == 0 {
    print("All \(checks) store checks passed.")
} else {
    print("\(failures) of \(checks) store checks failed.")
    exit(1)
}
