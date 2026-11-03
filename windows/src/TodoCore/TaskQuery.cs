using System.Globalization;

namespace TodoCore;

public enum TaskScopeKind
{
    /// <summary>Open tasks from everywhere, plus what was finished today.</summary>
    AllOpen,
    Today,
    Upcoming,
    Completed,
    All,

    /// <summary>A project together with its sub-projects.</summary>
    Projects,
}

/// <summary>
/// Which slice of the workspace a list shows. <see cref="ProjectIds"/> is set only for
/// <see cref="TaskScopeKind.Projects"/>; compare scopes by <see cref="Kind"/>, since the set compares by reference.
/// </summary>
public readonly record struct TaskScope(TaskScopeKind Kind, IReadOnlySet<Guid>? ProjectIds = null)
{
    public static TaskScope AllOpen => new(TaskScopeKind.AllOpen);

    public static TaskScope Today => new(TaskScopeKind.Today);

    public static TaskScope Upcoming => new(TaskScopeKind.Upcoming);

    public static TaskScope Completed => new(TaskScopeKind.Completed);

    public static TaskScope All => new(TaskScopeKind.All);

    public static TaskScope Projects(IEnumerable<Guid> ids) => new(TaskScopeKind.Projects, ids.ToHashSet());

    /// <summary>One project on its own.</summary>
    public static TaskScope Project(Guid id) => Projects(new[] { id });
}

public enum TaskSort
{
    Manual,
    DueDate,
    Priority,
    Title,
    Created,
}

public static class TaskSorts
{
    public static string Title(this TaskSort sort) => sort switch
    {
        TaskSort.Manual => "Manual",
        TaskSort.DueDate => "Due Date",
        TaskSort.Priority => "Priority",
        TaskSort.Title => "Title",
        TaskSort.Created => "Date Created",
        _ => throw new ArgumentOutOfRangeException(nameof(sort), sort, null),
    };
}

