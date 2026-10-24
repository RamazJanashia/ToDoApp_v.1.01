using TodoCore;
using static TodoApp.State.Tests.TestSupport;
using TaskStatus = TodoCore.TaskStatus;

namespace TodoApp.State.Tests;

public sealed class TaskStoreTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly FakeClock _clock = new();
    private readonly ManualDebouncer _debouncer = new();
    private readonly FakeReminders _reminders = new();

    public void Dispose() => _temp.Dispose();

    private TaskStore NewStore() => new(_temp.Path, _clock, _debouncer, _reminders);

    private static TaskItem ByTitle(TaskStore store, string title) => store.Tasks.Single(t => t.Title == title);

    private static AppData ReadFile(string directory) => new DataFileStore(directory).Load()!;

    [Fact]
    public void FirstRun_SeedsSampleAndSaves()
    {
        var store = NewStore();
        store.SaveNow();

        Assert.True(File.Exists(Path.Combine(_temp.Path, "tasks.json")));
        Assert.Equal(14, ReadFile(_temp.Path).Tasks.Count);
        Assert.Equal(14, store.Tasks.Count);
        Assert.Equal(3, store.Projects.Count);
    }

    [Fact]
    public void Constructor_ActivatesRemindersBeforeSyncing()
    {
        NewStore();

        Assert.Equal("Activate", _reminders.Calls[0]);
        Assert.Contains("Sync", _reminders.Calls);
    }

    [Fact]
    public void ExistingFile_IsLoaded()
    {
        var first = NewStore();
        var created = first.CreateTask("Persisted");
        first.SaveNow();

        var second = new TaskStore(_temp.Path, _clock, new ManualDebouncer(), new FakeReminders());

        Assert.Equal(created, second.FindTask(created.Id));
        Assert.Equal(15, second.Tasks.Count);
    }

    [Fact]
    public void CreateTask_UsesStartOfDayAndNextSortOrder()
    {
        var store = NewStore();
        var project = store.Projects[0];

        var task = store.CreateTask("Write tests", project.Id, D(2026, 10, 28, 14, 30), TaskStatus.Done, Priority.High);

        Assert.Equal(D(2026, 10, 28), task.DueDate);
        Assert.Equal(15, task.SortOrder);
        Assert.Equal(_clock.UtcNow, task.CreatedAt);
        Assert.Equal(_clock.UtcNow, task.CompletedAt);
        Assert.Equal(project.Id, task.ProjectId);
        Assert.Equal(Priority.High, task.Priority);
        Assert.Equal(task, store.Tasks[^1]);
        Assert.Equal("New Task", store.Undo.UndoActionName);
    }

    [Fact]
    public void UpdateTask_WithoutUndoName_RegistersNoUndo()
    {
        var store = NewStore();
        var task = ByTitle(store, "Buy groceries");
        var changed = 0;
        store.Changed += (_, _) => changed++;

        store.UpdateTask(task.Id, null, t => t with { Title = "Buy more groceries" });

        Assert.Equal("Buy more groceries", store.FindTask(task.Id)!.Title);
        Assert.False(store.Undo.CanUndo);
        Assert.Equal(1, changed);
        Assert.True(_debouncer.HasPending);
    }

    [Fact]
    public void UpdateTask_WithUndoName_RegistersUndo()
    {
        var store = NewStore();
        var task = ByTitle(store, "Buy groceries");

        store.UpdateTask(task.Id, "Set Priority", t => t with { Priority = Priority.High });
        Assert.Equal("Set Priority", store.Undo.UndoActionName);

        store.Undo.Undo();
        Assert.Equal(task, store.FindTask(task.Id));
        store.Undo.Redo();
        Assert.Equal(Priority.High, store.FindTask(task.Id)!.Priority);
    }

    [Fact]
    public void UpdateTask_NoChange_DoesNothing()
    {
        var store = NewStore();
        var task = ByTitle(store, "Buy groceries");
        var changed = 0;
        store.Changed += (_, _) => changed++;

        store.UpdateTask(task.Id, "Edit", t => t);
        store.UpdateTask(task.Id, null, t => t with { Title = t.Title });
        store.UpdateTask(Guid.NewGuid(), "Edit", t => t with { Title = "x" });

        Assert.Equal(0, changed);
        Assert.False(store.Undo.CanUndo);
        Assert.False(_debouncer.HasPending);
    }

    [Fact]
    public void SetStatus_Done_SpawnsOccurrence_UndoRestores()
    {
        var store = NewStore();
        var report = ByTitle(store, "Send weekly report");

        store.SetStatus(new[] { report.Id }, TaskStatus.Done);

        Assert.Equal(15, store.Tasks.Count);
        var done = store.FindTask(report.Id)!;
        Assert.Equal(TaskStatus.Done, done.Status);
        Assert.Equal(_clock.UtcNow, done.CompletedAt);
        var next = store.FindTask(done.NextOccurrenceId)!;
        Assert.Equal(TaskStatus.Todo, next.Status);
        Assert.NotNull(next.Recurrence);
        Assert.Equal("Complete Task", store.Undo.UndoActionName);

        store.Undo.Undo();

        Assert.Equal(14, store.Tasks.Count);
        Assert.Equal(report, store.FindTask(report.Id));
        Assert.Equal("Complete Task", store.Undo.RedoActionName);
    }

    [Fact]
    public void ToggleCompleted_ReopensWithChangeStatusName()
    {
        var store = NewStore();
        var invoices = ByTitle(store, "Review invoices");

        store.ToggleCompleted(invoices.Id);

        Assert.Equal(TaskStatus.Todo, store.FindTask(invoices.Id)!.Status);
        Assert.Equal("Change Status", store.Undo.UndoActionName);

        store.ToggleCompleted(invoices.Id);
        Assert.Equal(TaskStatus.Done, store.FindTask(invoices.Id)!.Status);
        Assert.Equal("Complete Task", store.Undo.UndoActionName);
    }

    [Fact]
    public void Delete_UndoName_PluralForTwo()
    {
        var store = NewStore();
        var a = ByTitle(store, "Buy groceries");
        var b = ByTitle(store, "Organize photo library");

        store.DeleteTasks(new HashSet<Guid> { a.Id });
        Assert.Equal("Delete Task", store.Undo.UndoActionName);

        store.DeleteTasks(new HashSet<Guid> { b.Id, ByTitle(store, "Gym").Id });
        Assert.Equal("Delete Tasks", store.Undo.UndoActionName);
        Assert.Equal(11, store.Tasks.Count);

        store.Undo.Undo();
        Assert.Equal(13, store.Tasks.Count);
        store.Undo.Undo();
        Assert.Equal(a, store.FindTask(a.Id));
    }

    [Fact]
    public void Delete_UnknownIds_DoesNothing()
    {
        var store = NewStore();

        store.DeleteTasks(new HashSet<Guid> { Guid.NewGuid() });

        Assert.False(store.Undo.CanUndo);
        Assert.Equal(14, store.Tasks.Count);
    }

    [Fact]
    public void Duplicate_InsertsAfterOriginalWithMidpointOrder()
    {
        var store = NewStore();
        var original = ByTitle(store, "Review contract");
        _clock.UtcNow = Now.AddHours(1);

        var copy = store.DuplicateTask(original.Id)!;

        var index = store.Tasks.ToList().FindIndex(t => t.Id == original.Id);
        Assert.Equal(copy, store.Tasks[index + 1]);
        Assert.NotEqual(original.Id, copy.Id);
        Assert.Equal(1.5, copy.SortOrder);
        Assert.Equal(_clock.UtcNow, copy.CreatedAt);
        Assert.Equal(original.Title, copy.Title);
        Assert.Equal(2, copy.Subtasks.Count);
        Assert.Equal(original.Subtasks.Select(s => s.Title), copy.Subtasks.Select(s => s.Title));
        Assert.Empty(copy.Subtasks.Select(s => s.Id).Intersect(original.Subtasks.Select(s => s.Id)));
        Assert.Equal("Duplicate Task", store.Undo.UndoActionName);

        var last = ByTitle(store, "Gym");
        Assert.Equal(last.SortOrder + 1, store.DuplicateTask(last.Id)!.SortOrder);
        Assert.Null(store.DuplicateTask(Guid.NewGuid()));
    }

    [Fact]
    public void DiscardIfBlank_RemovesOnlyEmptyAndIsNotUndoable()
    {
        var store = NewStore();
        var blank = store.CreateTask("  ");
        var filled = store.CreateTask("Something");
        var withNotes = store.CreateTask("");
        store.UpdateTask(withNotes.Id, null, t => t with { Notes = "details" });
        var withSubtask = store.CreateTask("");
        store.UpdateTask(withSubtask.Id, null, t => t with { Subtasks = ValueList.Of(new Subtask { Title = "step" }) });

        store.DiscardIfBlank(filled.Id);
        store.DiscardIfBlank(withNotes.Id);
        store.DiscardIfBlank(withSubtask.Id);
        Assert.Equal(14 + 4, store.Tasks.Count);

        store.DiscardIfBlank(blank.Id);

        Assert.Equal(14 + 3, store.Tasks.Count);
        Assert.Null(store.FindTask(blank.Id));
        // Four creations are on the undo stack; discarding added nothing.
        for (var i = 0; i < 4; i++)
        {
            Assert.Equal("New Task", store.Undo.UndoActionName);
            store.Undo.Undo();
        }

        Assert.False(store.Undo.CanUndo);
    }

    [Fact]
    public void DeleteProject_MovesTasksToInbox()
    {
        var store = NewStore();
        var work = store.Projects.Single(p => p.Name == "Work");
        var belonging = store.Tasks.Where(t => t.ProjectId == work.Id).Select(t => t.Id).ToList();
        Assert.NotEmpty(belonging);

        store.DeleteProject(work.Id);

        Assert.Null(store.FindProject(work.Id));
        Assert.Equal(14, store.Tasks.Count);
        Assert.All(belonging, id => Assert.Null(store.FindTask(id)!.ProjectId));
        Assert.Equal("Delete Project", store.Undo.UndoActionName);

        store.Undo.Undo();

        Assert.NotNull(store.FindProject(work.Id));
        Assert.All(belonging, id => Assert.Equal(work.Id, store.FindTask(id)!.ProjectId));
    }

    [Fact]
    public void MoveProject_Reorders()
    {
        var store = NewStore();
        var names = store.Projects.Select(p => p.Name).ToList();
        Assert.Equal(new[] { "Work", "Personal", "Projects" }, names);

        store.MoveProject(store.Projects[2].Id, store.Projects[0].Id);
        Assert.Equal(new[] { "Projects", "Work", "Personal" }, store.Projects.Select(p => p.Name));
        Assert.Equal("Move Project", store.Undo.UndoActionName);

        store.MoveProject(store.Projects[0].Id, store.Projects[0].Id);
        Assert.Equal(new[] { "Projects", "Work", "Personal" }, store.Projects.Select(p => p.Name));

        store.Undo.Undo();
        Assert.Equal(names, store.Projects.Select(p => p.Name));
    }

    [Fact]
    public void CreateAndUpdateProject_RegisterUndo()
    {
        var store = NewStore();

        var project = store.CreateProject("Garden", "leaf", ProjectColor.Green);
        Assert.Equal("New Project", store.Undo.UndoActionName);
        Assert.Equal(project, store.Projects[^1]);

        store.UpdateProject(project.Id, p => p with { Name = "Allotment" });
        Assert.Equal("Edit Project", store.Undo.UndoActionName);
        Assert.Equal("Allotment", store.FindProject(project.Id)!.Name);

        store.Undo.Undo();
        Assert.Equal("Garden", store.FindProject(project.Id)!.Name);
    }

    [Fact]
    public void MoveTask_PlacesBeforeTargetAndIsUndoable()
    {
        var store = NewStore();
        var ordered = store.Tasks.OrderBy(t => t.SortOrder).ToList();
        var last = ordered[^1];
        var second = ordered[1];

        store.MoveTask(last.Id, second.Id, ordered);

        Assert.Equal((ordered[0].SortOrder + second.SortOrder) / 2, store.FindTask(last.Id)!.SortOrder);
        Assert.Equal("Move Task", store.Undo.UndoActionName);
    }

    [Fact]
    public void Undo_KeepsCurrentTextOfSurvivingTasks()
    {
        var store = NewStore();
        var task = ByTitle(store, "Buy groceries");

        store.UpdateTask(task.Id, null, t => t with { Title = "Buy groceries today" });
        store.UpdateTask(task.Id, "Set Priority", t => t with { Priority = Priority.High });
        store.UpdateTask(task.Id, null, t => t with { Title = "Buy groceries and milk", Notes = "typed later" });

        store.Undo.Undo();

        var undone = store.FindTask(task.Id)!;
        Assert.Equal(Priority.None, undone.Priority);
        Assert.Equal("Buy groceries and milk", undone.Title);
        Assert.Equal("typed later", undone.Notes);

        store.Undo.Redo();
        Assert.Equal(Priority.High, store.FindTask(task.Id)!.Priority);
        Assert.Equal("Buy groceries and milk", store.FindTask(task.Id)!.Title);
    }

    [Fact]
    public void Undo_KeepsCurrentSubtaskTitles()
    {
        var store = NewStore();
        var contract = ByTitle(store, "Review contract");
        var first = contract.Subtasks[0];

        store.UpdateTask(contract.Id, "Set Priority", t => t with { Priority = Priority.Low });
        store.UpdateTask(contract.Id, null, t => t with { Subtasks = t.Subtasks.SetItem(0, first with { Title = "Renamed" }) });

        store.Undo.Undo();

        var undone = store.FindTask(contract.Id)!;
        Assert.Equal(Priority.High, undone.Priority);
        Assert.Equal("Renamed", undone.Subtasks[0].Title);
        Assert.Equal(contract.Subtasks[1], undone.Subtasks[1]);
    }

    [Fact]
    public void Changes_AreSavedAfterDebounce_AndRemindersSynced()
    {
        var store = NewStore();
        var task = ByTitle(store, "Buy groceries");
        var syncsBefore = _reminders.Syncs.Count;

        store.UpdateTask(task.Id, "Set Priority", t => t with { Priority = Priority.High });

        Assert.True(_debouncer.HasPending);
        Assert.Equal(Priority.None, ReadFile(_temp.Path).Tasks.Single(t => t.Id == task.Id).Priority);
        Assert.Equal(syncsBefore, _reminders.Syncs.Count);

        _debouncer.Flush();

        Assert.False(_debouncer.HasPending);
        Assert.Equal(Priority.High, ReadFile(_temp.Path).Tasks.Single(t => t.Id == task.Id).Priority);
        Assert.Equal(syncsBefore + 1, _reminders.Syncs.Count);
        Assert.Equal(Priority.High, _reminders.Syncs[^1].Tasks.Single(t => t.Id == task.Id).Priority);
    }

    [Fact]
    public void SaveNow_CancelsPendingSave()
    {
        var store = NewStore();
        store.CreateTask("x");
        Assert.True(_debouncer.HasPending);

        store.SaveNow();

        Assert.False(_debouncer.HasPending);
    }

    [Fact]
    public void Save_FailureIsLoggedAndAppKeepsState()
    {
        AppLog.Configure(_temp.Path);
        var store = NewStore();
        var task = ByTitle(store, "Buy groceries");
        var file = Path.Combine(_temp.Path, "tasks.json");
        File.Delete(file);
        var blocker = Directory.CreateDirectory(file);
        blocker.Attributes |= FileAttributes.ReadOnly;
        var syncsBefore = _reminders.Syncs.Count;

        store.UpdateTask(task.Id, "Set Priority", t => t with { Priority = Priority.High });
        var exception = Record.Exception(_debouncer.Flush);

        Assert.Null(exception);
        Assert.Equal(Priority.High, store.FindTask(task.Id)!.Priority);
        Assert.Equal(syncsBefore + 1, _reminders.Syncs.Count);
        var log = File.ReadAllText(Path.Combine(_temp.Path, "TodoApp.log"));
        Assert.Contains("Couldn't save", log);
    }
}
