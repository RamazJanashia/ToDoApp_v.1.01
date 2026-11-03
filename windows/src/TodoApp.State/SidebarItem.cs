namespace TodoApp.State;

public enum SidebarKind
{
    AllTasks,
    Today,
    Upcoming,
    Completed,
    Dashboard,
    Project,
}

/// <summary>A place in the sidebar. <see cref="ProjectId"/> is set only for <see cref="SidebarKind.Project"/>.</summary>
public readonly record struct SidebarItem(SidebarKind Kind, Guid? ProjectId)
{
    public static SidebarItem AllTasks => new(SidebarKind.AllTasks, null);

    public static SidebarItem Today => new(SidebarKind.Today, null);

    public static SidebarItem Upcoming => new(SidebarKind.Upcoming, null);

    public static SidebarItem Completed => new(SidebarKind.Completed, null);

    public static SidebarItem Dashboard => new(SidebarKind.Dashboard, null);

    public static SidebarItem Project(Guid id) => new(SidebarKind.Project, id);
}
