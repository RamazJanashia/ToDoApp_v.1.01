namespace TodoApp.State;

/// <summary>Everything a person can switch off in Settings. Hiding only removes it from view: no data changes.</summary>
public enum WorkspaceElement
{
    AllTasks, Today, Upcoming, Completed, Dashboard, Projects,
    ListView, BoardView, CalendarView,
    RowPriority, RowProgress, RowProject, RowDueDate, RowRepeat, RowReminder, RowSubtasks, RowAttachments, RowNotes,
    DetailDueDate, DetailReminder, DetailPriority, DetailProject, DetailRepeat, DetailStatus,
    DetailNotes, DetailSubtasks, DetailAttachments,
    DashboardProgress, DashboardMetrics, DashboardActivity, DashboardProjects, DashboardUpNext, DashboardPriority,
    HeaderProgress, ToolbarSearch, ToolbarAppearance,
}

public enum WorkspaceGroup { Sidebar, Views, Rows, Details, Dashboard, Window }

public static class WorkspaceElements
{
    private static readonly Dictionary<string, WorkspaceElement> ByRawValue =
        Enum.GetValues<WorkspaceElement>().ToDictionary(RawValue, StringComparer.Ordinal);

    /// <summary>The Mac's raw value ("rowPriority"), which settings.json stores.</summary>
    public static string RawValue(this WorkspaceElement element)
    {
        var name = element.ToString();
        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    public static WorkspaceElement? Parse(string rawValue) =>
        ByRawValue.TryGetValue(rawValue, out var element) ? element : null;

    public static WorkspaceGroup Group(this WorkspaceElement element) => element switch
    {
        <= WorkspaceElement.Projects => WorkspaceGroup.Sidebar,
        <= WorkspaceElement.CalendarView => WorkspaceGroup.Views,
        <= WorkspaceElement.RowNotes => WorkspaceGroup.Rows,
        <= WorkspaceElement.DetailAttachments => WorkspaceGroup.Details,
        <= WorkspaceElement.DashboardPriority => WorkspaceGroup.Dashboard,
        _ => WorkspaceGroup.Window,
    };

    public static IReadOnlyList<WorkspaceElement> Elements(this WorkspaceGroup group) =>
        Enum.GetValues<WorkspaceElement>().Where(e => e.Group() == group).ToList();

    public static string Title(this WorkspaceGroup group) => group switch
    {
        WorkspaceGroup.Sidebar => "Sidebar",
        WorkspaceGroup.Views => "Views",
        WorkspaceGroup.Rows => "Task rows",
        WorkspaceGroup.Details => "Task details",
        WorkspaceGroup.Dashboard => "Dashboard",
        WorkspaceGroup.Window => "Window",
        _ => throw new ArgumentOutOfRangeException(nameof(group), group, null),
    };

    public static string Title(this WorkspaceElement element) => element switch
    {
        WorkspaceElement.AllTasks => "All Tasks",
        WorkspaceElement.Today => "Today",
        WorkspaceElement.Upcoming => "Upcoming",
        WorkspaceElement.Completed => "Completed",
        WorkspaceElement.Dashboard => "Dashboard",
        WorkspaceElement.Projects => "Projects",
        WorkspaceElement.ListView => "List",
        WorkspaceElement.BoardView => "Board",
        WorkspaceElement.CalendarView => "Calendar",
        WorkspaceElement.RowPriority => "Priority flag",
        WorkspaceElement.RowProgress => "Subtask progress bar",
        WorkspaceElement.RowProject => "Project name",
        WorkspaceElement.RowDueDate => "Due date",
        WorkspaceElement.RowRepeat => "Repeat icon",
        WorkspaceElement.RowReminder => "Reminder icon",
        WorkspaceElement.RowSubtasks => "Subtask count",
        WorkspaceElement.RowAttachments => "Attachment count",
        WorkspaceElement.RowNotes => "Notes icon",
        WorkspaceElement.DetailDueDate => "Due date",
        WorkspaceElement.DetailReminder => "Reminder",
        WorkspaceElement.DetailPriority => "Priority",
        WorkspaceElement.DetailProject => "Project",
        WorkspaceElement.DetailRepeat => "Repeat",
        WorkspaceElement.DetailStatus => "Status",
        WorkspaceElement.DetailNotes => "Notes",
        WorkspaceElement.DetailSubtasks => "Subtasks",
        WorkspaceElement.DetailAttachments => "Attachments",
        WorkspaceElement.DashboardProgress => "Overall progress",
        WorkspaceElement.DashboardMetrics => "Number tiles",
        WorkspaceElement.DashboardActivity => "Activity chart",
        WorkspaceElement.DashboardProjects => "Projects",
        WorkspaceElement.DashboardUpNext => "Up next",
        WorkspaceElement.DashboardPriority => "Priority breakdown",
        WorkspaceElement.HeaderProgress => "Progress bar above the list",
        WorkspaceElement.ToolbarSearch => "Search field",
        WorkspaceElement.ToolbarAppearance => "Appearance button",
        _ => throw new ArgumentOutOfRangeException(nameof(element), element, null),
    };

    public static WorkspaceElement Element(this ViewMode mode) => mode switch
    {
        ViewMode.List => WorkspaceElement.ListView,
        ViewMode.Board => WorkspaceElement.BoardView,
        ViewMode.Calendar => WorkspaceElement.CalendarView,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };
}

public static class SidebarItems
{
    private const string ProjectPrefix = "project:";

    /// <summary>How the selection is remembered between launches; the Mac's keys.</summary>
    public static string StorageKey(this SidebarItem item) => item.Kind switch
    {
        SidebarKind.AllTasks => "allTasks",
        SidebarKind.Today => "today",
        SidebarKind.Upcoming => "upcoming",
        SidebarKind.Completed => "completed",
        SidebarKind.Dashboard => "dashboard",
        SidebarKind.Project => ProjectPrefix + item.ProjectId!.Value.ToString("D").ToUpperInvariant(),
        _ => throw new ArgumentOutOfRangeException(nameof(item), item, null),
    };

    public static SidebarItem? FromStorageKey(string key) => key switch
    {
        "allTasks" => SidebarItem.AllTasks,
        "today" => SidebarItem.Today,
        "upcoming" => SidebarItem.Upcoming,
        "completed" => SidebarItem.Completed,
        "dashboard" => SidebarItem.Dashboard,
        _ when key.StartsWith(ProjectPrefix, StringComparison.Ordinal)
            && Guid.TryParseExact(key[ProjectPrefix.Length..], "D", out var id) => SidebarItem.Project(id),
        _ => null,
    };

    public static WorkspaceElement Element(this SidebarItem item) => item.Kind switch
    {
        SidebarKind.AllTasks => WorkspaceElement.AllTasks,
        SidebarKind.Today => WorkspaceElement.Today,
        SidebarKind.Upcoming => WorkspaceElement.Upcoming,
        SidebarKind.Completed => WorkspaceElement.Completed,
        SidebarKind.Dashboard => WorkspaceElement.Dashboard,
        SidebarKind.Project => WorkspaceElement.Projects,
        _ => throw new ArgumentOutOfRangeException(nameof(item), item, null),
    };
}
