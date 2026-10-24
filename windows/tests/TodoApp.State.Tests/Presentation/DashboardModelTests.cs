using TodoCore;
using static TodoApp.State.Tests.TestSupport;
using TaskStatus = TodoCore.TaskStatus;

namespace TodoApp.State.Tests;

public sealed class DashboardModelTests : IDisposable
{
    private readonly PresentationFixture _f = new();

    public void Dispose() => _f.Dispose();

    private DashboardModel Build() => DashboardModel.Build(_f.State);

    [Fact]
    public void EmptyWorkspace()
    {
        var model = Build();

        Assert.Equal("Friday, October 23 · 0 open tasks", model.Subtitle);
        Assert.Equal("0% of all tasks complete", model.CompletionText);
        Assert.Empty(model.UpNext);
        Assert.All(model.Metrics, m => Assert.Equal(0, m.Value));
        Assert.All(model.Priorities, p => Assert.Equal(0, p.Fraction));
    }

    [Fact]
    public void Texts_UseOpenCountAndRoundedCompletion()
    {
        _f.Store.CreateTask("A");
        _f.Store.CreateTask("B");
        var done = _f.Store.CreateTask("C");
        _f.Store.ToggleCompleted(done.Id);

        var model = Build();

        Assert.Equal("Friday, October 23 · 2 open tasks", model.Subtitle);
        Assert.Equal("33% of all tasks complete", model.CompletionText);
        Assert.Equal(1, model.Stats.Done);
    }

    [Fact]
    public void CompletionText_RoundsHalvesAwayFromZero()
    {
        // 1 of 8 is 12.5 %, which the Mac rounds up to 13.
        var done = _f.Store.CreateTask("done");
        _f.Store.ToggleCompleted(done.Id);
        for (var i = 0; i < 7; i++) _f.Store.CreateTask("open " + i);

        Assert.Equal("13% of all tasks complete", Build().CompletionText);
    }

    [Fact]
    public void Metrics_AreInOrderWithLabelsValuesAndTargets()
    {
        _f.Store.CreateTask("due 1", null, _f.Today);
        _f.Store.CreateTask("due 2", null, _f.Today);
        _f.Store.CreateTask("late", null, _f.Day(-1));
        var done = _f.Store.CreateTask("done");
        _f.Store.ToggleCompleted(done.Id);

        var metrics = Build().Metrics;

        Assert.Equal(new[] { "Due today", "Overdue", "Completed today", "Done this week" }, metrics.Select(m => m.Label).ToArray());
        Assert.Equal(new[] { 2, 1, 1, 1 }, metrics.Select(m => m.Value).ToArray());
        Assert.Equal(
            new[] { SidebarItem.Today, SidebarItem.Today, SidebarItem.Completed, SidebarItem.Completed },
            metrics.Select(m => m.Target).ToArray());
    }

    [Fact]
    public void OverdueMetric_IsRedOnlyWhenPositive()
    {
        Assert.Equal("Primary", Build().Metrics[1].ValueToken);

        _f.Store.CreateTask("late", null, _f.Day(-1));

        var metrics = Build().Metrics;
        Assert.Equal("Red", metrics[1].ValueToken);
        Assert.Equal("Primary", metrics[0].ValueToken);
        Assert.Equal("Primary", metrics[2].ValueToken);
        Assert.Equal("Primary", metrics[3].ValueToken);
    }

    [Fact]
    public void UpNext_ListsOpenScheduledTasksSoonestFirst()
    {
        var project = _f.Store.CreateProject("Home", "house", ProjectColor.Green);
        _f.Store.CreateTask("later", null, _f.Day(1));
        _f.Store.CreateTask("late", project.Id, _f.Day(-1));
        var timed = _f.Store.CreateTask("timed", null, _f.Today);
        _f.Store.UpdateTask(timed.Id, null, t => t with { DueDate = D(2026, 10, 23, 17), HasDueTime = true, Priority = Priority.Medium });
        _f.Store.CreateTask("no date");

        var rows = Build().UpNext;

        Assert.Equal(new[] { "late", "timed", "later" }, rows.Select(r => r.Title).ToArray());
        Assert.Equal("Home", rows[0].ProjectName);
        Assert.Equal("Yesterday", rows[0].DueText);
        Assert.True(rows[0].DueIsOverdue);
        Assert.Null(rows[1].ProjectName);
        Assert.Equal("Today, 5:00 PM", rows[1].DueText);
        Assert.False(rows[1].DueIsOverdue);
        Assert.Equal(Priority.Medium, rows[1].Priority);
        Assert.Equal("Tomorrow", rows[2].DueText);
    }

    [Fact]
    public void UpNext_UsesUntitledForBlankTitles()
    {
        _f.Store.CreateTask("", null, _f.Day(1));

        Assert.Equal("Untitled", Assert.Single(Build().UpNext).Title);
    }

    [Fact]
    public void Priorities_ListHighMediumLowWithBarFractions()
    {
        void Add(Priority p, int n)
        {
            for (var i = 0; i < n; i++) _f.Store.CreateTask(p + " " + i, priority: p);
        }

        Add(Priority.High, 1);
        Add(Priority.Medium, 4);
        Add(Priority.Low, 2);
        Add(Priority.None, 9);

        var bars = Build().Priorities;

        Assert.Equal(new[] { "High", "Medium", "Low" }, bars.Select(b => b.Title).ToArray());
        Assert.Equal(new[] { Priority.High, Priority.Medium, Priority.Low }, bars.Select(b => b.Priority).ToArray());
        Assert.Equal(new[] { 1, 4, 2 }, bars.Select(b => b.Count).ToArray());
        Assert.Equal(new[] { 0.25, 1.0, 0.5 }, bars.Select(b => b.Fraction).ToArray());
        Assert.Equal(new[] { "Red", "PriorityMedium", "PriorityLow" }, bars.Select(b => b.IconToken).ToArray());
    }
}
