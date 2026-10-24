using TodoCore;

namespace TodoApp.State;

public sealed record PlannedReminder(Guid TaskId, DateTime FireAt, string Title, string Body);

/// <summary>Decides which reminders to schedule and what they say. Pure, so it can be tested anywhere.</summary>
public static class ReminderPlanner
{
    /// <summary>The platform keeps a limited number of pending notifications per app; stay well under it.</summary>
    public const int MaximumPending = 60;

    /// <summary>Open tasks whose reminder is still in the future, soonest first, at most <see cref="MaximumPending"/>.</summary>
    public static IReadOnlyList<PlannedReminder> Plan(
        IReadOnlyList<TaskItem> tasks, IReadOnlyList<Project> projects, DateTime now, TodoCalendar calendar)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(calendar);

        var projectNames = new Dictionary<Guid, string>();
        foreach (var project in projects) projectNames.TryAdd(project.Id, project.Name);

        return tasks
            .Select(task => (Task: task, FireAt: task.IsCompleted ? null : task.ReminderDate(calendar)))
            .Where(entry => entry.FireAt is { } fireAt && fireAt > now)
            .OrderBy(entry => entry.FireAt!.Value)
            .Take(MaximumPending)
            .Select(entry =>
            {
                var task = entry.Task;
                var fireAt = entry.FireAt!.Value;
                string? projectName = task.ProjectId is { } id && projectNames.TryGetValue(id, out var name) ? name : null;
                return new PlannedReminder(
                    task.Id,
                    fireAt,
                    task.Title.Length == 0 ? "Untitled task" : task.Title,
                    Body(task, fireAt, projectName, calendar));
            })
            .ToList();
    }

    /// <summary>Words like "today" are worked out from when the reminder appears, not when it was scheduled.</summary>
    private static string Body(TaskItem task, DateTime fireAt, string? projectName, TodoCalendar calendar)
    {
        var parts = new List<string>();
        if (task.DueDate is { } due)
        {
            var label = DueDateText.Label(due, task.HasDueTime, fireAt, calendar);
            parts.Add("Due " + LowercasedFirstWordIfRelative(label));
        }

        if (projectName is not null) parts.Add(projectName);
        return string.Join(" · ", parts);
    }

    /// <summary>"Today, 4:00 PM" reads as "due today, 4:00 PM"; month names stay capitalised.</summary>
    private static string LowercasedFirstWordIfRelative(string text)
    {
        foreach (var word in new[] { "Today", "Tomorrow", "Yesterday" })
        {
            if (text.StartsWith(word, StringComparison.Ordinal)) return word.ToLowerInvariant() + text[word.Length..];
        }

        return text;
    }
}
