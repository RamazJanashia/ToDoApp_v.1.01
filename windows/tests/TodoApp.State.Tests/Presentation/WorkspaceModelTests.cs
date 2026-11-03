using TodoCore;
using static TodoApp.State.Tests.TestSupport;
using TaskStatus = TodoCore.TaskStatus;

namespace TodoApp.State.Tests;

/// <summary>The models with sub-projects, progress and hidden elements (the Mac's mac-fixes changes).</summary>
public sealed class WorkspaceModelTests : IDisposable
{
    private readonly PresentationFixture _f = new();

    public void Dispose() => _f.Dispose();

    private FakeClock Clock => (FakeClock)_f.Clock;

    private static ValueList<Subtask> HalfDone() =>
        ValueList.Of(new Subtask { Title = "a", IsDone = true }, new Subtask { Title = "b" });

    private TaskItem WithSubtasks(string title, Guid? projectId = null)
    {
        var task = _f.Store.CreateTask(title, projectId);
        _f.Store.UpdateTask(task.Id, "Subtasks", t => t with { Subtasks = HalfDone() });
        return _f.Store.FindTask(task.Id)!;
    }

    private (Project Parent, Project Child, Project Loner) Tree()
    {
        var parent = _f.Store.CreateProject("Work", "folder", ProjectColor.Blue);
        var loner = _f.Store.CreateProject("Home", "house", ProjectColor.Green);
        var child = _f.Store.CreateProject("Clients", "folder", ProjectColor.Blue, parent.Id);
        return (parent, child, loner);
    }

    // Sidebar

    [Fact]
    public void Sidebar_HiddenPlacesOmitted()
    {
        _f.State.SetVisible(false, WorkspaceElement.Upcoming);
        var model = SidebarModel.Build(_f.State);
        Assert.DoesNotContain(model.Workspace, r => r.Title == "Upcoming");
        Assert.True(model.ShowsWorkspaceHeader);
    }

    [Fact]
    public void Sidebar_WorkspaceHeaderHiddenWhenNoPlaceShown()
    {
        Tree();
        foreach (var element in new[] { WorkspaceElement.AllTasks, WorkspaceElement.Today, WorkspaceElement.Upcoming, WorkspaceElement.Completed, WorkspaceElement.Dashboard })
        {
            _f.State.SetVisible(false, element);
        }

        var model = SidebarModel.Build(_f.State);
        Assert.Empty(model.Workspace);
        Assert.False(model.ShowsWorkspaceHeader);
        Assert.True(model.ShowsProjects);
    }

    [Fact]
    public void Sidebar_ProjectsHiddenWithTheProjectsElement()
    {
        Tree();
        _f.State.SetVisible(false, WorkspaceElement.Projects);

        var model = SidebarModel.Build(_f.State);
        Assert.False(model.ShowsProjects);
        Assert.Empty(model.Projects);
        Assert.False(model.ShowsEmptyProjectsHint);
    }

    [Fact]
    public void Sidebar_ProjectsOutlineWithDepthAndCollapse()
    {
        var (parent, child, loner) = Tree();

        var rows = SidebarModel.Build(_f.State).Projects;
        Assert.Equal(new[] { "Work", "Clients", "Home" }, rows.Select(r => r.Title));
        Assert.Equal(new[] { 0, 1, 0 }, rows.Select(r => r.Depth));
        Assert.Equal(new bool?[] { false, null, null }, rows.Select(r => r.IsCollapsed));

        _f.State.ToggleCollapsed(parent.Id);

        rows = SidebarModel.Build(_f.State).Projects;
        Assert.Equal(new[] { parent.Id, loner.Id }, rows.Select(r => r.Item.ProjectId!.Value));
        Assert.True(rows[0].IsCollapsed);
        Assert.DoesNotContain(rows, r => r.Item.ProjectId == child.Id);
    }

    [Fact]
    public void Sidebar_ParentCountIncludesChildTasks()
    {
        var (parent, child, _) = Tree();
        _f.Store.CreateTask("In child", child.Id);

        var rows = SidebarModel.Build(_f.State).Projects;
        Assert.Equal(1, rows.Single(r => r.Item.ProjectId == parent.Id).Count);
        Assert.Equal(1, rows.Single(r => r.Item.ProjectId == child.Id).Count);
    }

