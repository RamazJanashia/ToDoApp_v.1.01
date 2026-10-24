using TodoCore;
using static TodoApp.State.Tests.TestSupport;
using TaskStatus = TodoCore.TaskStatus;

namespace TodoApp.State.Tests;

public sealed class AppStateTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly FakeClock _clock = new();
    private readonly List<AppSettings> _saved = new();

    public void Dispose() => _temp.Dispose();

    /// <summary>A state over the first-run sample data.</summary>
    private AppState NewState(AppSettings? settings = null) => Wrap(NewStore(), settings);

    /// <summary>A state over an empty store: the sample is deleted, then <paramref name="seed"/> adds what the test needs.</summary>
    private AppState NewEmptyState(Action<TaskStore>? seed = null, AppSettings? settings = null)
    {
        var store = NewStore();
        store.DeleteTasks(store.Tasks.Select(t => t.Id).ToHashSet());
        foreach (var project in store.Projects.ToList()) store.DeleteProject(project.Id);
        seed?.Invoke(store);
        return Wrap(store, settings);
    }

    private TaskStore NewStore() => new(_temp.Path, _clock, new ManualDebouncer(), new FakeReminders());

    private AppState Wrap(TaskStore store, AppSettings? settings) =>
        new(store, _clock, settings ?? new AppSettings(), _saved.Add);

    private static string P(Guid id) => id.ToString("D").ToUpperInvariant();

    private static string[] Payload(params Guid[] ids) => ids.Select(P).ToArray();

    private static TaskItem Inbox(TaskStore store, string title) => store.CreateTask(title);

    // Inbox-scope list of three tasks, nothing selected (the Today scope is empty).
    private AppState InboxState(out TaskItem a, out TaskItem b, out TaskItem c)
    {
        TaskItem x = null!, y = null!, z = null!;
        var state = NewEmptyState(store =>
        {
            x = Inbox(store, "One");
            y = Inbox(store, "Two");
            z = Inbox(store, "Three");
        });
        state.Select(SidebarItem.Inbox);
        (a, b, c) = (x, y, z);
        return state;
    }

    [Fact]
    public void Initial_SelectsFirstTodayTask()
    {
        var state = NewState();

        Assert.Equal(SidebarItem.Today, state.SidebarSelection);
        Assert.Equal(ViewMode.List, state.ViewMode);
        Assert.NotNull(state.SelectedTaskId);
        Assert.Equal(state.VisibleTasks[0].Id, state.SelectedTaskId);
        Assert.Equal("Review contract", state.SelectedTask!.Title);
    }

    [Fact]
    public void Initial_SelectsNothingWhenTodayIsEmpty()
    {
        var state = NewEmptyState();

        Assert.Null(state.SelectedTaskId);
        Assert.Null(state.SelectedTask);
    }

    [Fact]
    public void Select_ClearsSearchAndHiddenSelection()
    {
        Project project = null!;
        TaskItem task = null!;
        var state = NewEmptyState(store =>
        {
            project = store.CreateProject("Work", "folder", ProjectColor.Blue);
            task = store.CreateTask("Filed", project.Id, D(2026, 10, 23));
        });
        Assert.Equal(task.Id, state.SelectedTaskId);
        state.SearchText = "fil";
        state.IsComposing = true;

        state.Select(SidebarItem.Project(project.Id));

        Assert.Equal("", state.SearchText);
        Assert.False(state.IsComposing);
        Assert.Equal(task.Id, state.SelectedTaskId);

        state.Select(SidebarItem.Inbox);

        Assert.Equal(SidebarItem.Inbox, state.SidebarSelection);
        Assert.Null(state.SelectedTaskId);
    }

    [Fact]
    public void Search_SwitchesScopeToAll_AndTitleToSearch()
    {
        var state = NewState();
        state.Select(SidebarItem.Inbox);

        state.SearchText = "   ";
        Assert.False(state.IsSearching);
        Assert.Equal(TaskScope.Inbox, state.Scope);
        Assert.Equal("Inbox", state.Title);

        state.SearchText = "contract";
        Assert.True(state.IsSearching);
        Assert.Equal(TaskScope.All, state.Scope);
        Assert.Equal("Search", state.Title);
        Assert.Contains(state.VisibleTasks, t => t.Title == "Review contract");

        state.Select(SidebarItem.Dashboard);
        state.SearchText = "contract";
        Assert.False(state.IsDashboard);
    }

    [Fact]
    public void Search_ShowsCompletedEvenWhenHidden()
    {
        var state = NewState();
        state.ShowCompleted = false;
        state.Select(SidebarItem.Inbox);
        Assert.DoesNotContain(state.VisibleTasks, t => t.IsCompleted);

        state.SearchText = "invoices";

        Assert.Contains(state.VisibleTasks, t => t.Title == "Review invoices");
    }

    [Fact]
    public void WindowTitle_IsTasksDashTitle()
    {
        var state = NewState();
        Assert.Equal("Tasks — Today", state.WindowTitle);

        state.Select(SidebarItem.Upcoming);
        Assert.Equal("Tasks — Upcoming", state.WindowTitle);

        state.Select(SidebarItem.Completed);
        Assert.Equal("Tasks — Completed", state.WindowTitle);

        state.Select(SidebarItem.Dashboard);
        Assert.Equal("Tasks — Dashboard", state.WindowTitle);
        Assert.True(state.IsDashboard);

        var work = state.Store.Projects.Single(p => p.Name == "Work");
        state.Select(SidebarItem.Project(work.Id));
        Assert.Equal("Tasks — Work", state.WindowTitle);

        state.Select(SidebarItem.Project(Guid.NewGuid()));
        Assert.Equal("Tasks — Project", state.WindowTitle);
    }

    [Fact]
    public void BeginNewTask_InListOpensComposer_InBoardCreatesDraft()
    {
        var state = NewEmptyState();
        state.Select(SidebarItem.Inbox);
        state.SearchText = "zzz";

        state.BeginNewTask();

        Assert.True(state.IsComposing);
        Assert.Equal("", state.SearchText);
        Assert.Empty(state.Store.Tasks);

        state.IsComposing = false;
        state.ViewMode = ViewMode.Board;
        state.IsInspectorVisible = false;
        Guid? focused = null;
        state.TitleFocusRequested += (_, id) => focused = id;

        state.BeginNewTask();

        var draft = Assert.Single(state.Store.Tasks);
        Assert.Equal("", draft.Title);
        Assert.False(state.IsComposing);
        Assert.Equal(draft.Id, state.SelectedTaskId);
        Assert.True(state.IsInspectorVisible);
        Assert.Equal(draft.Id, focused);
    }

    [Theory]
    [InlineData(SidebarKind.Dashboard)]
    [InlineData(SidebarKind.Completed)]
    public void BeginNewTask_FromDashboardOrCompleted_GoesToInbox(SidebarKind kind)
    {
        var state = NewState();
        state.Select(new SidebarItem(kind, null));

        state.BeginNewTask();

        Assert.Equal(SidebarItem.Inbox, state.SidebarSelection);
        Assert.True(state.IsComposing);
    }

    [Fact]
    public void Draft_DiscardedWhenSelectionMovesAwayUntouched()
    {
        var state = InboxState(out var a, out _, out _);
        state.ViewMode = ViewMode.Board;
        state.BeginNewTask();
        var draft = state.SelectedTaskId!.Value;
        Assert.NotNull(state.Store.FindTask(draft));

        state.SelectTask(a.Id);

        Assert.Null(state.Store.FindTask(draft));
        Assert.Equal(3, state.Store.Tasks.Count);

        state.BeginNewTask();
        var second = state.SelectedTaskId!.Value;
        state.SelectTask(null);

        Assert.Null(state.Store.FindTask(second));
    }

    [Fact]
    public void Draft_KeptOnceTitled()
    {
        var state = InboxState(out var a, out _, out _);
        state.ViewMode = ViewMode.Board;
        state.BeginNewTask();
        var draft = state.SelectedTaskId!.Value;
        state.Store.UpdateTask(draft, null, t => t with { Title = "Typed" });

        state.SelectTask(a.Id);

        Assert.Equal("Typed", state.Store.FindTask(draft)!.Title);

        // Once kept it stays: selecting back and away again does not discard it.
        state.SelectTask(draft);
        state.SelectTask(a.Id);
        Assert.NotNull(state.Store.FindTask(draft));
    }

    [Fact]
    public void NewTaskDefaults_TodayDueToday_UpcomingTomorrow_ProjectFiled_CalendarSelectedDay()
    {
        Project project = null!;
        var state = NewEmptyState(store => project = store.CreateProject("Work", "folder", ProjectColor.Blue));

        state.Select(SidebarItem.Today);
        var today = state.CreateTask("a");
        Assert.Equal(D(2026, 10, 23), today.DueDate);
        Assert.Null(today.ProjectId);

        state.Select(SidebarItem.Upcoming);
        var upcoming = state.CreateTask("b");
        Assert.Equal(D(2026, 10, 24), upcoming.DueDate);

        state.Select(SidebarItem.Inbox);
        var inbox = state.CreateTask("c");
        Assert.Null(inbox.DueDate);
        Assert.Null(inbox.ProjectId);

        state.Select(SidebarItem.Project(project.Id));
        var filed = state.CreateTask("d");
        Assert.Equal(project.Id, filed.ProjectId);
        Assert.Null(filed.DueDate);

        state.ViewMode = ViewMode.Calendar;
        state.CalendarSelectedDay = D(2026, 11, 5);
        var onDay = state.CreateTask("e");
        Assert.Equal(project.Id, onDay.ProjectId);
        Assert.Equal(D(2026, 11, 5), onDay.DueDate);

        state.Select(SidebarItem.Today);
        var explicitDue = state.CreateTask("f", D(2026, 12, 1));
        Assert.Equal(D(2026, 12, 1), explicitDue.DueDate);
        var withStatus = state.CreateTask("g", status: TaskStatus.InProgress);
        Assert.Equal(TaskStatus.InProgress, withStatus.Status);
    }

    [Fact]
    public void MoveSelection_ClampsAndStartsAtEnds()
    {
        var state = InboxState(out var a, out var b, out var c);
        var order = state.NavigationOrder.Select(t => t.Id).ToList();
        Assert.Equal(new[] { a.Id, b.Id, c.Id }, order);
        Assert.Null(state.SelectedTaskId);

        state.MoveSelection(1);
        Assert.Equal(a.Id, state.SelectedTaskId);

        state.MoveSelection(-1);
        Assert.Equal(a.Id, state.SelectedTaskId);

        state.MoveSelection(1);
        state.MoveSelection(1);
        state.MoveSelection(1);
        Assert.Equal(c.Id, state.SelectedTaskId);

        state.SelectTask(null);
        state.MoveSelection(-1);
        Assert.Equal(c.Id, state.SelectedTaskId);
    }

    [Fact]
    public void MoveSelection_WithNoTasks_DoesNothing()
    {
        var state = NewEmptyState();

        state.MoveSelection(1);

        Assert.Null(state.SelectedTaskId);
    }

    [Fact]
    public void NavigationOrder_FollowsViewMode()
    {
        TaskItem done = null!, doing = null!, todo = null!, late = null!, early = null!;
        var state = NewEmptyState(store =>
        {
            done = store.CreateTask("Done", status: TaskStatus.Done);
            doing = store.CreateTask("Doing", status: TaskStatus.InProgress);
            todo = store.CreateTask("Todo");
            late = store.CreateTask("Late", dueDate: D(2026, 11, 20));
            early = store.CreateTask("Early", dueDate: D(2026, 11, 2));
        });
        state.Select(SidebarItem.Inbox);

        state.ViewMode = ViewMode.Board;
        Assert.Equal(
            new[] { todo.Id, late.Id, early.Id, doing.Id, done.Id },
            state.NavigationOrder.Select(t => t.Id).ToArray());

        state.ViewMode = ViewMode.Calendar;
        Assert.Equal(new[] { early.Id, late.Id }, state.NavigationOrder.Select(t => t.Id).ToArray());
        Assert.Equal(2, state.CalendarTasks.Count);
    }

    [Fact]
    public void Delete_SelectsNextRemainingRow()
    {
        var state = InboxState(out var a, out var b, out var c);
        state.SelectTask(b.Id);

        state.Delete(new HashSet<Guid> { b.Id });

        Assert.Null(state.Store.FindTask(b.Id));
        Assert.Equal(c.Id, state.SelectedTaskId);

        state.Delete(new HashSet<Guid> { c.Id });
        Assert.Equal(a.Id, state.SelectedTaskId);

        state.Delete(new HashSet<Guid> { a.Id });
        Assert.Null(state.SelectedTaskId);
        Assert.Empty(state.Store.Tasks);
    }

    [Fact]
    public void Delete_OfOtherRows_KeepsSelection()
    {
        var state = InboxState(out var a, out var b, out var c);
        state.SelectTask(b.Id);

        state.Delete(new HashSet<Guid> { a.Id, c.Id });

        Assert.Equal(b.Id, state.SelectedTaskId);
        Assert.Single(state.Store.Tasks);
    }

    [Fact]
    public void DeleteSelected_RemovesTheSelection()
    {
        var state = InboxState(out var a, out var b, out _);
        state.SelectTask(a.Id);

        state.DeleteSelected();

        Assert.Null(state.Store.FindTask(a.Id));
        Assert.Equal(b.Id, state.SelectedTaskId);

        state.SelectTask(null);
        state.DeleteSelected();
        Assert.Equal(2, state.Store.Tasks.Count);
    }

    [Fact]
    public void Reveal_PicksSidebarLikeMac()
    {
        Project project = null!;
        TaskItem inProject = null!, projectToday = null!, today = null!, completed = null!, inbox = null!, upcoming = null!;
        var state = NewEmptyState(store =>
        {
            project = store.CreateProject("Work", "folder", ProjectColor.Blue);
            inProject = store.CreateTask("Filed", project.Id, D(2026, 11, 20));
            projectToday = store.CreateTask("Filed today", project.Id, D(2026, 10, 23));
            today = store.CreateTask("Today", dueDate: D(2026, 10, 23));
            inbox = store.CreateTask("Inbox");
            upcoming = store.CreateTask("Later", dueDate: D(2026, 11, 20));
            _clock.UtcNow = D(2026, 10, 1);
            completed = store.CreateTask("Old", status: TaskStatus.Done);
            _clock.UtcNow = Now;
        });
        state.IsInspectorVisible = false;

        void Check(TaskItem task, SidebarItem expected)
        {
            state.SearchText = "x";
            state.IsInspectorVisible = false;
            state.Select(SidebarItem.Dashboard);
            state.SearchText = "x";

            state.Reveal(task.Id);

            Assert.Equal(expected, state.SidebarSelection);
            Assert.Equal(task.Id, state.SelectedTaskId);
            Assert.Equal("", state.SearchText);
            Assert.True(state.IsInspectorVisible);
        }

        Check(inProject, SidebarItem.Project(project.Id));
        Check(projectToday, SidebarItem.Today);
        Check(today, SidebarItem.Today);
        Check(completed, SidebarItem.Completed);
        Check(inbox, SidebarItem.Inbox);
        Check(upcoming, SidebarItem.Upcoming);

        state.Select(SidebarItem.Today);
        state.Reveal(Guid.NewGuid());
        Assert.Equal(SidebarItem.Today, state.SidebarSelection);
    }

    [Fact]
    public void DropOnSidebar_Today_SetsDueToday_KeepsTime()
    {
        TaskItem timed = null!, allDay = null!;
        var state = NewEmptyState(store =>
        {
            timed = store.CreateTask("Timed", dueDate: D(2026, 11, 2));
            allDay = store.CreateTask("All day", dueDate: D(2026, 11, 3));
        });
        state.Store.UpdateTask(timed.Id, null, t => t with { DueDate = D(2026, 11, 2, 16), HasDueTime = true });

        var handled = state.HandleDropOnSidebar(Payload(timed.Id, allDay.Id), SidebarItem.Today);

        Assert.True(handled);
        Assert.Equal(D(2026, 10, 23, 16), state.Store.FindTask(timed.Id)!.DueDate);
        Assert.Equal(D(2026, 10, 23), state.Store.FindTask(allDay.Id)!.DueDate);
    }

    [Fact]
    public void DropOnSidebar_UpcomingInboxAndProject_Refile()
    {
        Project project = null!;
        TaskItem task = null!;
        var state = NewEmptyState(store =>
        {
            project = store.CreateProject("Work", "folder", ProjectColor.Blue);
            task = store.CreateTask("Move me", project.Id);
        });

        Assert.True(state.HandleDropOnSidebar(Payload(task.Id), SidebarItem.Upcoming));
        Assert.Equal(D(2026, 10, 24), state.Store.FindTask(task.Id)!.DueDate);

        Assert.True(state.HandleDropOnSidebar(Payload(task.Id), SidebarItem.Inbox));
        Assert.Null(state.Store.FindTask(task.Id)!.ProjectId);

        Assert.True(state.HandleDropOnSidebar(Payload(task.Id), SidebarItem.Project(project.Id)));
        Assert.Equal(project.Id, state.Store.FindTask(task.Id)!.ProjectId);
    }

    [Fact]
    public void DropOnSidebar_Completed_CompletesTask()
    {
        var state = InboxState(out var a, out _, out _);

        var handled = state.HandleDropOnSidebar(Payload(a.Id), SidebarItem.Completed);

        Assert.True(handled);
        var task = state.Store.FindTask(a.Id)!;
        Assert.Equal(TaskStatus.Done, task.Status);
        Assert.NotNull(task.CompletedAt);
    }

    [Fact]
    public void DropOnSidebar_Dashboard_Rejected()
    {
        var state = InboxState(out var a, out _, out _);
        var before = state.Store.FindTask(a.Id);

        var handled = state.HandleDropOnSidebar(Payload(a.Id), SidebarItem.Dashboard);

        Assert.False(handled);
        Assert.Equal(before, state.Store.FindTask(a.Id));
    }

    [Fact]
    public void DropOnSidebar_UnrecognisedPayload_Rejected()
    {
        var state = InboxState(out _, out _, out _);

        Assert.False(state.HandleDropOnSidebar(new[] { "not a guid", "project:nope" }, SidebarItem.Today));
        Assert.False(state.HandleDropOnSidebar(Array.Empty<string>(), SidebarItem.Today));
        Assert.False(state.HandleDropOnSidebar(new[] { "project:" + P(Guid.NewGuid()) }, SidebarItem.Inbox));
    }

    [Fact]
    public void DropOnSidebar_ProjectPayload_ReordersProjects()
    {
        Project a = null!, b = null!, c = null!;
        var state = NewEmptyState(store =>
        {
            a = store.CreateProject("A", "folder", ProjectColor.Blue);
            b = store.CreateProject("B", "folder", ProjectColor.Green);
            c = store.CreateProject("C", "folder", ProjectColor.Red);
        });

        var handled = state.HandleDropOnSidebar(new[] { "project:" + P(c.Id) }, SidebarItem.Project(a.Id));

        Assert.True(handled);
        Assert.Equal(new[] { c.Id, a.Id, b.Id }, state.Store.Projects.Select(p => p.Id).ToArray());
    }

    [Fact]
    public void DropBefore_SwitchesSortToManual()
    {
        TaskItem bravo = null!, alpha = null!, charlie = null!;
        var state = NewEmptyState(store =>
        {
            bravo = store.CreateTask("Bravo");
            alpha = store.CreateTask("Alpha");
            charlie = store.CreateTask("Charlie");
        });
        state.Select(SidebarItem.Inbox);
        state.Sort = TaskSort.Title;
        var ordered = state.VisibleTasks;
        Assert.Equal(new[] { "Alpha", "Bravo", "Charlie" }, ordered.Select(t => t.Title).ToArray());

        Assert.False(state.HandleDropBefore(Payload(alpha.Id), alpha.Id, ordered));
        Assert.Equal(TaskSort.Title, state.Sort);
        Assert.False(state.HandleDropBefore(new[] { "junk" }, alpha.Id, ordered));

        var handled = state.HandleDropBefore(Payload(charlie.Id), alpha.Id, ordered);

        Assert.True(handled);
        Assert.Equal(TaskSort.Manual, state.Sort);
        Assert.Equal(new[] { "Bravo", "Charlie", "Alpha" }, state.VisibleTasks.Select(t => t.Title).ToArray());
    }

    [Fact]
    public void DropBefore_InManualSort_ReordersWithoutChangingSort()
    {
        var state = InboxState(out var a, out _, out var c);

        Assert.True(state.HandleDropBefore(Payload(c.Id), a.Id, state.VisibleTasks));

        Assert.Equal(TaskSort.Manual, state.Sort);
        Assert.Equal(c.Id, state.VisibleTasks[0].Id);
    }

    [Fact]
    public void DropOnStatus_ChangesStatusThenOrder()
    {
        TaskItem a = null!, d = null!, e = null!, c = null!;
        var state = NewEmptyState(store =>
        {
            a = store.CreateTask("A");
            store.CreateTask("B");
            d = store.CreateTask("D", status: TaskStatus.InProgress);
            e = store.CreateTask("E", status: TaskStatus.InProgress);
            c = store.CreateTask("C");
        });
        var column = new[] { d, e };

        var handled = state.HandleDropOnStatus(Payload(c.Id), TaskStatus.InProgress, d.Id, column);

        Assert.True(handled);
        var moved = state.Store.FindTask(c.Id)!;
        Assert.Equal(TaskStatus.InProgress, moved.Status);
        Assert.True(moved.SortOrder < state.Store.FindTask(d.Id)!.SortOrder);
        Assert.True(moved.SortOrder > state.Store.FindTask(a.Id)!.SortOrder);

        // Without a target card only the status changes.
        var before = state.Store.FindTask(a.Id)!.SortOrder;
        Assert.True(state.HandleDropOnStatus(Payload(a.Id), TaskStatus.Done));
        Assert.Equal(TaskStatus.Done, state.Store.FindTask(a.Id)!.Status);
        Assert.Equal(before, state.Store.FindTask(a.Id)!.SortOrder);

        Assert.False(state.HandleDropOnStatus(new[] { "junk" }, TaskStatus.Done));
    }

    [Fact]
    public void DropOnDay_ReschedulesKeepingTime()
    {
        TaskItem timed = null!, allDay = null!, undated = null!;
        var state = NewEmptyState(store =>
        {
            timed = store.CreateTask("Timed", dueDate: D(2026, 10, 28));
            allDay = store.CreateTask("All day", dueDate: D(2026, 10, 29));
            undated = store.CreateTask("Undated");
        });
        state.Store.UpdateTask(timed.Id, null, t => t with { DueDate = D(2026, 10, 28, 9, 15), HasDueTime = true });

        var handled = state.HandleDropOnDay(Payload(timed.Id, allDay.Id, undated.Id), D(2026, 11, 3));

        Assert.True(handled);
        Assert.Equal(D(2026, 11, 3, 9, 15), state.Store.FindTask(timed.Id)!.DueDate);
        Assert.Equal(D(2026, 11, 3), state.Store.FindTask(allDay.Id)!.DueDate);
        Assert.Equal(D(2026, 11, 3), state.Store.FindTask(undated.Id)!.DueDate);
        Assert.False(state.HandleDropOnDay(new[] { "junk" }, D(2026, 11, 3)));
    }

    [Theory]
    [InlineData(KeyCommand.Space)]
    [InlineData(KeyCommand.Delete)]
    [InlineData(KeyCommand.Down)]
    [InlineData(KeyCommand.Up)]
    [InlineData(KeyCommand.Enter)]
    [InlineData(KeyCommand.Escape)]
    public void HandleKey_IsIgnoredWhileTextInputFocused(KeyCommand key)
    {
        var state = InboxState(out _, out var b, out _);
        state.SelectTask(b.Id);
        var tasks = state.Store.Tasks;
        var changes = 0;
        state.Changed += (_, _) => changes++;
        var focusRequests = 0;
        state.TitleFocusRequested += (_, _) => focusRequests++;

        var handled = state.HandleKey(key, isTextInputFocused: true, hasModifiers: false, isDialogOpen: false);

        Assert.False(handled);
        Assert.Equal(b.Id, state.SelectedTaskId);
        Assert.Same(tasks, state.Store.Tasks);
        Assert.Equal(0, changes);
        Assert.Equal(0, focusRequests);
    }

    [Fact]
    public void HandleKey_IgnoredWithModifiers_OrDialog_OrDashboard()
    {
        var state = InboxState(out _, out var b, out _);
        state.SelectTask(b.Id);
        var tasks = state.Store.Tasks;

        foreach (var key in Enum.GetValues<KeyCommand>())
        {
            Assert.False(state.HandleKey(key, false, hasModifiers: true, isDialogOpen: false));
            Assert.False(state.HandleKey(key, false, hasModifiers: false, isDialogOpen: true));
        }

        Assert.Equal(b.Id, state.SelectedTaskId);
        Assert.Same(tasks, state.Store.Tasks);

        state.Select(SidebarItem.Dashboard);
        state.SelectTask(b.Id);
        foreach (var key in Enum.GetValues<KeyCommand>())
        {
            Assert.False(state.HandleKey(key, false, false, false));
        }

        Assert.Equal(b.Id, state.SelectedTaskId);
        Assert.Same(tasks, state.Store.Tasks);
    }

    [Fact]
    public void HandleKey_ArrowsSpaceAndDelete_ActOnTasks()
    {
        var state = InboxState(out var a, out var b, out var c);

        Assert.True(state.HandleKey(KeyCommand.Down, false, false, false));
        Assert.Equal(a.Id, state.SelectedTaskId);
        Assert.True(state.HandleKey(KeyCommand.Down, false, false, false));
        Assert.Equal(b.Id, state.SelectedTaskId);
        Assert.True(state.HandleKey(KeyCommand.Up, false, false, false));
        Assert.Equal(a.Id, state.SelectedTaskId);

        Assert.True(state.HandleKey(KeyCommand.Space, false, false, false));
        Assert.True(state.Store.FindTask(a.Id)!.IsCompleted);
        Assert.True(state.HandleKey(KeyCommand.Space, false, false, false));
        Assert.False(state.Store.FindTask(a.Id)!.IsCompleted);

        Assert.True(state.HandleKey(KeyCommand.Delete, false, false, false));
        Assert.Null(state.Store.FindTask(a.Id));
        Assert.Equal(b.Id, state.SelectedTaskId);
        Assert.NotNull(state.Store.FindTask(c.Id));
    }

    [Fact]
    public void HandleKey_WithoutSelection_ReturnsFalseForTaskKeys()
    {
        var state = InboxState(out _, out _, out _);

        Assert.False(state.HandleKey(KeyCommand.Delete, false, false, false));
        Assert.False(state.HandleKey(KeyCommand.Enter, false, false, false));
        Assert.False(state.HandleKey(KeyCommand.Space, false, false, false));
        Assert.False(state.HandleKey(KeyCommand.Escape, false, false, false));
        Assert.Equal(3, state.Store.Tasks.Count);
    }

    [Fact]
    public void HandleKey_EnterFocusesTitle()
    {
        var state = InboxState(out _, out var b, out _);
        state.SelectTask(b.Id);
        state.IsInspectorVisible = false;
        var requested = new List<Guid>();
        state.TitleFocusRequested += (_, id) => requested.Add(id);

        var handled = state.HandleKey(KeyCommand.Enter, false, false, false);

        Assert.True(handled);
        Assert.Equal(new[] { b.Id }, requested);
        Assert.True(state.IsInspectorVisible);
    }

    [Fact]
    public void HandleKey_EscapeClearsSelection()
    {
        var state = InboxState(out var a, out _, out _);
        state.SelectTask(a.Id);

        Assert.True(state.HandleKey(KeyCommand.Escape, false, false, false));
        Assert.Null(state.SelectedTaskId);
        Assert.False(state.HandleKey(KeyCommand.Escape, false, false, false));
    }

    [Fact]
    public void HandleClick_SelectsOnce_AndOpensDetailsOnDouble()
    {
        var state = InboxState(out var a, out var b, out _);
        state.IsInspectorVisible = false;
        var focus = 0;
        state.TitleFocusRequested += (_, _) => focus++;

        state.HandleClick(a.Id, isDoubleClick: false);
        Assert.Equal(a.Id, state.SelectedTaskId);
        Assert.False(state.IsInspectorVisible);

        state.HandleClick(b.Id, isDoubleClick: true);
        Assert.Equal(b.Id, state.SelectedTaskId);
        Assert.True(state.IsInspectorVisible);
        Assert.Equal(0, focus);
    }

    [Fact]
    public void Actions_ToggleDuplicateAndEdit()
    {
        var state = InboxState(out var a, out var b, out _);
        state.SelectTask(a.Id);

        state.ToggleSelectedCompleted();
        Assert.True(state.Store.FindTask(a.Id)!.IsCompleted);
        state.ToggleCompleted(a.Id);
        Assert.False(state.Store.FindTask(a.Id)!.IsCompleted);

        state.DuplicateSelected();
        Assert.Equal(4, state.Store.Tasks.Count);
        Assert.NotEqual(a.Id, state.SelectedTaskId);
        Assert.Equal("One", state.SelectedTask!.Title);

        state.SetPriority(Priority.High, new[] { a.Id, b.Id });
        Assert.Equal(Priority.High, state.Store.FindTask(a.Id)!.Priority);
        Assert.Equal(Priority.High, state.Store.FindTask(b.Id)!.Priority);

        var project = state.Store.CreateProject("P", "folder", ProjectColor.Red);
        state.Move(new[] { a.Id }, project.Id);
        Assert.Equal(project.Id, state.Store.FindTask(a.Id)!.ProjectId);

        state.SetDueDay(D(2026, 11, 9), new[] { b.Id });
        Assert.Equal(D(2026, 11, 9), state.Store.FindTask(b.Id)!.DueDate);
        state.SetDueDay(null, new[] { b.Id });
        Assert.Null(state.Store.FindTask(b.Id)!.DueDate);
    }

    [Fact]
    public void SetDueOffset_IsTodayPlusDays()
    {
        var state = InboxState(out var a, out _, out _);

        state.SetDueOffset(0, a.Id);
        Assert.Equal(D(2026, 10, 23), state.Store.FindTask(a.Id)!.DueDate);

        state.SetDueOffset(1, a.Id);
        Assert.Equal(D(2026, 10, 24), state.Store.FindTask(a.Id)!.DueDate);

        state.SetDueOffset(7, a.Id);
        Assert.Equal(D(2026, 10, 30), state.Store.FindTask(a.Id)!.DueDate);
    }

    [Fact]
    public void ProjectRequests_RaiseEvents_AndConfirmedDeleteLeavesTheProject()
    {
        Project project = null!;
        TaskItem task = null!;
        var state = NewEmptyState(store =>
        {
            project = store.CreateProject("Work", "folder", ProjectColor.Blue);
            task = store.CreateTask("Filed", project.Id);
        });
        var editor = new List<Guid?>();
        var deletion = new List<Project>();
        state.ProjectEditorRequested += (_, id) => editor.Add(id);
        state.ProjectDeletionRequested += (_, p) => deletion.Add(p);

        state.RequestProjectEditor(null);
        state.RequestProjectEditor(project.Id);
        state.RequestProjectDeletion(project.Id);
        state.RequestProjectDeletion(Guid.NewGuid());

        Assert.Equal(new Guid?[] { null, project.Id }, editor);
        Assert.Equal(new[] { project }, deletion);

        state.Select(SidebarItem.Project(project.Id));
        state.DeleteProjectConfirmed(project.Id);

        Assert.Empty(state.Store.Projects);
        Assert.Equal(SidebarItem.Inbox, state.SidebarSelection);
        Assert.Null(state.Store.FindTask(task.Id)!.ProjectId);
    }

    [Fact]
    public void FocusSearch_RaisesEvent()
    {
        var state = NewState();
        var count = 0;
        state.SearchFocusRequested += (_, _) => count++;

        state.FocusSearch();

        Assert.Equal(1, count);
    }

    [Fact]
    public void Changed_RaisedForPropertyStoreAndTick()
    {
        var state = InboxState(out var a, out _, out _);
        var changes = 0;
        state.Changed += (_, _) => changes++;

        state.SearchText = "x";
        Assert.True(changes >= 1);

        changes = 0;
        state.Store.UpdateTask(a.Id, null, t => t with { Title = "Renamed" });
        Assert.True(changes >= 1);

        changes = 0;
        state.Tick();
        Assert.True(changes >= 1);
    }

    [Fact]
    public void Changed_IsRaisedOnceForACompoundAction()
    {
        var state = InboxState(out var a, out _, out _);
        state.ViewMode = ViewMode.Board;
        state.BeginNewTask();
        var changes = 0;
        state.Changed += (_, _) => changes++;

        // Moving off the draft changes the selection and discards the draft.
        state.SelectTask(a.Id);

        Assert.Equal(1, changes);
    }

    [Fact]
    public void CalendarMonth_DefaultsToNow()
    {
        var state = NewState();

        Assert.Equal(_clock.UtcNow, state.CalendarMonth);
    }

    [Fact]
    public void Settings_ArePersistedOnChange()
    {
        var state = NewState();
        Assert.Empty(_saved);

        state.ViewMode = ViewMode.Board;
        Assert.Equal(ViewMode.Board, _saved[^1].ViewMode);

        state.Sort = TaskSort.Priority;
        Assert.Equal(TaskSort.Priority, _saved[^1].Sort);

        state.ShowCompleted = false;
        Assert.False(_saved[^1].ShowCompleted);

        state.IsInspectorVisible = false;
        Assert.False(_saved[^1].InspectorVisible);

        state.Appearance = AppearancePreference.Dark;
        Assert.Equal(AppearancePreference.Dark, _saved[^1].Appearance);

        state.SidebarWidth = 300;
        state.InspectorWidth = 400;
        Assert.Equal(300, _saved[^1].SidebarWidth);
        Assert.Equal(400, _saved[^1].InspectorWidth);
        Assert.Equal(
            new AppSettings(ViewMode.Board, TaskSort.Priority, false, false, AppearancePreference.Dark, 300, 400),
            _saved[^1]);

        var count = _saved.Count;
        state.ViewMode = ViewMode.Board;
        Assert.Equal(count, _saved.Count);
    }

    [Fact]
    public void Settings_AreReadFromTheGivenSettings_AndWidthsAreClamped()
    {
        var state = NewState(new AppSettings(
            ViewMode.Calendar, TaskSort.Title, false, false, AppearancePreference.Light, SidebarWidth: 900, InspectorWidth: 50));

        Assert.Equal(ViewMode.Calendar, state.ViewMode);
        Assert.Equal(TaskSort.Title, state.Sort);
        Assert.False(state.ShowCompleted);
        Assert.False(state.IsInspectorVisible);
        Assert.Equal(AppearancePreference.Light, state.Appearance);
        Assert.Equal(340, state.SidebarWidth);
        Assert.Equal(290, state.InspectorWidth);
        Assert.Empty(_saved);

        state.SidebarWidth = 10;
        state.InspectorWidth = 5000;
        Assert.Equal(200, state.SidebarWidth);
        Assert.Equal(480, state.InspectorWidth);
        Assert.Equal(200, _saved[^1].SidebarWidth);
        Assert.Equal(480, _saved[^1].InspectorWidth);
    }

    [Fact]
    public void NextWeekStart_IsComingMonday()
    {
        // Friday 2026-10-23, Monday-first week.
        var state = NewState();
        Assert.Equal(D(2026, 10, 26), state.NextWeekStart());

        // On a Monday it is the Monday after.
        _clock.UtcNow = D(2026, 10, 26, 8);
        Assert.Equal(D(2026, 11, 2), state.NextWeekStart());

        // On a Sunday it is the next day.
        _clock.UtcNow = D(2026, 10, 25, 20);
        Assert.Equal(D(2026, 10, 26), state.NextWeekStart());
    }

    [Fact]
    public void NextWeekStart_UsesTheFirstWeekdayUnlessSunday()
    {
        var sundayFirst = new SettableClock(new TodoCalendar(Berlin.TimeZone, DayOfWeek.Sunday), Now);
        var saturdayFirst = new SettableClock(new TodoCalendar(Berlin.TimeZone, DayOfWeek.Saturday), Now);

        Assert.Equal(D(2026, 10, 26), StateWith(sundayFirst).NextWeekStart());
        Assert.Equal(D(2026, 10, 24), StateWith(saturdayFirst).NextWeekStart());
    }

    private AppState StateWith(IClock clock) =>
        new(NewStore(), clock, new AppSettings(), _saved.Add);

    private sealed class SettableClock(TodoCalendar calendar, DateTime now) : IClock
    {
        public DateTime UtcNow { get; } = now;

        public TodoCalendar Calendar { get; } = calendar;
    }
}
