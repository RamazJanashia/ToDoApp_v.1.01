namespace TodoCore;

/// <summary>Edits that touch more than one task, kept free of UI so they can be checked directly.</summary>
public static class TaskOperations
{
    /// <summary>
    /// Changes the status of <paramref name="ids"/>.
    /// Finishing a repeating task schedules its next occurrence and hands the repeat rule to it.
    /// Reopening that task takes the rule back and removes the occurrence again, unless the
    /// occurrence has been edited or finished since, in which case both are kept.
    /// </summary>
    public static void SetStatus(TaskStatus status, IReadOnlySet<Guid> ids, List<TaskItem> tasks, DateTime now, TodoCalendar calendar)
    {
        var spawned = new List<TaskItem>();
        var retracted = new HashSet<Guid>();

        for (var index = 0; index < tasks.Count; index++)
        {
            if (!ids.Contains(tasks[index].Id) || tasks[index].Status == status) continue;

            var wasDone = tasks[index].IsCompleted;
            tasks[index] = tasks[index].WithStatus(status, now);

            if (status == TaskStatus.Done && !wasDone && tasks[index].NextOccurrence(now, calendar) is { } next)
            {
                tasks[index] = tasks[index] with { Recurrence = null, NextOccurrenceId = next.Id };
                spawned.Add(next);
            }
            else if (wasDone && status != TaskStatus.Done && tasks[index].NextOccurrenceId is { } nextId)
            {
                tasks[index] = tasks[index] with { NextOccurrenceId = null };
                var occurrence = tasks.FirstOrDefault(t => t.Id == nextId);
                if (occurrence is not null && IsUntouched(occurrence, tasks[index]))
                {
                    tasks[index] = tasks[index] with
                    {
                        Recurrence = occurrence.Recurrence,
                        RecurrenceDay = occurrence.RecurrenceDay,
                    };
                    retracted.Add(nextId);
                }
            }
        }

        tasks.RemoveAll(t => retracted.Contains(t.Id));
        tasks.AddRange(spawned);
    }

    /// <summary>
    /// The sort order that puts a task directly in front of <paramref name="beforeId"/> in <paramref name="ordered"/>
    /// (the list as displayed), or null when the move makes no sense.
    /// </summary>
    public static double? SortOrderPlacing(Guid id, Guid beforeId, IReadOnlyList<TaskItem> ordered)
    {
        if (id == beforeId) return null;
        var others = ordered.Where(t => t.Id != id).ToList();
        var targetIndex = others.FindIndex(t => t.Id == beforeId);
        if (targetIndex < 0) return null;
        var after = others[targetIndex].SortOrder;
        if (targetIndex <= 0) return after - 1;
        var before = others[targetIndex - 1].SortOrder;
        // Completed rows always sit last, so at the open/completed boundary the neighbours
        // aren't in sort-order sequence. Landing just after the row above keeps the task
        // where it was dropped.
        return before < after ? (before + after) / 2 : before + 1;
    }

    /// <summary>Whether an occurrence is still exactly as its completion created it.</summary>
    internal static bool IsUntouched(TaskItem next, TaskItem original) =>
        next.Status == TaskStatus.Todo
        && next.NextOccurrenceId is null
        && next.Title == original.Title
        && next.Notes == original.Notes
        && next.Priority == original.Priority
        && next.ProjectId == original.ProjectId
        && next.Attachments.Equals(original.Attachments)
        && next.Subtasks.Select(s => s.Title).SequenceEqual(original.Subtasks.Select(s => s.Title))
        && !next.Subtasks.Any(s => s.IsDone);
}
