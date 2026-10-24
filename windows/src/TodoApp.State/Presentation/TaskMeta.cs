using System.Globalization;
using TodoCore;

namespace TodoApp.State;

/// <summary>What the quiet grey line under a task title shows: project, due date, repeat, reminder, subtasks, attachments, notes.</summary>
public sealed record TaskMetaInfo(
    string? ProjectName,
    string? DueText,
    bool DueIsOverdue,
    string? RepeatSummary,
    bool HasReminder,
    string? SubtaskProgress,
    int AttachmentCount,
    bool ShowsNotesIcon)
{
    /// <summary>False means the line isn't drawn at all.</summary>
    public bool HasAnything =>
        ProjectName is not null
        || DueText is not null
        || RepeatSummary is not null
        || SubtaskProgress is not null
        || AttachmentCount > 0
        || ShowsNotesIcon;
}

public static class TaskMeta
{
    public static TaskMetaInfo Build(TaskItem task, Project? project, bool hidesDueToday, DateTime now, TodoCalendar calendar)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(calendar);

        string? dueText = null;
        var overdue = false;
        if (task.DueDate is { } due)
        {
            if (hidesDueToday && calendar.IsSameDay(due, now))
            {
                // The Today list already says "today"; a timed task still shows when.
                if (task.HasDueTime)
                {
                    dueText = calendar.ToLocal(due).ToString("t", CultureInfo.CurrentCulture);
                    overdue = task.IsOverdue(now, calendar);
                }
            }
            else
            {
                dueText = DueDateText.Label(due, task.HasDueTime, now, calendar);
                overdue = task.IsOverdue(now, calendar);
            }
        }

        // The Mac shows the notes icon in place of everything else, so it goes by the label, not the date.
        var showsNotes = task.Notes.Length > 0 && project is null && dueText is null;

        return new TaskMetaInfo(
            project?.Name,
            dueText,
            overdue,
            task.Recurrence?.Summary,
            task.ReminderOffset is not null && task.DueDate is not null,
            SubtaskProgress(task),
            task.Attachments.Count,
            showsNotes);
    }

    /// <summary>"1/2", or null when there are no subtasks.</summary>
    internal static string? SubtaskProgress(TaskItem task) =>
        task.Subtasks.Count == 0
            ? null
            : string.Create(CultureInfo.CurrentCulture, $"{task.CompletedSubtaskCount}/{task.Subtasks.Count}");

    internal static TaskRowModel Row(
        TaskItem task,
        TaskStore store,
        Guid? selectedId,
        bool hidesDueToday,
        DateTime now,
        TodoCalendar calendar) =>
        new(
            task.Id,
            task.Title.Length == 0 ? "Untitled" : task.Title,
            task.Title.Length == 0,
            task.IsCompleted,
            task.Priority,
            selectedId == task.Id,
            Build(task, store.FindProject(task.ProjectId), hidesDueToday, now, calendar),
            task);
}

/// <summary>One task in a list or on the board. <see cref="Title"/> is already "Untitled" for a blank title.</summary>
public sealed record TaskRowModel(
    Guid Id,
    string Title,
    bool IsUntitled,
    bool IsCompleted,
    Priority Priority,
    bool IsSelected,
    TaskMetaInfo Meta,
    TaskItem Task);

/// <summary>
/// Theme token names the views map to brushes. Icon and dot colours in the models are one of:
/// "Secondary", "Red", "Green", "Primary", "Tertiary", "PriorityMedium", "PriorityLow", or "Project"
/// (the model then carries the project's colour next to it).
/// </summary>
public static class PriorityStyle
{
    /// <summary>The Mac's <c>Priority.tint</c>: only High uses colour; the rest step down in brightness.</summary>
    public static string Token(Priority priority) => priority switch
    {
        Priority.High => "Red",
        Priority.Medium => "PriorityMedium",
        Priority.Low => "PriorityLow",
        Priority.None => "Tertiary",
        _ => throw new ArgumentOutOfRangeException(nameof(priority), priority, null),
    };
}
