using TodoCore;
using static TodoApp.State.Tests.TestSupport;

namespace TodoApp.State.Tests;

public sealed class WorkspaceTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly FakeClock _clock = new();
    private readonly List<AppSettings> _saved = new();

    public void Dispose() => _temp.Dispose();

    private TaskStore NewStore() => new(_temp.Path, _clock, new ManualDebouncer(), new FakeReminders());

    private AppState NewState(AppSettings? settings = null, TaskStore? store = null) =>
        new(store ?? NewStore(), _clock, settings ?? new AppSettings(), _saved.Add);

    [Fact]
    public void ElementRawValues_AreTheMacNames()
    {
        Assert.Equal("allTasks", WorkspaceElement.AllTasks.RawValue());
        Assert.Equal("rowPriority", WorkspaceElement.RowPriority.RawValue());
        Assert.Equal("dashboardUpNext", WorkspaceElement.DashboardUpNext.RawValue());
        Assert.Equal("toolbarAppearance", WorkspaceElement.ToolbarAppearance.RawValue());
        Assert.Equal(WorkspaceElement.DetailNotes, WorkspaceElements.Parse("detailNotes"));
        Assert.Null(WorkspaceElements.Parse("DetailNotes"));
        Assert.Equal(36, Enum.GetValues<WorkspaceElement>().Length);
    }

    [Fact]
    public void Groups_HoldTheirElementsInOrder()
    {
        Assert.Equal(
            new[] { "Sidebar", "Views", "Task rows", "Task details", "Dashboard", "Window" },
            Enum.GetValues<WorkspaceGroup>().Select(g => g.Title()));
        Assert.Equal(
            new[] { WorkspaceElement.ListView, WorkspaceElement.BoardView, WorkspaceElement.CalendarView },
            WorkspaceGroup.Views.Elements());
        Assert.Equal("Subtask progress bar", WorkspaceElement.RowProgress.Title());
        Assert.Equal("Progress bar above the list", WorkspaceElement.HeaderProgress.Title());
    }

    [Fact]
    public void StorageKeys_RoundTrip()
    {
        var id = Guid.Parse("0905AB33-A30F-4D94-8D6F-7BE7F7B0CA90");
        var items = new[] { SidebarItem.AllTasks, SidebarItem.Today, SidebarItem.Upcoming, SidebarItem.Completed, SidebarItem.Dashboard, SidebarItem.Project(id) };
        Assert.Equal(
            new[] { "allTasks", "today", "upcoming", "completed", "dashboard", "project:0905AB33-A30F-4D94-8D6F-7BE7F7B0CA90" },
            items.Select(i => i.StorageKey()));
        Assert.All(items, item => Assert.Equal(item, SidebarItems.FromStorageKey(item.StorageKey())));
        Assert.Null(SidebarItems.FromStorageKey("inbox"));
        Assert.Null(SidebarItems.FromStorageKey("project:nope"));
    }

    [Fact]
    public void Restore_SelectionAndTask()
    {
        var store = NewStore();
        var project = store.Projects[0];
        var task = store.Tasks.Last(t => t.ProjectId == project.Id && !t.IsCompleted);

        var state = NewState(new AppSettings { SidebarSelection = SidebarItem.Project(project.Id).StorageKey(), SelectedTask = task.Id }, store);

        Assert.Equal(SidebarItem.Project(project.Id), state.SidebarSelection);
        Assert.Equal(task.Id, state.SelectedTaskId);
        Assert.Empty(_saved);
    }

    [Fact]
    public void Restore_MissingProject_FallsBackToToday()
    {
        var state = NewState(new AppSettings { SidebarSelection = SidebarItem.Project(Guid.NewGuid()).StorageKey() });
        Assert.Equal(SidebarItem.Today, state.SidebarSelection);
    }

    [Fact]
    public void Restore_TaskNotVisible_SelectsFirstVisible()
    {
        var state = NewState(new AppSettings { SidebarSelection = "today", SelectedTask = Guid.NewGuid() });
        Assert.Equal(state.VisibleTasks[0].Id, state.SelectedTaskId);
    }

    [Fact]
    public void Restore_FilterSidebarAndCollapsed()
    {
        var project = Guid.NewGuid();
        var state = NewState(new AppSettings
        {
            PriorityFilter = ValueList.Of(3),
            SidebarVisible = false,
            CollapsedProjects = ValueList.Of(project),
            HiddenElements = ValueList.Of("rowNotes"),
        });

        Assert.Equal(new[] { Priority.High }, state.PriorityFilter);
        Assert.False(state.IsSidebarVisible);
        Assert.Contains(project, state.CollapsedProjects);
        Assert.False(state.Shows(WorkspaceElement.RowNotes));
        Assert.True(state.Shows(WorkspaceElement.RowProject));
    }

    [Fact]
    public void Changes_ArePersisted()
    {
        var state = NewState();
        var project = state.Store.Projects[0];

        state.Select(SidebarItem.Project(project.Id));
        Assert.Equal(SidebarItem.Project(project.Id).StorageKey(), _saved[^1].SidebarSelection);

        var task = state.VisibleTasks[0];
        state.SelectTask(task.Id);
        Assert.Equal(task.Id, _saved[^1].SelectedTask);

        state.PriorityFilter = new HashSet<Priority> { Priority.High };
        Assert.Equal(new[] { 3 }, _saved[^1].PriorityFilter);

        state.IsSidebarVisible = false;
        Assert.False(_saved[^1].SidebarVisible);

        state.ToggleCollapsed(project.Id);
        Assert.Equal(new[] { project.Id }, _saved[^1].CollapsedProjects);
        state.ToggleCollapsed(project.Id);
        Assert.Empty(_saved[^1].CollapsedProjects);

        state.SetVisible(false, WorkspaceElement.RowNotes);
        Assert.Equal(new[] { "rowNotes" }, _saved[^1].HiddenElements);
    }

    [Fact]
    public void HidingCurrentPlace_MovesToFirstShown()
    {
        var state = NewState();
        Assert.Equal(SidebarItem.Today, state.SidebarSelection);

        state.SetVisible(false, WorkspaceElement.Today);

        Assert.Equal(SidebarItem.AllTasks, state.SidebarSelection);
    }

    [Fact]
    public void HidingAllPlaces_SelectsFirstProject()
    {
        var state = NewState();
        foreach (var element in new[] { WorkspaceElement.AllTasks, WorkspaceElement.Upcoming, WorkspaceElement.Completed, WorkspaceElement.Dashboard, WorkspaceElement.Today })
        {
            state.SetVisible(false, element);
        }

        Assert.Equal(SidebarItem.Project(state.Store.Projects.Outline()[0].Id), state.SidebarSelection);
    }

    [Fact]
    public void LastView_CannotBeHidden()
    {
        var state = NewState();
        state.SetVisible(false, WorkspaceElement.BoardView);
        state.SetVisible(false, WorkspaceElement.CalendarView);

        Assert.False(state.CanHide(WorkspaceElement.ListView));
        state.SetVisible(false, WorkspaceElement.ListView);
        Assert.True(state.Shows(WorkspaceElement.ListView));
        Assert.Equal(new[] { ViewMode.List }, state.VisibleViewModes);
        Assert.True(state.CanHide(WorkspaceElement.RowNotes));
    }

    [Fact]
    public void HidingCurrentView_SwitchesToFirstShown()
    {
        var state = NewState();
        state.ViewMode = ViewMode.Board;

        state.SetVisible(false, WorkspaceElement.BoardView);

        Assert.Equal(ViewMode.List, state.ViewMode);
    }

    [Fact]
    public void ShowEverything_ClearsHidden()
    {
        var state = NewState();
        state.SetVisible(false, WorkspaceElement.RowNotes);
        state.SetVisible(false, WorkspaceElement.Dashboard);

        state.ShowEverything();

        Assert.Empty(state.HiddenElements);
        Assert.Empty(_saved[^1].HiddenElements);
    }

    [Fact]
    public void SelectIfShown_IgnoresHidden()
    {
        var state = NewState();
        state.SetVisible(false, WorkspaceElement.Completed);

        state.SelectIfShown(SidebarItem.Completed);
        Assert.Equal(SidebarItem.Today, state.SidebarSelection);

        state.SelectIfShown(SidebarItem.Upcoming);
        Assert.Equal(SidebarItem.Upcoming, state.SidebarSelection);
    }

    [Fact]
    public void HomeForNewTasks_AllTasksElseToday()
    {
        var state = NewState();
        Assert.Equal(SidebarItem.AllTasks, state.HomeForNewTasks);
        state.SetVisible(false, WorkspaceElement.AllTasks);
        Assert.Equal(SidebarItem.Today, state.HomeForNewTasks);
    }

    [Fact]
    public void OpenSettings_RaisesRequest()
    {
        var state = NewState();
        var raised = 0;
        state.SettingsRequested += (_, _) => raised++;

        state.OpenSettings();

        Assert.Equal(1, raised);
    }
}
