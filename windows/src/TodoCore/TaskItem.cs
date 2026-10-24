using System.Globalization;

namespace TodoCore;

public sealed record Subtask
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required string Title { get; init; }

    public bool IsDone { get; init; }
}

public enum AttachmentKind
{
    File,
    Link,
}

public sealed record Attachment
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required AttachmentKind Kind { get; init; }

    /// <summary>What the user sees: the original file name, or the link's host.</summary>
    public required string Name { get; init; }

    /// <summary>Files: the name of the copy inside the attachments folder. Links: the absolute URL string.</summary>
    public required string Location { get; init; }

    public DateTime AddedAt { get; init; } = DateTime.UtcNow;
}

public sealed record TaskItem
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Title { get; init; } = "";

    public string Notes { get; init; } = "";

    public TaskStatus Status { get; init; } = TaskStatus.Todo;

    public Priority Priority { get; init; } = Priority.None;

    public Guid? ProjectId { get; init; }

    /// <summary>Start of day for all-day tasks; the exact moment when <see cref="HasDueTime"/> is true.</summary>
    public DateTime? DueDate { get; init; }

    public bool HasDueTime { get; init; }

    /// <summary>Seconds before the due moment to send a reminder. Null means no reminder.</summary>
    public double? ReminderOffset { get; init; }

    public RecurrenceRule? Recurrence { get; init; }

    /// <summary>
    /// The day of the month a repeating series aims for (e.g. 31), carried from one occurrence
    /// to the next so short months don't pull the series earlier.
    /// </summary>
    public int? RecurrenceDay { get; init; }

    /// <summary>
    /// Set on a finished repeating task: the occurrence its completion created. Reopening the
    /// task removes that occurrence again if it hasn't been touched.
    /// </summary>
    public Guid? NextOccurrenceId { get; init; }

    public ValueList<Subtask> Subtasks { get; init; } = ValueList<Subtask>.Empty;

    public ValueList<Attachment> Attachments { get; init; } = ValueList<Attachment>.Empty;

    /// <summary>Manual ordering; lower comes first.</summary>
    public double SortOrder { get; init; }

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    public DateTime? CompletedAt { get; init; }

    public bool IsCompleted => Status == TaskStatus.Done;

    public int CompletedSubtaskCount => Subtasks.Count(s => s.IsDone);

    public TaskItem WithCompleted(bool completed, DateTime at) =>
        WithStatus(completed ? TaskStatus.Done : TaskStatus.Todo, at);

    public TaskItem WithStatus(TaskStatus status, DateTime at) =>
        this with
        {
            Status = status,
            CompletedAt = status == TaskStatus.Done ? CompletedAt ?? at : null,
        };

    /// <summary>Sets the due day, keeping the time of day when the task has one.</summary>
    public TaskItem RescheduledTo(DateTime day, TodoCalendar calendar)
    {
        var start = calendar.StartOfDay(day);
        if (!HasDueTime || DueDate is not { } current) return this with { DueDate = start };
        var time = calendar.ToLocal(current);
        return this with { DueDate = calendar.SettingTime(start, time.Hour, time.Minute) };
    }

    public TaskItem WithoutDueDate() =>
        this with { DueDate = null, HasDueTime = false, ReminderOffset = null };

    /// <summary>When the reminder fires. All-day tasks anchor their reminder at 9:00 on the due day.</summary>
    public DateTime? ReminderDate(TodoCalendar calendar)
    {
        if (DueDate is not { } due || ReminderOffset is not { } offset) return null;
        var anchor = HasDueTime ? due : calendar.SettingTime(due, 9, 0);
        return anchor.AddSeconds(-offset);
    }

    public bool IsOverdue(DateTime now, TodoCalendar calendar)
    {
        if (IsCompleted || DueDate is not { } due) return false;
        if (HasDueTime) return due < now;
        return due < calendar.StartOfDay(now);
    }

    public bool IsDueOnSameDayAs(DateTime day, TodoCalendar calendar) =>
        DueDate is { } due && calendar.IsSameDay(due, day);
}

public static class ReminderPreset
{
    /// <summary>Offsets offered in the reminder picker, in seconds before the due moment.</summary>
    public static IReadOnlyList<double> Offsets { get; } =
        new double[] { 0, 300, 900, 1_800, 3_600, 7_200, 86_400, 172_800 };

    public static string Title(double offset)
    {
        switch (offset)
        {
            case 0: return "At time of due";
            case 300: return "5 minutes before";
            case 900: return "15 minutes before";
            case 1_800: return "30 minutes before";
            case 3_600: return "1 hour before";
            case 7_200: return "2 hours before";
            case 86_400: return "1 day before";
            case 172_800: return "2 days before";
        }

        var seconds = (long)Math.Round(offset);
        if (seconds > 0 && seconds % 86_400 == 0) return Plural(seconds / 86_400, "day");
        if (seconds > 0 && seconds % 3_600 == 0) return Plural(seconds / 3_600, "hour");
        return Plural((long)Math.Round(offset / 60), "minute");
    }

    private static string Plural(long n, string unit) =>
        string.Create(CultureInfo.InvariantCulture, $"{n} {unit}{(n == 1 ? "" : "s")} before");
}
