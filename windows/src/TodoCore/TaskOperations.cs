namespace TodoCore;

/// <summary>Edits that touch more than one task, kept free of UI so they can be checked directly.</summary>
public static class TaskOperations
{
    /// <summary>
    /// Changes the status of <paramref name="ids"/>.
    /// Finishing a repeating task schedules its next occurrence and hands the repeat rule to it.
    /// Reopening that task takes the rule back and removes the occurrence again, unless the
    /// occurrence has been edited or finished since, in which case both are kept. If the
    /// occurrence has been deleted, the task simply takes the rule back.
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
                tasks[index] = tasks[index] with
                {
                    Recurrence = null,
                    NextOccurrenceId = next.Id,
                    NextOccurrenceDueDate = next.DueDate,
                    NextOccurrenceRule = next.Recurrence,
                };
                spawned.Add(next);
            }
            else if (wasDone && status != TaskStatus.Done && tasks[index].NextOccurrenceId is { } nextId)
            {
                var original = tasks[index];
                tasks[index] = tasks[index] with { NextOccurrenceId = null, NextOccurrenceDueDate = null, NextOccurrenceRule = null };
                var occurrence = tasks.FirstOrDefault(t => t.Id == nextId);
                if (occurrence is null)
                {
                    tasks[index] = tasks[index] with { Recurrence = original.NextOccurrenceRule };
                }
                else if (IsUntouched(occurrence, original))
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

    /// <summary>
    /// Removes a project without losing anything under it: its sub-projects and tasks move up to its parent, or to
    /// the top level and "no project" when it has none.
    /// </summary>
    public static void DeleteProject(Guid id, List<Project> projects, List<TaskItem> tasks)
    {
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(tasks);
        var declared = projects.Find(p => p.Id == id)?.ParentId;
        var parent = declared is { } value && value != id && projects.Exists(p => p.Id == value) ? declared : null;
        projects.RemoveAll(p => p.Id == id);
        for (var index = 0; index < projects.Count; index++)
        {
            if (projects[index].ParentId == id) projects[index] = projects[index] with { ParentId = parent };
        }

        for (var index = 0; index < tasks.Count; index++)
        {
            if (tasks[index].ProjectId == id) tasks[index] = tasks[index] with { ProjectId = parent };
        }
    }

    /// <summary>
    /// Moves a task directly in front of <paramref name="beforeId"/> in <paramref name="ordered"/> (the list as
    /// displayed). When there's no room between its new neighbours (equal orders, or so many drops into one gap that
    /// the numbers can't get closer), every task is renumbered first, keeping their order.
    /// </summary>
    public static void Move(Guid id, Guid beforeId, IReadOnlyList<TaskItem> ordered, List<TaskItem> tasks)
    {
        ArgumentNullException.ThrowIfNull(ordered);
        ArgumentNullException.ThrowIfNull(tasks);
        var shown = ordered;
        if (!HasRoom(id, beforeId, ordered))
        {
            var manual = Comparer<TaskItem>.Create((lhs, rhs) =>
                TaskQuery.Precedes(lhs, rhs, TaskSort.Manual) ? -1 : TaskQuery.Precedes(rhs, lhs, TaskSort.Manual) ? 1 : 0);
            var ranked = tasks.OrderBy(t => t, manual).ToList();
            var fresh = new Dictionary<Guid, double>();
            for (var rank = 0; rank < ranked.Count; rank++) fresh.TryAdd(ranked[rank].Id, rank + 1);
            for (var index = 0; index < tasks.Count; index++)
            {
                if (fresh.TryGetValue(tasks[index].Id, out var order)) tasks[index] = tasks[index] with { SortOrder = order };
            }

            shown = ordered.Select(t => fresh.TryGetValue(t.Id, out var order) ? t with { SortOrder = order } : t).ToList();
        }

        if (SortOrderPlacing(id, beforeId, shown) is not { } newOrder) return;
        var target = tasks.FindIndex(t => t.Id == id);
        if (target >= 0) tasks[target] = tasks[target] with { SortOrder = newOrder };
    }

    private static bool HasRoom(Guid id, Guid beforeId, IReadOnlyList<TaskItem> ordered)
    {
        var others = ordered.Where(t => t.Id != id).ToList();
        var targetIndex = others.FindIndex(t => t.Id == beforeId);
        if (targetIndex <= 0) return true;
        var above = others[targetIndex - 1];
        var target = others[targetIndex];
        // Across the open/completed boundary the neighbours aren't in sequence; see SortOrderPlacing.
        if (above.IsCompleted != target.IsCompleted) return true;
        var middle = (above.SortOrder + target.SortOrder) / 2;
        return above.SortOrder < middle && middle < target.SortOrder;
    }

    /// <summary>Whether an occurrence is still exactly as its completion created it.</summary>
    internal static bool IsUntouched(TaskItem next, TaskItem original) =>
        next.Status == TaskStatus.Todo
        && next.NextOccurrenceId is null
        && next.DueDate == original.NextOccurrenceDueDate
        && next.Recurrence == original.NextOccurrenceRule
        && next.HasDueTime == original.HasDueTime
        && next.ReminderOffset == original.ReminderOffset
        && next.SortOrder == original.SortOrder
        && next.Title == original.Title
        && next.Notes == original.Notes
        && next.Priority == original.Priority
        && next.ProjectId == original.ProjectId
        && next.Attachments.Equals(original.Attachments)
        && next.Subtasks.Select(s => s.Title).SequenceEqual(original.Subtasks.Select(s => s.Title))
        && !next.Subtasks.Any(s => s.IsDone);
}