/// <param name="IncludesCompletedHistory">In a project scope, also bring in tasks finished on earlier days.</param>
public sealed record TaskQuery(
    TaskScope Scope,
    string SearchText = "",
    IReadOnlySet<Priority>? Priorities = null,
    bool ShowCompleted = true,
    TaskSort Sort = TaskSort.Manual,
    bool IncludesCompletedHistory = false)
{
    private const CompareOptions SearchOptions = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
    private const CompareOptions TitleOptions = CompareOptions.IgnoreCase | CompareOptions.NumericOrdering;

    public IReadOnlyList<TaskItem> Run(IReadOnlyList<TaskItem> tasks, IReadOnlyList<Project> projects, DateTime now, TodoCalendar calendar)
    {
        var names = new Dictionary<Guid, string>();
        foreach (var project in projects) names.TryAdd(project.Id, project.Name);

        var tokens = (SearchText ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var scope = Scope;
        var bounds = new DayBounds(now, calendar);

        var matching = tasks.Where(task =>
        {
            if (!Belongs(task, scope, bounds) && !IsCompletedHistory(task, scope)) return false;
            if (task.IsCompleted && !ShowCompleted && scope.Kind != TaskScopeKind.Completed) return false;
            if (Priorities is { Count: > 0 } && !Priorities.Contains(task.Priority)) return false;
            if (tokens.Length > 0)
            {
                string? projectName = null;
                if (task.ProjectId is { } id && names.TryGetValue(id, out var name)) projectName = name;
                return tokens.All(token => Contains(task, projectName, token));
            }
            return true;
        });

        if (scope.Kind == TaskScopeKind.Completed)
        {
            return matching.OrderByDescending(t => t.CompletedAt ?? DateTime.MinValue).ToList();
        }

        var sort = Sort;
        var comparer = Comparer<TaskItem>.Create((lhs, rhs) =>
        {
            if (lhs.IsCompleted != rhs.IsCompleted) return lhs.IsCompleted ? 1 : -1;
            return Compare(lhs, rhs, sort);
        });
        return matching.OrderBy(t => t, comparer).ToList();
    }

    private bool IsCompletedHistory(TaskItem task, TaskScope scope) =>
        IncludesCompletedHistory
        && task.IsCompleted
        && scope.Kind == TaskScopeKind.Projects
        && task.ProjectId is { } projectId
        && scope.ProjectIds?.Contains(projectId) == true;

    /// <summary>Incomplete tasks in a scope, for sidebar counters.</summary>
    public static int OpenCount(TaskScope scope, IReadOnlyList<TaskItem> tasks, DateTime now, TodoCalendar calendar)
    {
        var bounds = new DayBounds(now, calendar);
        return tasks.Count(task => !task.IsCompleted && Belongs(task, scope, bounds));
    }

    public static bool Belongs(TaskItem task, TaskScope scope, DateTime now, TodoCalendar calendar) =>
        Belongs(task, scope, new DayBounds(now, calendar));

    /// <summary>Worked out once per pass: calendar arithmetic is the expensive part of running over many tasks.</summary>
    private readonly struct DayBounds
    {
        public DayBounds(DateTime now, TodoCalendar calendar)
        {
            StartOfToday = calendar.StartOfDay(now);
            StartOfTomorrow = calendar.AddDays(StartOfToday, 1);
        }

        public DateTime StartOfToday { get; }

        public DateTime StartOfTomorrow { get; }
    }

    private static bool Belongs(TaskItem task, TaskScope scope, DayBounds bounds)
    {
        var startOfTomorrow = bounds.StartOfTomorrow;
        var finishedToday = task.CompletedAt is { } completed && completed >= bounds.StartOfToday && completed < startOfTomorrow;

        switch (scope.Kind)
        {
            case TaskScopeKind.AllOpen:
                return !task.IsCompleted || finishedToday;
            case TaskScopeKind.Today:
                if (task.DueDate is not { } todayDue || todayDue >= startOfTomorrow) return false;
                return !task.IsCompleted || finishedToday;
            case TaskScopeKind.Upcoming:
                if (task.DueDate is not { } upcomingDue) return false;
                return upcomingDue >= startOfTomorrow && !task.IsCompleted;
            case TaskScopeKind.Completed:
                return task.IsCompleted;
            case TaskScopeKind.All:
                return true;
            case TaskScopeKind.Projects:
                if (task.ProjectId is not { } projectId || scope.ProjectIds?.Contains(projectId) != true) return false;
                return !task.IsCompleted || finishedToday;
            default:
                throw new ArgumentOutOfRangeException(nameof(scope), scope, null);
        }
    }

    private static bool Matches(string? text, string token) =>
        text is not null && CultureInfo.CurrentCulture.CompareInfo.IndexOf(text, token, SearchOptions) >= 0;

    private static bool Contains(TaskItem task, string? projectName, string token) =>
        Matches(task.Title, token)
        || Matches(task.Notes, token)
        || Matches(projectName, token)
        || task.Subtasks.Any(s => Matches(s.Title, token))
        || task.Attachments.Any(a => Matches(a.Name, token));

    /// <summary>Earlier due dates first; a task with a date beats one without; otherwise 0 (fall through).</summary>
    private static int CompareDue(TaskItem lhs, TaskItem rhs)
    {
        switch (lhs.DueDate, rhs.DueDate)
        {
            case ({ } l, { } r) when l != r: return l < r ? -1 : 1;
            case (not null, null): return -1;
            case (null, not null): return 1;
            default: return 0;
        }
    }

    internal static bool Precedes(TaskItem lhs, TaskItem rhs, TaskSort sort) => Compare(lhs, rhs, sort) < 0;

    private static int Compare(TaskItem lhs, TaskItem rhs, TaskSort sort)
    {
        int order;
        switch (sort)
        {
            case TaskSort.DueDate:
                order = CompareDue(lhs, rhs);
                if (order != 0) return order;
                break;
            case TaskSort.Priority:
                if (lhs.Priority != rhs.Priority) return lhs.Priority > rhs.Priority ? -1 : 1;
                order = CompareDue(lhs, rhs);
                if (order != 0) return order;
                break;
            case TaskSort.Title:
                order = CultureInfo.CurrentCulture.CompareInfo.Compare(lhs.Title, rhs.Title, TitleOptions);
                if (order != 0) return order;
                break;
            case TaskSort.Created:
                if (lhs.CreatedAt != rhs.CreatedAt) return lhs.CreatedAt > rhs.CreatedAt ? -1 : 1;
                break;
        }
        if (lhs.SortOrder != rhs.SortOrder) return lhs.SortOrder < rhs.SortOrder ? -1 : 1;
        return lhs.CreatedAt.CompareTo(rhs.CreatedAt);
    }
}

/// <summary>A titled run of tasks in a list, e.g. "Overdue" or one day in Upcoming.</summary>
/// <param name="IsCompletedHistory">One day's finished tasks below a project's open ones.</param>
public sealed record TaskSection(
    string Id,
    string? Title,
    string? Detail,
    bool IsWarning,
    IReadOnlyList<TaskItem> Tasks,
    bool IsCompletedHistory = false)
{
    /// <summary>Splits an already sorted list into sections appropriate for the scope.</summary>
    public static IReadOnlyList<TaskSection> Make(TaskScope scope, IReadOnlyList<TaskItem> tasks, DateTime now, TodoCalendar calendar)
    {
        switch (scope.Kind)
        {
            case TaskScopeKind.Today:
            {
                var overdue = tasks.Where(t => t.IsOverdue(now, calendar) && !t.IsDueOnSameDayAs(now, calendar)).ToList();
                if (overdue.Count == 0) return new[] { Flat(tasks) };
                var overdueIds = overdue.Select(t => t.Id).ToHashSet();
                var rest = tasks.Where(t => !overdueIds.Contains(t.Id)).ToList();
                var sections = new List<TaskSection> { new("overdue", "Overdue", null, true, overdue) };
                if (rest.Count > 0)
                {
                    var detail = calendar.ToLocal(now).ToString("dddd, MMMM d", CultureInfo.CurrentCulture);
                    sections.Add(new TaskSection("today", "Today", detail, false, rest));
                }
                return sections;
            }
            case TaskScopeKind.Upcoming:
                return Grouped(tasks, t => t.DueDate, ascending: true, now, calendar);
            case TaskScopeKind.Completed:
                return Grouped(tasks, t => t.CompletedAt, ascending: false, now, calendar);
            case TaskScopeKind.Projects:
            {
                var open = tasks.Where(t => !t.IsCompleted).ToList();
                var finished = tasks
                    .Where(t => t.IsCompleted)
                    .OrderByDescending(t => t.CompletedAt ?? DateTime.MinValue)
                    .ToList();
                if (finished.Count == 0) return new[] { Flat(tasks) };
                var history = Grouped(finished, t => t.CompletedAt, ascending: false, now, calendar)
                    .Select(day => new TaskSection(
                        "completed-" + day.Id,
                        "Completed · " + (day.Title ?? ""),
                        day.Detail,
                        false,
                        day.Tasks,
                        IsCompletedHistory: true));
                return (open.Count == 0 ? Enumerable.Empty<TaskSection>() : new[] { Flat(open) }).Concat(history).ToList();
            }
            default:
                return new[] { Flat(tasks) };
        }
    }

    private static TaskSection Flat(IReadOnlyList<TaskItem> tasks) => new("all", null, null, false, tasks);

    private static IReadOnlyList<TaskSection> Grouped(
        IReadOnlyList<TaskItem> tasks,
        Func<TaskItem, DateTime?> date,
        bool ascending,
        DateTime now,
        TodoCalendar calendar)
    {
        // DateTime.MinValue stands in for "no date" (Swift's distantPast), so it sorts before every real day.
        var buckets = new Dictionary<DateTime, List<TaskItem>>();
        foreach (var task in tasks)
        {
            var day = date(task) is { } d ? calendar.StartOfDay(d) : DateTime.MinValue;
            if (!buckets.TryGetValue(day, out var list)) buckets[day] = list = new List<TaskItem>();
            list.Add(task);
        }

        var days = ascending ? buckets.Keys.OrderBy(k => k) : buckets.Keys.OrderByDescending(k => k);
        return days.Select(day =>
        {
            if (day == DateTime.MinValue) return new TaskSection("none", "No Date", null, false, buckets[day]);
            var title = DueDateText.DayLabel(day, now, calendar);
            var detail = calendar.ToLocal(day).ToString("MMM d", CultureInfo.CurrentCulture);
            return new TaskSection(
                day.Ticks.ToString(CultureInfo.InvariantCulture),
                title,
                title == detail ? null : detail,
                false,
                buckets[day]);
        }).ToList();
    }
}

public static class DueDateText
{
    /// <summary>"Today", "Tomorrow, 4:00 PM", "Friday", "Oct 30".</summary>
    public static string Label(DateTime date, bool hasTime, DateTime now, TodoCalendar calendar)
    {
        var day = DayLabel(date, now, calendar);
        if (!hasTime) return day;
        return $"{day}, {calendar.ToLocal(date).ToString("t", CultureInfo.CurrentCulture)}";
    }

    public static string DayLabel(DateTime date, DateTime now, TodoCalendar calendar)
    {
        var difference = DayDifference(now, date, calendar);
        switch (difference)
        {
            case 0: return "Today";
            case 1: return "Tomorrow";
            case -1: return "Yesterday";
        }

        var local = calendar.ToLocal(date);
        if (difference is >= 2 and <= 6) return local.ToString("dddd", CultureInfo.CurrentCulture);
        return calendar.IsSameYear(date, now)
            ? local.ToString("MMM d", CultureInfo.CurrentCulture)
            : local.ToString("MMM d, yyyy", CultureInfo.CurrentCulture);
    }

    /// <summary>The coming Monday (or the locale's first weekday, where that isn't Sunday).</summary>
    public static DateTime NextWeek(DateTime now, TodoCalendar calendar)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        var today = calendar.StartOfDay(now);
        var target = calendar.FirstWeekday == 1 ? 2 : calendar.FirstWeekday;
        var ahead = (target - calendar.Weekday(today) + 7) % 7;
        return calendar.AddDays(today, ahead == 0 ? 7 : ahead);
    }

    public static int DayDifference(DateTime start, DateTime end, TodoCalendar calendar) =>
        calendar.DayDifference(start, end);
}
