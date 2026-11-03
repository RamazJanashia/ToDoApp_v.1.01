using TodoCore;
using static TodoApp.State.Tests.TestSupport;
using TaskStatus = TodoCore.TaskStatus;

namespace TodoApp.State.Tests;

public sealed class HeaderModelTests : IDisposable
{
    private readonly PresentationFixture _f = new();

    public void Dispose() => _f.Dispose();

    private HeaderModel Header() => HeaderModel.Build(_f.State, _f.State.VisibleTasks);

    [Fact]
    public void Today_SubtitleHasDateAndCount()
    {
        for (var i = 0; i < 5; i++) _f.Store.CreateTask("Task " + i, null, _f.Today);
        _f.State.Select(SidebarItem.Today);

        var header = Header();

        Assert.Equal("Today", header.Title);
        Assert.Equal("Friday, October 23 · 5 tasks", header.Subtitle);
        Assert.True(header.ShowsProgress);
        Assert.Equal(0, header.Progress);
    }

    [Fact]
    public void Today_SingularNoun()
    {
        _f.Store.CreateTask("Only", null, _f.Today);
        _f.State.Select(SidebarItem.Today);

        Assert.Equal("Friday, October 23 · 1 task", Header().Subtitle);
    }

    [Fact]
    public void Upcoming_CountsScheduled()
    {
        for (var i = 1; i <= 3; i++) _f.Store.CreateTask("Task " + i, null, _f.Day(i));
        _f.State.Select(SidebarItem.Upcoming);

        var header = Header();

        Assert.Equal("Upcoming", header.Title);
        Assert.Equal("3 scheduled", header.Subtitle);
        Assert.False(header.ShowsProgress);
    }

    [Fact]
    public void Completed_CountsCompleted()
    {
        var a = _f.Store.CreateTask("A");
        var b = _f.Store.CreateTask("B");
        _f.Store.ToggleCompleted(a.Id);
        _f.Store.ToggleCompleted(b.Id);
        _f.State.Select(SidebarItem.Completed);

        var header = Header();

        Assert.Equal("2 completed", header.Subtitle);
        Assert.False(header.ShowsProgress);
    }

    [Fact]
    public void Default_ShowsDoneTodayWhenSomethingIsDone()
    {
        var a = _f.Store.CreateTask("A");
        _f.Store.CreateTask("B");
        _f.Store.CreateTask("C");
        _f.State.Select(SidebarItem.AllTasks);

        Assert.Equal("3 tasks", Header().Subtitle);
        Assert.Equal(0, Header().Progress);

        _f.Store.ToggleCompleted(a.Id);

        var header = Header();
        Assert.Equal("3 tasks · 1 done today", header.Subtitle);
        Assert.Equal(1.0 / 3, header.Progress, 6);
        Assert.True(header.ShowsProgress);
    }

    [Fact]
    public void Project_UsesProjectNameAsTitle()
    {
        var project = _f.Store.CreateProject("Home", "house", ProjectColor.Green);
        _f.Store.CreateTask("A", project.Id);
        _f.State.Select(SidebarItem.Project(project.Id));

        var header = Header();

        Assert.Equal("Home", header.Title);
        Assert.Equal("1 task", header.Subtitle);
    }

    [Fact]
    public void Search_CountsResultsAndQuotesTheTrimmedQuery()
    {
        _f.Store.CreateTask("Report one");
        _f.Store.CreateTask("Report two");
        _f.Store.CreateTask("Other");
        _f.State.SearchText = "  report ";

        var header = Header();

        Assert.Equal("Search", header.Title);
        Assert.Equal("2 results for “report”", header.Subtitle);
        Assert.False(header.ShowsProgress);

        _f.State.SearchText = "other";
        Assert.Equal("1 result for “other”", Header().Subtitle);
    }

    [Fact]
    public void Calendar_NeverShowsProgress()
    {
        _f.Store.CreateTask("A", null, _f.Today);
        _f.State.Select(SidebarItem.Today);
        Assert.True(Header().ShowsProgress);

        _f.State.ViewMode = ViewMode.Calendar;

        Assert.False(Header().ShowsProgress);
    }

    [Fact]
    public void EmptyList_HasNoProgress()
    {
        _f.State.Select(SidebarItem.AllTasks);

        var header = Header();

        Assert.False(header.ShowsProgress);
        Assert.Equal(0, header.Progress);
        Assert.Equal("0 tasks", header.Subtitle);
    }

    [Fact]
    public void Progress_IsFractionOfTheGivenTasksDone()
    {
        var a = _f.Store.CreateTask("A");
        var b = _f.Store.CreateTask("B");
        _f.Store.ToggleCompleted(a.Id);
        _f.State.Select(SidebarItem.AllTasks);

        var header = HeaderModel.Build(_f.State, new[] { _f.Store.FindTask(a.Id)!, _f.Store.FindTask(b.Id)! });

        Assert.Equal(0.5, header.Progress);
    }
}
