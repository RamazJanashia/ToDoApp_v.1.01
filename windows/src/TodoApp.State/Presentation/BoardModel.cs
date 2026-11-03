using TodoCore;
using TaskStatus = TodoCore.TaskStatus;

namespace TodoApp.State;

public sealed record BoardColumnModel(TaskStatus Status, string Title, string Icon, IReadOnlyList<TaskRowModel> Cards, string EmptyText);

public sealed record BoardModel(IReadOnlyList<BoardColumnModel> Columns)
{
    public static BoardModel Build(AppState s)
    {
        ArgumentNullException.ThrowIfNull(s);
        var now = s.Clock.UtcNow;
        var calendar = s.Clock.Calendar;
        var tasks = s.VisibleTasks;

        // Cards never hide their due date, even in Today.
        var columns = Enum.GetValues<TaskStatus>()
            .Select(status => new BoardColumnModel(
                status,
                status.Title(),
                StatusIcon(status),
                tasks.Where(t => t.Status == status)
                    .Select(t => TaskMeta.Row(t, s, false, now, calendar))
                    .ToList(),
                status == TaskStatus.Done ? "Drop finished work here" : "No tasks"))
            .ToList();
        return new BoardModel(columns);
    }

    /// <summary>The Mac's <c>TaskStatus.icon</c> (an SF Symbol name).</summary>
    internal static string StatusIcon(TaskStatus status) => status switch
    {
        TaskStatus.Todo => "circle",
        TaskStatus.InProgress => "circle.lefthalf.filled",
        TaskStatus.Done => "checkmark.circle",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };
}
