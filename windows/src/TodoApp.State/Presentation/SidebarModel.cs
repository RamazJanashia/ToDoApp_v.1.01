using TodoCore;

namespace TodoApp.State;

/// <summary>One sidebar line. <see cref="Count"/> is null when there is nothing to show (zero, or a row without a counter).</summary>
/// <param name="Depth">How many levels this project sits under others.</param>
/// <param name="IsCollapsed">Null for a row with nothing under it.</param>
public sealed record SidebarRowModel(
    SidebarItem Item,
    string Icon,
    ProjectColor? Tint,
    string Title,
    int? Count,
    bool IsSelected,
    int Depth = 0,
    bool? IsCollapsed = null)
{
    /// <summary>All Tasks lists everything and the Dashboard lists nothing, so tasks can't be filed there.</summary>
    public bool AcceptsDrops => Item.Kind is not (SidebarKind.Dashboard or SidebarKind.AllTasks);
}

/// <param name="ShowsWorkspaceHeader">False when every place above the projects has been hidden.</param>
/// <param name="ShowsProjects">False when the projects have been hidden in Settings.</param>
public sealed record SidebarModel(
    IReadOnlyList<SidebarRowModel> Workspace,
    IReadOnlyList<SidebarRowModel> Projects,
    bool ShowsEmptyProjectsHint,
    bool ShowsWorkspaceHeader,
    bool ShowsProjects)
{
    public static SidebarModel Build(AppState s)
    {
        ArgumentNullException.ThrowIfNull(s);
        var now = s.Clock.UtcNow;
        var calendar = s.Clock.Calendar;
        var tasks = s.Store.Tasks;

        int? Count(TaskScope scope)
        {
            var open = TaskQuery.OpenCount(scope, tasks, now, calendar);
            return open > 0 ? open : null;
        }

        SidebarRowModel Row(SidebarItem item, string icon, ProjectColor? tint, string title, TaskScope? scope) =>
            new(item, icon, tint, title, scope is { } value ? Count(value) : null, s.SidebarSelection == item && !s.IsSearching);

        var workspace = new[]
            {
                Row(SidebarItem.AllTasks, "tray", null, "All Tasks", TaskScope.AllOpen),
                Row(SidebarItem.Today, "sun.max", null, "Today", TaskScope.Today),
                Row(SidebarItem.Upcoming, "calendar", null, "Upcoming", TaskScope.Upcoming),
                Row(SidebarItem.Completed, "checkmark.circle", null, "Completed", null),
                Row(SidebarItem.Dashboard, "square.grid.2x2", null, "Dashboard", null),
            }
            .Where(row => s.Shows(row.Item.Element()))
            .ToList();

        var showsProjects = s.Shows(WorkspaceElement.Projects);
        var all = s.Store.Projects;
        var projects = showsProjects
            ? all.Outline(s.CollapsedProjects)
                .Select(row => Row(
                        SidebarItem.Project(row.Id),
                        row.Project.Icon,
                        row.Project.Color,
                        row.Project.Name,
                        TaskScope.Projects(all.SubtreeIds(row.Id))) with
                    {
                        Depth = row.Depth,
                        IsCollapsed = row.HasChildren ? s.CollapsedProjects.Contains(row.Id) : null,
                    })
                .ToList()
            : new List<SidebarRowModel>();

        return new SidebarModel(workspace, projects, showsProjects && all.Count == 0, workspace.Count > 0, showsProjects);
    }
}
