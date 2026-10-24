using TodoCore;

namespace TodoApp.State;

/// <summary>One sidebar line. <see cref="Count"/> is null when there is nothing to show (zero, or a row without a counter).</summary>
public sealed record SidebarRowModel(SidebarItem Item, string Icon, ProjectColor? Tint, string Title, int? Count, bool IsSelected);

public sealed record SidebarModel(IReadOnlyList<SidebarRowModel> Workspace, IReadOnlyList<SidebarRowModel> Projects, bool ShowsEmptyProjectsHint)
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
            Row(SidebarItem.Inbox, "tray", null, "Inbox", TaskScope.Inbox),
            Row(SidebarItem.Today, "sun.max", null, "Today", TaskScope.Today),
            Row(SidebarItem.Upcoming, "calendar", null, "Upcoming", TaskScope.Upcoming),
            Row(SidebarItem.Completed, "checkmark.circle", null, "Completed", null),
            Row(SidebarItem.Dashboard, "square.grid.2x2", null, "Dashboard", null),
        };

        var projects = s.Store.Projects
            .Select(p => Row(SidebarItem.Project(p.Id), p.Icon, p.Color, p.Name, TaskScope.Project(p.Id)))
            .ToList();

        return new SidebarModel(workspace, projects, projects.Count == 0);
    }
}
