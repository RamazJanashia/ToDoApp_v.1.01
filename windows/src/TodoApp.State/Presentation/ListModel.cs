using TodoCore;

namespace TodoApp.State;

public sealed record ListSectionModel(string Id, string? Title, string? Detail, bool IsWarning, IReadOnlyList<TaskRowModel> Rows);

public sealed record EmptyStateModel(string Icon, string Title, string? Message);

/// <param name="Sections">The open tasks' sections, above the new-task field.</param>
/// <param name="History">A project's finished tasks, one section a day, below the new-task field.</param>
public sealed record ListModel(
    IReadOnlyList<ListSectionModel> Sections,
    IReadOnlyList<ListSectionModel> History,
    EmptyStateModel? Empty,
    bool CanCompose)
{
    public static ListModel Build(AppState s)
    {
        ArgumentNullException.ThrowIfNull(s);
        var now = s.Clock.UtcNow;
        var calendar = s.Clock.Calendar;
        var scope = s.Scope;
        var tasks = s.VisibleTasks;
        var hidesDueToday = scope.Kind == TaskScopeKind.Today;

        var all = TaskSection.Make(scope, tasks, now, calendar);
        ListSectionModel Model(TaskSection section) => new(
            section.Id,
            section.Title,
            section.Detail,
            section.IsWarning,
            section.Tasks.Select(t => TaskMeta.Row(t, s, hidesDueToday, now, calendar)).ToList());

        var empty = tasks.Count == 0 && !s.IsComposing ? EmptyState(s) : null;
        return new ListModel(
            all.Where(section => !section.IsCompletedHistory).Select(Model).ToList(),
            all.Where(section => section.IsCompletedHistory).Select(Model).ToList(),
            empty,
            !s.IsSearching && s.SidebarSelection != SidebarItem.Completed);
    }

    private static EmptyStateModel EmptyState(AppState s)
    {
        if (s.IsSearching)
        {
            return new EmptyStateModel("magnifyingglass", "No matching tasks", "Try a different word, or clear the filters.");
        }

        return s.SidebarSelection.Kind switch
        {
            SidebarKind.Today => new EmptyStateModel("sun.max", "Nothing due today", "Enjoy the quiet, or plan something new."),
            SidebarKind.Upcoming => new EmptyStateModel("calendar", "Nothing scheduled", "Tasks with a future due date appear here."),
            SidebarKind.Completed => new EmptyStateModel("checkmark.circle", "No completed tasks yet", null),
            _ => new EmptyStateModel("tray", "All clear", "Press Ctrl+N to add a task."),
        };
    }
}
