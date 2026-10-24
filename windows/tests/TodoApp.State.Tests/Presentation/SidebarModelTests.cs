using TodoCore;
using static TodoApp.State.Tests.TestSupport;

namespace TodoApp.State.Tests;

public sealed class SidebarModelTests : IDisposable
{
    private readonly PresentationFixture _f = new();

    public void Dispose() => _f.Dispose();

    [Fact]
    public void Workspace_HasTheFiveRowsInOrder()
    {
        var model = SidebarModel.Build(_f.State);

        Assert.Equal(
            new[] { "Inbox", "Today", "Upcoming", "Completed", "Dashboard" },
            model.Workspace.Select(r => r.Title).ToArray());
        Assert.Equal(
            new[] { "tray", "sun.max", "calendar", "checkmark.circle", "square.grid.2x2" },
            model.Workspace.Select(r => r.Icon).ToArray());
        Assert.Equal(
            new[] { SidebarItem.Inbox, SidebarItem.Today, SidebarItem.Upcoming, SidebarItem.Completed, SidebarItem.Dashboard },
            model.Workspace.Select(r => r.Item).ToArray());
        Assert.All(model.Workspace, r => Assert.Null(r.Tint));
    }

    [Fact]
    public void Counts_OnlyForInboxTodayUpcomingAndProjects()
    {
        var project = _f.Store.CreateProject("Home", "house", ProjectColor.Green);
        _f.Store.CreateTask("Inbox 1");
        _f.Store.CreateTask("Inbox 2");
        var done = _f.Store.CreateTask("Finished");
        _f.Store.ToggleCompleted(done.Id);
        _f.Store.CreateTask("Today", null, _f.Today);
        _f.Store.CreateTask("Soon", null, _f.Day(1));
        _f.Store.CreateTask("Later", null, _f.Day(5));
        _f.Store.CreateTask("In project", project.Id);

        var model = SidebarModel.Build(_f.State);
        int? Count(string title) => model.Workspace.Single(r => r.Title == title).Count;

        Assert.Equal(5, Count("Inbox"));
        Assert.Equal(1, Count("Today"));
        Assert.Equal(2, Count("Upcoming"));
        Assert.Null(Count("Completed"));
        Assert.Null(Count("Dashboard"));
        Assert.Equal(1, Assert.Single(model.Projects).Count);
    }

    [Fact]
    public void ZeroCount_ShowsNothing()
    {
        var project = _f.Store.CreateProject("Home", "house", ProjectColor.Green);

        var model = SidebarModel.Build(_f.State);

        Assert.All(model.Workspace, r => Assert.Null(r.Count));
        Assert.Null(Assert.Single(model.Projects).Count);
    }

    [Fact]
    public void ProjectRows_CarryIconColourAndName()
    {
        var home = _f.Store.CreateProject("Home", "house", ProjectColor.Green);
        var work = _f.Store.CreateProject("Work", "briefcase", ProjectColor.Orange);

        var model = SidebarModel.Build(_f.State);

        Assert.Equal(new[] { "Home", "Work" }, model.Projects.Select(r => r.Title).ToArray());
        Assert.Equal(new[] { "house", "briefcase" }, model.Projects.Select(r => r.Icon).ToArray());
        Assert.Equal(new ProjectColor?[] { ProjectColor.Green, ProjectColor.Orange }, model.Projects.Select(r => r.Tint).ToArray());
        Assert.Equal(new[] { SidebarItem.Project(home.Id), SidebarItem.Project(work.Id) }, model.Projects.Select(r => r.Item).ToArray());
        Assert.False(model.ShowsEmptyProjectsHint);
    }

    [Fact]
    public void EmptyProjectsHint_ShowsWithoutProjects()
    {
        Assert.True(SidebarModel.Build(_f.State).ShowsEmptyProjectsHint);
    }

    [Fact]
    public void Selection_MarksOneRowAndNoneWhileSearching()
    {
        var project = _f.Store.CreateProject("Home", "house", ProjectColor.Green);
        _f.State.Select(SidebarItem.Upcoming);

        var model = SidebarModel.Build(_f.State);
        Assert.Equal("Upcoming", Assert.Single(model.Workspace.Concat(model.Projects), r => r.IsSelected).Title);

        _f.State.Select(SidebarItem.Project(project.Id));
        model = SidebarModel.Build(_f.State);
        Assert.Equal("Home", Assert.Single(model.Workspace.Concat(model.Projects), r => r.IsSelected).Title);

        _f.State.SearchText = "x";
        model = SidebarModel.Build(_f.State);
        Assert.DoesNotContain(model.Workspace.Concat(model.Projects), r => r.IsSelected);
    }
}
