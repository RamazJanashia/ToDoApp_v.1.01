using TodoCore;
using static TodoApp.State.Tests.TestSupport;
using TaskStatus = TodoCore.TaskStatus;

namespace TodoApp.State.Tests;

public sealed class ReminderPlannerTests
{
    private static readonly IReadOnlyList<Project> NoProjects = Array.Empty<Project>();

    private static TaskItem AllDay(string title, DateTime due, double offset = 0) =>
        new() { Title = title, DueDate = due, ReminderOffset = offset };

    private static IReadOnlyList<PlannedReminder> Plan(IReadOnlyList<TaskItem> tasks, IReadOnlyList<Project>? projects = null, DateTime? now = null) =>
        ReminderPlanner.Plan(tasks, projects ?? NoProjects, now ?? Now, Berlin);

    [Fact]
    public void Plan_SkipsDoneAndPast_SortsByFireTime_CapsAt60()
    {
        var later = AllDay("later", D(2026, 10, 28));
        var sooner = AllDay("sooner", D(2026, 10, 26));
        var done = AllDay("done", D(2026, 10, 27)) with { Status = TaskStatus.Done };
        var past = AllDay("past", D(2026, 10, 22));
        var noReminder = new TaskItem { Title = "none", DueDate = D(2026, 10, 27) };
        var atNow = new TaskItem { Title = "atNow", DueDate = Now, HasDueTime = true, ReminderOffset = 0 };

        var plan = Plan(new[] { later, done, past, noReminder, atNow, sooner });

        Assert.Equal(new[] { "sooner", "later" }, plan.Select(p => p.Title));
        Assert.Equal(sooner.Id, plan[0].TaskId);
        Assert.Equal(D(2026, 10, 26, 9, 0), plan[0].FireAt);

        var many = Enumerable.Range(0, 75)
            .Select(i => AllDay($"t{i:00}", D(2026, 11, 1).AddDays(74 - i)))
            .ToList();
        var capped = Plan(many);

        Assert.Equal(60, ReminderPlanner.MaximumPending);
        Assert.Equal(60, capped.Count);
        Assert.Equal("t74", capped[0].Title);
        Assert.Equal("t15", capped[59].Title);
        Assert.Equal(capped.OrderBy(p => p.FireAt).Select(p => p.TaskId), capped.Select(p => p.TaskId));
    }

    [Fact]
    public void Body_UsesWordsRelativeToFiringTime()
    {
        // Scheduled on Oct 23, but it fires on Oct 24 at 09:00, so it reads "today", not "tomorrow".
        var task = AllDay("Report", D(2026, 10, 24));

        var reminder = Assert.Single(Plan(new[] { task }));

        Assert.Equal(D(2026, 10, 24, 9, 0), reminder.FireAt);
        Assert.Equal("Due today", reminder.Body);
    }

    [Fact]
    public void Body_TimedWithProject()
    {
        var work = new Project { Name = "Work" };
        var task = new TaskItem
        {
            Title = "Call",
            DueDate = D(2026, 10, 24, 16, 0),
            HasDueTime = true,
            ReminderOffset = 24 * 3600,
            ProjectId = work.Id,
        };

        var reminder = Assert.Single(Plan(new[] { task }, new[] { work }));

        Assert.Equal(D(2026, 10, 23, 16, 0), reminder.FireAt);
        Assert.Equal("Due tomorrow, 4:00 PM · Work", reminder.Body);
    }

    [Fact]
    public void Body_MonthNamesStayCapitalised()
    {
        var fire = D(2026, 10, 23, 16, 0);
        var due = D(2026, 10, 30, 18, 0);
        var task = new TaskItem
        {
            Title = "Far",
            DueDate = due,
            HasDueTime = true,
            ReminderOffset = (due - fire).TotalSeconds,
        };

        var reminder = Assert.Single(Plan(new[] { task }));

        Assert.Equal(fire, reminder.FireAt);
        Assert.Equal("Due Oct 30, 6:00 PM", reminder.Body);

        var allDay = AllDay("Far all day", D(2026, 10, 30), offset: (D(2026, 10, 30, 9, 0) - fire).TotalSeconds);
        Assert.Equal("Due Oct 30", Assert.Single(Plan(new[] { allDay })).Body);
    }

    [Fact]
    public void Body_ProjectOnlyAndEmpty()
    {
        var home = new Project { Name = "Home" };
        // Both fire on Friday Oct 23 at 16:00, so Monday and Tuesday are days away.
        var fire = D(2026, 10, 23, 16, 0);
        var inProject = new TaskItem
        {
            Title = "A", DueDate = D(2026, 10, 26), ReminderOffset = (D(2026, 10, 26, 9, 0) - fire).TotalSeconds, ProjectId = home.Id,
        };
        var unknownProject = new TaskItem
        {
            Title = "B", DueDate = D(2026, 10, 27), ReminderOffset = (D(2026, 10, 27, 9, 0) - fire).TotalSeconds, ProjectId = Guid.NewGuid(),
        };

        var plan = Plan(new[] { inProject, unknownProject }, new[] { home });

        Assert.Equal("Due Monday · Home", plan[0].Body);
        Assert.Equal("Due Tuesday", plan[1].Body);
    }

    [Fact]
    public void Title_EmptyBecomesUntitledTask()
    {
        var task = AllDay("", D(2026, 10, 26));

        Assert.Equal("Untitled task", Assert.Single(Plan(new[] { task })).Title);
    }
}