    [Fact]
    public void Sidebar_AllTasksAndDashboardDoNotAcceptDrops()
    {
        var (parent, _, _) = Tree();
        var model = SidebarModel.Build(_f.State);
        Assert.False(model.Workspace.Single(r => r.Item == SidebarItem.AllTasks).AcceptsDrops);
        Assert.False(model.Workspace.Single(r => r.Item == SidebarItem.Dashboard).AcceptsDrops);
        Assert.True(model.Workspace.Single(r => r.Item == SidebarItem.Today).AcceptsDrops);
        Assert.True(model.Projects.Single(r => r.Item.ProjectId == parent.Id).AcceptsDrops);
    }

    // Header

    [Fact]
    public void Header_ProgressGivesPartialCredit()
    {
        WithSubtasks("Half");
        var done = _f.Store.CreateTask("Done");
        _f.Store.ToggleCompleted(done.Id);
        _f.State.Select(SidebarItem.AllTasks);

        var model = HeaderModel.Build(_f.State, _f.State.CurrentTasks);

        Assert.Equal(0.75, model.Progress);
        Assert.True(model.ShowsProgress);
    }

    [Fact]
    public void Header_ProgressHiddenWhenSwitchedOff()
    {
        _f.Store.CreateTask("Open");
        _f.State.Select(SidebarItem.AllTasks);
        _f.State.SetVisible(false, WorkspaceElement.HeaderProgress);

        Assert.False(HeaderModel.Build(_f.State, _f.State.CurrentTasks).ShowsProgress);
    }

    // Rows

    [Fact]
    public void Meta_HiddenElementsLeaveTheLine()
    {
        var project = new Project { Name = "Work" };
        var task = new TaskItem { Title = "x", ProjectId = project.Id, DueDate = D(2026, 10, 30), Notes = "n" };

        var hidden = new HashSet<WorkspaceElement> { WorkspaceElement.RowProject, WorkspaceElement.RowDueDate };
        var meta = TaskMeta.Build(task, project, false, Now, Berlin, hidden);
        Assert.Null(meta.ProjectName);
        Assert.Null(meta.DueText);
        Assert.True(meta.ShowsNotesIcon);

        hidden.Add(WorkspaceElement.RowNotes);
        Assert.False(TaskMeta.Build(task, project, false, Now, Berlin, hidden).HasAnything);
    }

    [Fact]
    public void Meta_ReminderAloneCountsAsSomething()
    {
        var task = new TaskItem { Title = "x", DueDate = D(2026, 10, 30), ReminderOffset = 0 };
        var meta = TaskMeta.Build(task, null, false, Now, Berlin, new HashSet<WorkspaceElement> { WorkspaceElement.RowDueDate });
        Assert.True(meta.HasReminder);
        Assert.True(meta.HasAnything);
        Assert.False(TaskMeta.Build(task, null, false, Now, Berlin, new HashSet<WorkspaceElement> { WorkspaceElement.RowDueDate, WorkspaceElement.RowReminder }).HasAnything);
    }

    [Fact]
    public void Row_ProgressOnlyWithSubtasksAndShown()
    {
        var half = WithSubtasks("Half");
        _f.Store.CreateTask("Plain");
        _f.State.Select(SidebarItem.AllTasks);

        var rows = ListModel.Build(_f.State).Sections.SelectMany(s => s.Rows).ToList();
        Assert.Equal(0.5, rows.Single(r => r.Id == half.Id).Progress);
        Assert.Null(rows.Single(r => r.Title == "Plain").Progress);
        Assert.All(rows, r => Assert.True(r.ShowsPriority));

        _f.State.SetVisible(false, WorkspaceElement.RowProgress);
        _f.State.SetVisible(false, WorkspaceElement.RowPriority);
        rows = ListModel.Build(_f.State).Sections.SelectMany(s => s.Rows).ToList();
        Assert.All(rows, r => Assert.Null(r.Progress));
        Assert.All(rows, r => Assert.False(r.ShowsPriority));
    }

