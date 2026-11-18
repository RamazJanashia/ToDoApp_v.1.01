import Foundation

/// Everything a person can switch off in Settings. Hiding only removes it from view: no data changes.
enum WorkspaceElement: String, CaseIterable, Identifiable {
    case allTasks, today, upcoming, completed, dashboard, projects
    case listView, boardView, calendarView
    case rowPriority, rowProgress, rowProject, rowDueDate, rowRepeat, rowReminder, rowSubtasks, rowAttachments, rowNotes
    case detailDueDate, detailReminder, detailPriority, detailProject, detailRepeat, detailStatus
    case detailNotes, detailSubtasks, detailAttachments
    case dashboardProgress, dashboardMetrics, dashboardActivity, dashboardProjects, dashboardUpNext, dashboardPriority
    case headerProgress, toolbarSearch, toolbarAppearance

    var id: String { rawValue }

    enum Group: String, CaseIterable, Identifiable {
        case sidebar = "Sidebar"
        case views = "Views"
        case rows = "Task rows"
        case details = "Task details"
        case dashboard = "Dashboard"
        case window = "Window"

        var id: String { rawValue }

        var elements: [WorkspaceElement] { WorkspaceElement.allCases.filter { $0.group == self } }
    }

    var group: Group {
        switch self {
        case .allTasks, .today, .upcoming, .completed, .dashboard, .projects: return .sidebar
        case .listView, .boardView, .calendarView: return .views
        case .rowPriority, .rowProgress, .rowProject, .rowDueDate, .rowRepeat, .rowReminder, .rowSubtasks, .rowAttachments, .rowNotes: return .rows
        case .detailDueDate, .detailReminder, .detailPriority, .detailProject, .detailRepeat, .detailStatus,
             .detailNotes, .detailSubtasks, .detailAttachments: return .details
        case .dashboardProgress, .dashboardMetrics, .dashboardActivity, .dashboardProjects, .dashboardUpNext, .dashboardPriority: return .dashboard
        case .headerProgress, .toolbarSearch, .toolbarAppearance: return .window
        }
    }

    var title: String {
        switch self {
        case .allTasks: return "All Tasks"
        case .today: return "Today"
        case .upcoming: return "Upcoming"
        case .completed: return "Completed"
        case .dashboard: return "Dashboard"
        case .projects: return "Projects"
        case .listView: return "List"
        case .boardView: return "Board"
        case .calendarView: return "Calendar"
        case .rowPriority: return "Priority flag"
        case .rowProgress: return "Subtask progress bar"
        case .rowProject: return "Project name"
        case .rowDueDate: return "Due date"
        case .rowRepeat: return "Repeat icon"
        case .rowReminder: return "Reminder icon"
        case .rowSubtasks: return "Subtask count"
        case .rowAttachments: return "Attachment count"
        case .rowNotes: return "Notes icon"
        case .detailDueDate: return "Due date"
        case .detailReminder: return "Reminder"
        case .detailPriority: return "Priority"
        case .detailProject: return "Project"
        case .detailRepeat: return "Repeat"
        case .detailStatus: return "Status"
        case .detailNotes: return "Notes"
        case .detailSubtasks: return "Subtasks"
        case .detailAttachments: return "Attachments"
        case .dashboardProgress: return "Overall progress"
        case .dashboardMetrics: return "Number tiles"
        case .dashboardActivity: return "Activity chart"
        case .dashboardProjects: return "Projects"
        case .dashboardUpNext: return "Up next"
        case .dashboardPriority: return "Priority breakdown"
        case .headerProgress: return "Progress bar above the list"
        case .toolbarSearch: return "Search field"
        case .toolbarAppearance: return "Appearance button"
        }
    }
}

extension SidebarItem {
    /// How the selection is remembered between launches.
    var storageKey: String {
        switch self {
        case .allTasks: return "allTasks"
        case .today: return "today"
        case .upcoming: return "upcoming"
        case .completed: return "completed"
        case .dashboard: return "dashboard"
        case .project(let id): return "project:\(id.uuidString)"
        }
    }

    init?(storageKey: String) {
        switch storageKey {
        case "allTasks": self = .allTasks
        case "today": self = .today
        case "upcoming": self = .upcoming
        case "completed": self = .completed
        case "dashboard": self = .dashboard
        default:
            guard storageKey.hasPrefix("project:"),
                  let id = UUID(uuidString: String(storageKey.dropFirst("project:".count))) else { return nil }
            self = .project(id)
        }
    }

    var element: WorkspaceElement {
        switch self {
        case .allTasks: return .allTasks
        case .today: return .today
        case .upcoming: return .upcoming
        case .completed: return .completed
        case .dashboard: return .dashboard
        case .project: return .projects
        }
    }
}

extension ViewMode {
    var element: WorkspaceElement {
        switch self {
        case .list: return .listView
        case .board: return .boardView
        case .calendar: return .calendarView
        }
    }
}
