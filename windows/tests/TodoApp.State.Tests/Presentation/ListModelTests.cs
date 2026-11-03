using TodoCore;
using static TodoApp.State.Tests.TestSupport;
using TaskStatus = TodoCore.TaskStatus;

namespace TodoApp.State.Tests;

public sealed class ListModelTests : IDisposable
{
    private readonly PresentationFixture _f = new();

    public void Dispose() => _f.Dispose();

    private ListModel Build() => ListModel.Build(_f.State);

    [Theory]
    [InlineData("Today", "sun.max", "Nothing due today", "Enjoy the quiet, or plan something new.")]
    [InlineData("Upcoming", "calendar", "Nothing scheduled", "Tasks with a future due date appear here.")]
    [InlineData("Completed", "checkmark.circle", "No completed tasks yet", null)]
    [InlineData("Inbox", "tray", "All clear", "Press Ctrl+N to add a task.")]
    [InlineData("Dashboard", "tray", "All clear", "Press Ctrl+N to add a task.")]
    public void EmptyState_PerSidebarSelection(string selection, string icon, string title, string? message)
    {
        _f.State.Select(selection switch
        {
            "Today" => SidebarItem.Today,
            "Upcoming" => SidebarItem.Upcoming,
            "Completed" => SidebarItem.Completed,
            "Inbox" => SidebarItem.AllTasks,
            _ => SidebarItem.Dashboard,
        });

        var empty = Build().Empty;

        Assert.Equal(new EmptyStateModel(icon, title, message), empty);
    }

    [Fact]
    public void EmptyState_ForAnEmptyProject()
    {
        var project = _f.Store.CreateProject("Home", "house", ProjectColor.Green);
        _f.State.Select(SidebarItem.Project(project.Id));

        Assert.Equal(new EmptyStateModel("tray", "All clear", "Press Ctrl+N to add a task."), Build().Empty);
    }

    [Fact]
    public void EmptyState_ForSearchWithoutMatches()
    {
        _f.Store.CreateTask("Alpha");
        _f.State.SearchText = "zzz";

        Assert.Equal(
            new EmptyStateModel("magnifyingglass", "No matching tasks", "Try a different word, or clear the filters."),
            Build().Empty);
    }

    [Fact]
    public void EmptyState_IsHiddenWhileComposingOrWhenThereAreTasks()
    {
        _f.State.Select(SidebarItem.AllTasks);
        Assert.NotNull(Build().Empty);

        _f.State.IsComposing = true;
        Assert.Null(Build().Empty);

        _f.State.IsComposing = false;
        _f.Store.CreateTask("A");
        Assert.Null(Build().Empty);
    }

    [Fact]
    public void CanCompose_IsFalseOnCompletedAndWhileSearching()
    {
        _f.State.Select(SidebarItem.AllTasks);
        Assert.True(Build().CanCompose);

        _f.State.Select(SidebarItem.Today);
        Assert.True(Build().CanCompose);

        _f.State.Select(SidebarItem.Completed);
        Assert.False(Build().CanCompose);

        _f.State.Select(SidebarItem.AllTasks);
        _f.State.SearchText = "x";
        Assert.False(Build().CanCompose);
    }

    [Fact]
    public void Today_HasAnOverdueSectionAndTheDateSection()
    {
        var late = _f.Store.CreateTask("Late", null, _f.Day(-2));
        var now = _f.Store.CreateTask("Now", null, _f.Today);
        _f.State.Select(SidebarItem.Today);
        _f.State.SelectedTaskId = now.Id;

        var sections = Build().Sections;

        Assert.Equal(2, sections.Count);
        Assert.Equal("overdue", sections[0].Id);
        Assert.Equal("Overdue", sections[0].Title);
        Assert.Null(sections[0].Detail);
        Assert.True(sections[0].IsWarning);
        Assert.Equal("today", sections[1].Id);
        Assert.Equal("Today", sections[1].Title);
        Assert.Equal("Friday, October 23", sections[1].Detail);
        Assert.False(sections[1].IsWarning);
        Assert.Equal(late.Id, Assert.Single(sections[0].Rows).Id);
        var row = Assert.Single(sections[1].Rows);
        Assert.Equal(now.Id, row.Id);
        Assert.True(row.IsSelected);
        Assert.False(sections[0].Rows[0].IsSelected);
    }

    [Fact]
    public void Rows_DescribeTheTask()
    {
        var done = _f.Store.CreateTask("Done");
        _f.Store.ToggleCompleted(done.Id);
        _f.Store.UpdateTask(done.Id, null, t => t with { Priority = Priority.High });
        _f.Store.CreateTask("");
        _f.State.Select(SidebarItem.AllTasks);

        var rows = Build().Sections.Single().Rows;

        var untitled = rows.Single(r => r.IsUntitled);
        Assert.Equal("Untitled", untitled.Title);
        Assert.False(untitled.IsCompleted);
        var finished = rows.Single(r => r.IsCompleted);
        Assert.Equal("Done", finished.Title);
        Assert.False(finished.IsUntitled);
        Assert.Equal(Priority.High, finished.Priority);
        Assert.Equal(_f.Store.FindTask(done.Id), finished.Task);
    }

    [Fact]
    public void Rows_HideTheDueDateOnlyInTheTodayScope()
    {
        var task = _f.Store.CreateTask("A", null, _f.Today);
        _f.State.Select(SidebarItem.Today);
        Assert.Null(Build().Sections.Single().Rows.Single(r => r.Id == task.Id).Meta.DueText);

        _f.State.Select(SidebarItem.AllTasks);
        Assert.Equal("Today", Build().Sections.Single().Rows.Single(r => r.Id == task.Id).Meta.DueText);

        // Searching widens the scope to everything, so the date comes back.
        _f.State.Select(SidebarItem.Today);
        _f.State.SearchText = "A";
        Assert.Equal("Today", Build().Sections.Single().Rows.Single(r => r.Id == task.Id).Meta.DueText);
    }

    [Fact]
    public void Rows_CarryTheProjectName()
    {
        var project = _f.Store.CreateProject("Home", "house", ProjectColor.Green);
        var task = _f.Store.CreateTask("A", project.Id);
        _f.State.Select(SidebarItem.Project(project.Id));

        var row = Build().Sections.Single().Rows.Single();

        Assert.Equal(task.Id, row.Id);
        Assert.Equal("Home", row.Meta.ProjectName);
    }

    [Fact]
    public void Upcoming_GroupsByDay()
    {
        _f.Store.CreateTask("A", null, _f.Day(1));
        _f.Store.CreateTask("B", null, _f.Day(1));
        _f.Store.CreateTask("C", null, _f.Day(9));
        _f.State.Select(SidebarItem.Upcoming);

        var sections = Build().Sections;

        Assert.Equal(new[] { "Tomorrow", "Nov 1" }, sections.Select(s => s.Title).ToArray());
        Assert.Equal(new[] { 2, 1 }, sections.Select(s => s.Rows.Count).ToArray());
    }
}
