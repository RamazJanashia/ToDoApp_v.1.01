using System.Globalization;
using TodoCore;

namespace TodoApp.State;

/// <summary>An icon and a value in the task details. Icon names are SF Symbols; tokens are listed on <see cref="PriorityStyle"/>.</summary>
/// <param name="IconProjectColor">Set when <paramref name="IconToken"/> is "Project".</param>
/// <param name="IsPlaceholder">The text is a prompt ("Add due date"), drawn in the tertiary tone.</param>
public sealed record PropertyRowModel(string Icon, string IconToken, ProjectColor? IconProjectColor, string Text, bool IsPlaceholder);

public sealed record InspectorModel(
    Guid TaskId,
    string Title,
    bool IsCompleted,
    PropertyRowModel Due,
    PropertyRowModel Reminder,
    PropertyRowModel Priority,
    PropertyRowModel Project,
    PropertyRowModel Repeat,
    PropertyRowModel Status,
    string Notes,
    IReadOnlyList<Subtask> Subtasks,
    string? SubtaskProgress,
    IReadOnlyList<Attachment> Attachments,
    string CreatedText)
{
    /// <summary>Null when no task is selected.</summary>
    public static InspectorModel? Build(AppState s)
    {
        ArgumentNullException.ThrowIfNull(s);
        if (s.SelectedTask is not { } task) return null;
        var now = s.Clock.UtcNow;
        var calendar = s.Clock.Calendar;
        var project = s.Store.FindProject(task.ProjectId);

        var due = task.DueDate is { } dueDate
            ? new PropertyRowModel(
                "calendar",
                task.IsOverdue(now, calendar) ? "Red" : "Secondary",
                null,
                DueDateText.Label(dueDate, task.HasDueTime, now, calendar),
                false)
            : new PropertyRowModel("calendar", "Secondary", null, "Add due date", true);

        var reminder = new PropertyRowModel(
            task.ReminderOffset is null ? "bell" : "bell.fill",
            "Secondary",
            null,
            task.DueDate is not null && task.ReminderOffset is { } offset ? ReminderPreset.Title(offset) : "Add reminder",
            task.ReminderOffset is null || task.DueDate is null);

        var priority = task.Priority == TodoCore.Priority.None
            ? new PropertyRowModel("flag", "Secondary", null, "Add priority", true)
            : new PropertyRowModel("flag", PriorityStyle.Token(task.Priority), null, task.Priority.Title(), false);

        var projectRow = project is null
            ? new PropertyRowModel("tray", "Secondary", null, "Inbox", false)
            : new PropertyRowModel(project.Icon, "Project", project.Color, project.Name, false);

        var repeat = new PropertyRowModel("repeat", "Secondary", null, task.Recurrence?.Summary ?? "Doesn’t repeat", task.Recurrence is null);

        var status = new PropertyRowModel(
            BoardModel.StatusIcon(task.Status),
            task.Status == TodoCore.TaskStatus.Done ? "Green" : "Secondary",
            null,
            task.Status.Title(),
            false);

        return new InspectorModel(
            task.Id,
            task.Title,
            task.IsCompleted,
            due,
            reminder,
            priority,
            projectRow,
            repeat,
            status,
            task.Notes,
            task.Subtasks,
            TaskMeta.SubtaskProgress(task),
            task.Attachments,
            "Created " + calendar.ToLocal(task.CreatedAt).ToString("MMM d, yyyy", CultureInfo.CurrentCulture));
    }
}

public static class TextSync
{
    /// <summary>
    /// Whether a model's text should be written into a text box. Never while the user is typing in it (a
    /// re-render would reset the caret and drop keystrokes), and not when it already shows that text.
    /// </summary>
    public static bool ShouldPush(string current, string incoming, bool hasFocus) =>
        !hasFocus && !string.Equals(current, incoming, StringComparison.Ordinal);
}
