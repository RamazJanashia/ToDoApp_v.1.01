namespace TodoCore;

public enum Priority
{
    None = 0,
    Low = 1,
    Medium = 2,
    High = 3,
}

public static class Priorities
{
    /// <summary>Highest first, the order menus and pickers show them in.</summary>
    public static IReadOnlyList<Priority> Descending { get; } =
        new[] { Priority.High, Priority.Medium, Priority.Low, Priority.None };

    public static string Title(this Priority priority) => priority switch
    {
        Priority.None => "No Priority",
        Priority.Low => "Low",
        Priority.Medium => "Medium",
        Priority.High => "High",
        _ => throw new ArgumentOutOfRangeException(nameof(priority), priority, null),
    };
}

public enum TaskStatus
{
    Todo,
    InProgress,
    Done,
}

public static class TaskStatuses
{
    public static string Title(this TaskStatus status) => status switch
    {
        TaskStatus.Todo => "To Do",
        TaskStatus.InProgress => "In Progress",
        TaskStatus.Done => "Done",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };
}
