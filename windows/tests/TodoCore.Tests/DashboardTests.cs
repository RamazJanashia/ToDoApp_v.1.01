using TodoCore;
using static TodoCore.Tests.TestSupport;

namespace TodoCore.Tests;

public class DashboardTests
{
    private static readonly Project Work = new() { Name = "Work" };

    private static readonly List<TaskItem> Tasks = new()
    {
        new() { Title = "Inbox open", SortOrder = 1 },
        new() { Title = "Inbox done today", Status = TaskStatus.Done, SortOrder = 2, CompletedAt = Now },
        new() { Title = "Inbox done last week", Status = TaskStatus.Done, SortOrder = 3, CompletedAt = D(2026, 10, 15) },
        new() { Title = "Due today", Priority = Priority.Low, ProjectId = Work.Id, DueDate = Today, SortOrder = 4 },
        new() { Title = "Overdue", Priority = Priority.High, ProjectId = Work.Id, DueDate = D(2026, 10, 21), SortOrder = 5 },
        new() { Title = "Tomorrow", ProjectId = Work.Id, DueDate = D(2026, 10, 24), SortOrder = 6 },
        new() { Title = "Next week", Notes = "quarterly numbers", DueDate = D(2026, 10, 30), SortOrder = 7 },
    };

    private static readonly DashboardStats Stats = new(Tasks, new[] { Work }, Now, Berlin);

    [Fact]
    public void Dashboard_ProjectCountsIncludeSubprojects()
    {
        var parent = new Project { Name = "Parent" };
        var child = new Project { Name = "Child", ParentId = parent.Id };
        var tasks = new List<TaskItem>
        {
            new() { Title = "Own", ProjectId = parent.Id },
            new() { Title = "Child 1", ProjectId = child.Id },
            new() { Title = "Child 2", ProjectId = child.Id, Status = TaskStatus.Done, CompletedAt = Now },
        };

        var stats = new DashboardStats(tasks, new[] { parent, child }, Now, Berlin);

        var progress = stats.Projects.Single(p => p.Project.Id == parent.Id);
        Assert.Equal(3, progress.Total);
        Assert.Equal(1, progress.Done);
        Assert.Equal(2, stats.Projects.Single(p => p.Project.Id == child.Id).Total);
    }

    [Fact]
    public void OpenAndDone()
    {
        Assert.Equal(5, Stats.Open);
        Assert.Equal(2, Stats.Done);
    }

    [Fact]
    public void OverdueAndDueToday()
    {
        Assert.Equal(1, Stats.Overdue);
        Assert.Equal(1, Stats.DueToday);
    }

    [Fact]
    public void CompletedTodayAndThisWeek()
    {
        Assert.Equal(1, Stats.CompletedToday);
        Assert.Equal(1, Stats.CompletedThisWeek);
    }

    [Fact]
    public void ActivityEndsToday()
    {
        Assert.Equal(Today, Stats.Activity[^1].Day);
        Assert.Equal(14, Stats.Activity.Count);
        Assert.Equal(1, Stats.Activity[^1].Count);
        // Completed on Oct 15, which is 8 days back and inside the 14-day window.
        Assert.Equal(1, Stats.Activity.Single(a => a.Day == D(2026, 10, 15)).Count);
    }

    [Fact]
    public void ProjectProgress()
    {
        Assert.Equal(3, Stats.Projects[0].Total);
        Assert.Equal(0, Stats.Projects[0].Done);
        Assert.Equal(0.0, Stats.Projects[0].Fraction);
    }

    [Fact]
    public void UpNextStartsWithOverdue()
    {
        Assert.Equal("Overdue", Stats.UpNext[0].Title);
        Assert.Equal(new[] { "Overdue", "Due today", "Tomorrow", "Next week" }, Stats.UpNext.Select(t => t.Title));
    }

    [Fact]
    public void OpenByPriorityAndRate()
    {
        Assert.Equal(1, Stats.OpenByPriority[Priority.High]);
        Assert.Equal(1, Stats.OpenByPriority[Priority.Low]);
        Assert.Equal(3, Stats.OpenByPriority[Priority.None]);
        Assert.Equal(2.0 / 7.0, Stats.CompletionRate, 10);
    }
}
