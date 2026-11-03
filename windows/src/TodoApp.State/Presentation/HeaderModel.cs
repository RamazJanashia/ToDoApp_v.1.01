using System.Globalization;
using TodoCore;

namespace TodoApp.State;

/// <summary>Large title, date and count, and the progress line of the centre column.</summary>
public sealed record HeaderModel(string Title, string Subtitle, double Progress, bool ShowsProgress)
{
    public static HeaderModel Build(AppState s, IReadOnlyList<TaskItem> tasks)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(tasks);
        var now = s.Clock.UtcNow;
        var calendar = s.Clock.Calendar;

        var count = tasks.Count;
        var done = tasks.Count(t => t.IsCompleted);
        var noun = count == 1 ? "task" : "tasks";
        var kind = s.SidebarSelection.Kind;

        string subtitle;
        if (s.IsSearching)
        {
            subtitle = $"{count} {(count == 1 ? "result" : "results")} for “{s.SearchText.Trim()}”";
        }
        else
        {
            subtitle = kind switch
            {
                SidebarKind.Today =>
                    $"{calendar.ToLocal(now).ToString("dddd, MMMM d", CultureInfo.CurrentCulture)} · {count} {noun}",
                SidebarKind.Upcoming => $"{count} scheduled",
                SidebarKind.Completed => $"{count} completed",
                _ => done > 0 ? $"{count} {noun} · {done} done today" : $"{count} {noun}",
            };
        }

        var showsProgress = s.Shows(WorkspaceElement.HeaderProgress)
            && !s.IsSearching
            && count > 0
            && s.ViewMode != ViewMode.Calendar
            && kind is not (SidebarKind.Upcoming or SidebarKind.Completed);

        // Half the subtasks of a task count as half a task.
        return new HeaderModel(s.Title, subtitle, TaskItem.ProgressOf(tasks), showsProgress);
    }
}
