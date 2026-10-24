using System.Globalization;
using TodoCore;

namespace TodoApp.State;

/// <param name="ValueToken">"Red" when the number needs attention, otherwise "Primary".</param>
/// <param name="Target">Where clicking the tile goes.</param>
public sealed record MetricModel(string Label, int Value, string ValueToken, SidebarItem Target);

public sealed record UpNextRowModel(Guid Id, string Title, string? ProjectName, string? DueText, bool DueIsOverdue, Priority Priority);

/// <param name="IconToken">See <see cref="PriorityStyle"/>.</param>
/// <param name="Fraction">Bar length relative to the longest bar (0 when nothing is open).</param>
public sealed record PriorityBarModel(Priority Priority, string Title, string IconToken, int Count, double Fraction);

public sealed record DashboardModel(
    DashboardStats Stats,
    string Subtitle,
    string CompletionText,
    IReadOnlyList<MetricModel> Metrics,
    IReadOnlyList<UpNextRowModel> UpNext,
    IReadOnlyList<PriorityBarModel> Priorities)
{
    public static DashboardModel Build(AppState s)
    {
        ArgumentNullException.ThrowIfNull(s);
        var now = s.Clock.UtcNow;
        var calendar = s.Clock.Calendar;
        var stats = new DashboardStats(s.Store.Tasks, s.Store.Projects, now, calendar);

        var date = calendar.ToLocal(now).ToString("dddd, MMMM d", CultureInfo.CurrentCulture);
        var percent = (int)Math.Round(stats.CompletionRate * 100, MidpointRounding.AwayFromZero);

        var metrics = new[]
        {
            new MetricModel("Due today", stats.DueToday, "Primary", SidebarItem.Today),
            new MetricModel("Overdue", stats.Overdue, stats.Overdue > 0 ? "Red" : "Primary", SidebarItem.Today),
            new MetricModel("Completed today", stats.CompletedToday, "Primary", SidebarItem.Completed),
            new MetricModel("Done this week", stats.CompletedThisWeek, "Primary", SidebarItem.Completed),
        };

        var upNext = stats.UpNext.Select(task => new UpNextRowModel(
            task.Id,
            task.Title.Length == 0 ? "Untitled" : task.Title,
            s.Store.FindProject(task.ProjectId)?.Name,
            task.DueDate is { } due ? DueDateText.Label(due, task.HasDueTime, now, calendar) : null,
            task.IsOverdue(now, calendar),
            task.Priority)).ToList();

        var levels = new[] { Priority.High, Priority.Medium, Priority.Low };
        var counts = levels.Select(p => stats.OpenByPriority.GetValueOrDefault(p)).ToList();
        var largest = Math.Max(counts.Max(), 1);
        var priorities = levels.Select((p, i) => new PriorityBarModel(
            p,
            p.Title(),
            PriorityStyle.Token(p),
            counts[i],
            (double)counts[i] / largest)).ToList();

        return new DashboardModel(
            stats,
            $"{date} · {stats.Open} open tasks",
            $"{percent}% of all tasks complete",
            metrics,
            upNext,
            priorities);
    }
}
