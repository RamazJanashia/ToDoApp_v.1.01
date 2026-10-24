namespace TodoCore;

public sealed record DayCount(DateTime Day, int Count);

public sealed record ProjectProgress(Project Project, int Done, int Total)
{
    public double Fraction => Total == 0 ? 0 : (double)Done / Total;
}

/// <summary>Numbers behind the dashboard, computed in one pass over the tasks.</summary>
public sealed class DashboardStats
{
    public DashboardStats(
        IReadOnlyList<TaskItem> tasks,
        IReadOnlyList<Project> projects,
        DateTime now,
        TodoCalendar calendar,
        int days = 14)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(calendar);

        var startOfToday = calendar.StartOfDay(now);
        var startOfTomorrow = calendar.AddDays(startOfToday, 1);
        var weekAgo = calendar.AddDays(startOfToday, -6);
        var firstDay = calendar.AddDays(startOfToday, -(days - 1));

        var perDay = new Dictionary<DateTime, int>();
        var perProject = new Dictionary<Guid, (int Done, int Total)>();
        var openByPriority = new Dictionary<Priority, int>();

        foreach (var task in tasks)
        {
            if (task.ProjectId is { } projectId)
            {
                perProject.TryGetValue(projectId, out var entry);
                entry.Total++;
                if (task.IsCompleted) entry.Done++;
                perProject[projectId] = entry;
            }

            if (task.IsCompleted)
            {
                Done++;
                if (task.CompletedAt is { } completedAt)
                {
                    var day = calendar.StartOfDay(completedAt);
                    if (day >= firstDay) perDay[day] = perDay.GetValueOrDefault(day) + 1;
                    if (day == startOfToday) CompletedToday++;
                    if (day >= weekAgo) CompletedThisWeek++;
                }
            }
            else
            {
                Open++;
                openByPriority[task.Priority] = openByPriority.GetValueOrDefault(task.Priority) + 1;
                if (task.IsOverdue(now, calendar))
                {
                    Overdue++;
                }
                else if (task.DueDate is { } due && due >= startOfToday && due < startOfTomorrow)
                {
                    DueToday++;
                }
            }
        }

        OpenByPriority = openByPriority;

        Activity = Enumerable.Range(0, days)
            .Select(offset =>
            {
                var day = calendar.AddDays(firstDay, offset);
                return new DayCount(day, perDay.GetValueOrDefault(day));
            })
            .ToList();

        Projects = projects
            .Select(project =>
            {
                perProject.TryGetValue(project.Id, out var entry);
                return new ProjectProgress(project, entry.Done, entry.Total);
            })
            .ToList();

        UpNext = tasks
            .Where(t => !t.IsCompleted && t.DueDate is not null)
            .OrderBy(t => t.DueDate!.Value)
            .Take(6)
            .ToList();
    }

    public int Open { get; }

    public int Done { get; }

    public int DueToday { get; }

    public int Overdue { get; }

    public int CompletedToday { get; }

    public int CompletedThisWeek { get; }

    public IReadOnlyDictionary<Priority, int> OpenByPriority { get; }

    /// <summary>Completions per day, oldest first, ending today.</summary>
    public IReadOnlyList<DayCount> Activity { get; }

    public IReadOnlyList<ProjectProgress> Projects { get; }

    /// <summary>The next open tasks with a due date, soonest (including overdue) first.</summary>
    public IReadOnlyList<TaskItem> UpNext { get; }

    public double CompletionRate => Open + Done == 0 ? 0 : (double)Done / (Open + Done);
}
