import AppKit
import Foundation
import os
import TodoCore
import UserNotifications

/// Mirrors task reminders into macOS notifications.
///
/// Rather than tracking individual edits, `sync` rebuilds the pending set from the
/// current tasks whenever something reminder-related changed. Notifications are
/// delivered by the system, so they arrive even when the app isn't running.
final class ReminderScheduler: NSObject, UNUserNotificationCenterDelegate {
    /// Called on the main thread when the user clicks a reminder.
    var onOpenTask: ((UUID) -> Void)?

    private static let identifierPrefix = "task-reminder-"
    /// macOS keeps a limited number of pending requests per app; stay well under it.
    private static let maximumPending = 60

    private var lastFingerprint: [String] = []
    /// Identifiers this session has scheduled, so stale ones can be removed without
    /// asking the notification center (an async round-trip that could race the next sync).
    private var scheduledIDs: Set<String> = []
    private var latest: (tasks: [TaskItem], projects: [Project])?
    private var hasRequestedAuthorization = false
    private let logger = Logger(subsystem: "local.todoapp.TodoApp", category: "reminders")

    /// `nil` when running outside an app bundle (e.g. straight from the build folder),
    /// where the notification center is unavailable.
    private var center: UNUserNotificationCenter? {
        Bundle.main.bundleIdentifier == nil ? nil : UNUserNotificationCenter.current()
    }

    func activate() {
        guard let center else { return }
        center.delegate = self
        // Start clean; the first sync schedules whatever is still due.
        center.removeAllPendingNotificationRequests()
    }

    func requestAuthorizationIfNeeded() {
        guard let center, !hasRequestedAuthorization else { return }
        hasRequestedAuthorization = true
        center.requestAuthorization(options: [.alert, .sound, .badge]) { [weak self, logger] granted, error in
            if let error {
                logger.error("Notification permission failed: \(error.localizedDescription, privacy: .public)")
            } else if !granted {
                logger.notice("Notifications are turned off for TodoApp in System Settings.")
            }
            guard granted else { return }
            // Anything scheduled while the permission prompt was up may have been dropped.
            DispatchQueue.main.async {
                guard let self, let latest = self.latest else { return }
                self.lastFingerprint = []
                self.sync(tasks: latest.tasks, projects: latest.projects)
            }
        }
    }

    func sync(tasks: [TaskItem], projects: [Project]) {
        guard let center else { return }
        latest = (tasks, projects)
        let now = Date()
        let upcoming = tasks
            .compactMap { task -> (task: TaskItem, fireDate: Date)? in
                guard !task.isCompleted, let fireDate = task.reminderDate(), fireDate > now else { return nil }
                return (task, fireDate)
            }
            .sorted { $0.fireDate < $1.fireDate }
            .prefix(Self.maximumPending)

        let projectNames = Dictionary(projects.map { ($0.id, $0.name) }, uniquingKeysWith: { first, _ in first })
        let fingerprint = upcoming.map { entry -> String in
            let project = entry.task.projectID.flatMap { projectNames[$0] } ?? ""
            return "\(entry.task.id)|\(entry.fireDate.timeIntervalSince1970)|\(entry.task.title)|\(project)|\(entry.task.dueDate?.timeIntervalSince1970 ?? 0)"
        }
        guard fingerprint != lastFingerprint else { return }
        lastFingerprint = fingerprint
        if !upcoming.isEmpty { requestAuthorizationIfNeeded() }

        // All calls happen here, in order, on the main thread. Adding a request with an
        // existing identifier replaces it, so only reminders that went away need removing.
        let desired = Set(upcoming.map { Self.identifierPrefix + $0.task.id.uuidString })
        let stale = scheduledIDs.subtracting(desired)
        if !stale.isEmpty { center.removePendingNotificationRequests(withIdentifiers: Array(stale)) }
        scheduledIDs = desired

        for (task, fireDate) in upcoming {
            let content = UNMutableNotificationContent()
            content.title = task.title.isEmpty ? "Untitled task" : task.title
            content.body = Self.body(for: task, firing: fireDate, projectName: task.projectID.flatMap { projectNames[$0] })
            content.sound = .default
            content.userInfo = ["taskID": task.id.uuidString]

            let components = Calendar.current.dateComponents([.year, .month, .day, .hour, .minute, .second], from: fireDate)
            let request = UNNotificationRequest(
                identifier: Self.identifierPrefix + task.id.uuidString,
                content: content,
                trigger: UNCalendarNotificationTrigger(dateMatching: components, repeats: false)
            )
            center.add(request) { [logger] error in
                if let error {
                    logger.error("Couldn't schedule reminder: \(error.localizedDescription, privacy: .public)")
                }
            }
        }
    }

    /// Words like "today" are worked out from when the reminder appears, not when it was scheduled.
    private static func body(for task: TaskItem, firing fireDate: Date, projectName: String?) -> String {
        var parts: [String] = []
        if let due = task.dueDate {
            let label = DueDateText.label(for: due, hasTime: task.hasDueTime, now: fireDate)
            parts.append("Due \(label.lowercasedFirstWordIfRelative)")
        }
        if let projectName { parts.append(projectName) }
        return parts.joined(separator: " · ")
    }

    // MARK: - UNUserNotificationCenterDelegate

    /// Show reminders as banners even while the app is frontmost.
    func userNotificationCenter(
        _ center: UNUserNotificationCenter,
        willPresent notification: UNNotification,
        withCompletionHandler completionHandler: @escaping (UNNotificationPresentationOptions) -> Void
    ) {
        completionHandler([.banner, .sound, .list])
    }

    func userNotificationCenter(
        _ center: UNUserNotificationCenter,
        didReceive response: UNNotificationResponse,
        withCompletionHandler completionHandler: @escaping () -> Void
    ) {
        let idString = response.notification.request.content.userInfo["taskID"] as? String
        DispatchQueue.main.async { [weak self] in
            if let idString, let id = UUID(uuidString: idString) {
                NSApp.activate(ignoringOtherApps: true)
                self?.onOpenTask?(id)
            }
            completionHandler()
        }
    }
}

private extension String {
    /// "Today, 4:00 PM" reads as "due today, 4:00 PM"; month names stay capitalised.
    var lowercasedFirstWordIfRelative: String {
        for word in ["Today", "Tomorrow", "Yesterday"] where hasPrefix(word) {
            return word.lowercased() + dropFirst(word.count)
        }
        return self
    }
}