    [Fact]
    public void Row_TitleIsDisplayTitle()
    {
        _f.Store.CreateTask("");
        _f.State.Select(SidebarItem.AllTasks);
        var row = ListModel.Build(_f.State).Sections.SelectMany(s => s.Rows).Single();
        Assert.Equal("Untitled", row.Title);
        Assert.True(row.IsUntitled);
    }

    // Project list and board

    private Project ProjectWithHistory()
    {
        var project = _f.Store.CreateProject("Work", "folder", ProjectColor.Blue);
        _f.Store.CreateTask("Open", project.Id);
        Clock.UtcNow = D(2026, 10, 15, 9);
        _f.Store.CreateTask("Done earlier", project.Id, status: TaskStatus.Done);
        Clock.UtcNow = Now;
        _f.Store.CreateTask("Done today", project.Id, status: TaskStatus.Done);
        _f.State.Select(SidebarItem.Project(project.Id));
        return project;
    }

    [Fact]
    public void List_ProjectHistoryAfterOpen()
    {
        ProjectWithHistory();

        var model = ListModel.Build(_f.State);

        Assert.Equal(new[] { "all" }, model.Sections.Select(s => s.Id));
        Assert.Equal(new[] { "Open" }, model.Sections[0].Rows.Select(r => r.Title));
        Assert.Equal("Completed · Today", model.History[0].Title);
        Assert.Equal(new[] { "Done earlier" }, model.History[1].Rows.Select(r => r.Title));
    }

    [Fact]
    public void Board_HasNoHistory()
    {
        ProjectWithHistory();
        _f.State.ViewMode = ViewMode.Board;

        var cards = BoardModel.Build(_f.State).Columns.SelectMany(c => c.Cards).Select(c => c.Title).ToList();

        Assert.Contains("Done today", cards);
        Assert.DoesNotContain("Done earlier", cards);
    }

    // Inspector

    [Fact]
    public void Inspector_NoProjectLabelHiddenAndFraction()
    {
        var task = WithSubtasks("Half");
        _f.State.Select(SidebarItem.AllTasks);
        _f.State.SelectTask(task.Id);

        var model = InspectorModel.Build(_f.State)!;
        Assert.Equal("No Project", model.Project.Text);
        Assert.Equal(0.5, model.SubtaskFraction);
        Assert.True(model.Shows(WorkspaceElement.DetailNotes));

        _f.State.SetVisible(false, WorkspaceElement.DetailNotes);
        Assert.False(InspectorModel.Build(_f.State)!.Shows(WorkspaceElement.DetailNotes));
    }

    // Dashboard

    [Fact]
    public void Dashboard_ProjectsInOutlineOrderWithRollUp()
    {
        var (parent, child, _) = Tree();
        _f.Store.CreateTask("Own", parent.Id);
        _f.Store.CreateTask("In child", child.Id);

        var model = DashboardModel.Build(_f.State);

        Assert.Equal(new[] { "Work", "Clients", "Home" }, model.Stats.Projects.Select(p => p.Project.Name));
        Assert.Equal(2, model.Stats.Projects[0].Total);
    }

    [Fact]
    public void Dashboard_HiddenSections()
    {
        _f.State.SetVisible(false, WorkspaceElement.DashboardActivity);
        var model = DashboardModel.Build(_f.State);
        Assert.False(model.Shows(WorkspaceElement.DashboardActivity));
        Assert.True(model.Shows(WorkspaceElement.DashboardMetrics));
    }

    // Small helpers

    [Theory]
    [InlineData("New Task (Ctrl+N)", "New Task")]
    [InlineData("Hide Sidebar (Ctrl+Shift+S)", "Hide Sidebar")]
    [InlineData("Complete", "Complete")]
    public void AccessibleText_StripsShortcut(string help, string expected) =>
        Assert.Equal(expected, AccessibleText.FromHelp(help));

    [Fact]
    public void Glyphs_SidebarToggleAndChevron()
    {
        Assert.Equal("", Glyphs.For("sidebar.left"));
        Assert.Equal("", Glyphs.For("chevron.down"));
    }
}
