using TodoCore;
using TaskStatus = TodoCore.TaskStatus;

namespace TodoApp.State;

/// <summary>
/// The single source of truth for tasks and projects.
/// Main-thread only. Every mutation goes through a method here so that saving, reminder scheduling and undo stay
/// in one place. Changes are written to disk automatically, a moment after the last edit.
/// </summary>
public sealed class TaskStore
{
    private readonly DataFileStore _fileStore;
    private readonly IClock _clock;
    private readonly IDebouncer _saveDebouncer;
    private readonly IReminderScheduler _reminders;

    // Off for the session when an unreadable data file couldn't be set aside, so it is never overwritten.
    private bool _canSave = true;

    // Replaced, never mutated, so a snapshot is just a pair of references.
    private IReadOnlyList<TaskItem> _tasks = Array.Empty<TaskItem>();
    private IReadOnlyList<Project> _projects = Array.Empty<Project>();

    public TaskStore(string directory, IClock clock, IDebouncer saveDebouncer, IReminderScheduler reminders)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(saveDebouncer);
        ArgumentNullException.ThrowIfNull(reminders);
        _fileStore = new DataFileStore(directory);
        Attachments = new AttachmentStorage(Path.Combine(directory, "Attachments"));
        _clock = clock;
        _saveDebouncer = saveDebouncer;
        _reminders = reminders;

        // Activate first: it clears old pending reminders, which loading then schedules afresh.
        _reminders.Activate();
        Load();
    }

    public IReadOnlyList<TaskItem> Tasks => _tasks;

    public IReadOnlyList<Project> Projects => _projects;

    /// <summary>Set when the saved file couldn't be read at launch; shown once to the user.</summary>
    public string? LoadIssue { get; set; }

    public UndoManager Undo { get; } = new();

    public AttachmentStorage Attachments { get; }

    public event EventHandler? Changed;

    // Lookup

    public TaskItem? FindTask(Guid? id) => id is { } value ? _tasks.FirstOrDefault(t => t.Id == value) : null;

    public Project? FindProject(Guid? id) => id is { } value ? _projects.FirstOrDefault(p => p.Id == value) : null;

    // Tasks

    public TaskItem CreateTask(
        string title,
        Guid? projectId = null,
        DateTime? dueDate = null,
        TaskStatus status = TaskStatus.Todo,
        Priority priority = Priority.None)
    {
        var now = _clock.UtcNow;
        var task = new TaskItem
        {
            Title = title,
            Status = status,
            Priority = priority,
            ProjectId = projectId,
            DueDate = dueDate is { } due ? _clock.Calendar.StartOfDay(due) : null,
            SortOrder = (_tasks.Count == 0 ? 0 : _tasks.Max(t => t.SortOrder)) + 1,
            CreatedAt = now,
            CompletedAt = status == TaskStatus.Done ? now : null,
        };
        Undoable("New Task", (tasks, _) => tasks.Add(task));
        return task;
    }

    /// <summary>
    /// Applies <paramref name="change"/> to one task. Pass an <paramref name="undoName"/> for discrete edits
    /// (priority, dates…); leave it null for typing, which text fields already make undoable.
    /// </summary>
    public void UpdateTask(Guid id, string? undoName, Func<TaskItem, TaskItem> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        var index = IndexOfTask(id);
        if (index < 0) return;
        var current = _tasks[index];
        var task = change(current);
        if (task == current) return;
        if (undoName is not null)
        {
            Undoable(undoName, (tasks, _) => tasks[index] = task);
        }
        else
        {
            Apply((tasks, _) => tasks[index] = task);
            DidChange();
        }
    }

    public void UpdateTasks(IEnumerable<Guid> ids, string undoName, Func<TaskItem, TaskItem> change)
    {
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentNullException.ThrowIfNull(change);
        var targets = ids.ToHashSet();
        Undoable(undoName, (tasks, _) =>
        {
            for (var index = 0; index < tasks.Count; index++)
            {
                if (targets.Contains(tasks[index].Id)) tasks[index] = change(tasks[index]);
            }
        });
    }

    public void SetStatus(IEnumerable<Guid> ids, TaskStatus status)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var targets = ids.ToHashSet();
        var name = status == TaskStatus.Done ? "Complete Task" : "Change Status";
        Undoable(name, (tasks, _) => TaskOperations.SetStatus(status, targets, tasks, _clock.UtcNow, _clock.Calendar));
    }

    public void ToggleCompleted(Guid id)
    {
        if (FindTask(id) is not { } task) return;
        SetStatus(new[] { id }, task.IsCompleted ? TaskStatus.Todo : TaskStatus.Done);
    }

    public TaskItem? DuplicateTask(Guid id)
    {
        var index = IndexOfTask(id);
        if (index < 0) return null;
        var original = _tasks[index];
        var now = _clock.UtcNow;
        var following = _tasks.Select(t => t.SortOrder).Where(order => order > original.SortOrder).ToList();
        var copy = original with
        {
            Id = Guid.NewGuid(),
            CreatedAt = now,
            Subtasks = new ValueList<Subtask>(original.Subtasks.Select(s => new Subtask { Title = s.Title, IsDone = s.IsDone })),
            Attachments = new ValueList<Attachment>(original.Attachments.Select(a => new Attachment
            {
                Kind = a.Kind,
                Name = a.Name,
                Location = a.Location,
                AddedAt = now,
            })),
            SortOrder = following.Count > 0 ? (original.SortOrder + following.Min()) / 2 : original.SortOrder + 1,
        };
        Undoable("Duplicate Task", (tasks, _) => tasks.Insert(index + 1, copy));
        return copy;
    }

    /// <summary>
    /// Removes a task made from a "+" that never got any content. Not undoable: it's tidying up after an
    /// abandoned draft, not an edit the user made.
    /// </summary>
    public void DiscardIfBlank(Guid id)
    {
        var index = IndexOfTask(id);
        if (index < 0) return;
        var task = _tasks[index];
        if (task.Title.Trim().Length != 0 || task.Notes.Trim().Length != 0
            || task.Subtasks.Count != 0 || task.Attachments.Count != 0) return;
        Apply((tasks, _) => tasks.RemoveAt(index));
        DidChange();
    }

    public void DeleteTasks(IReadOnlySet<Guid> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (!_tasks.Any(t => ids.Contains(t.Id))) return;
        Undoable(ids.Count == 1 ? "Delete Task" : "Delete Tasks", (tasks, _) => tasks.RemoveAll(t => ids.Contains(t.Id)));
    }

    /// <summary>Places <paramref name="id"/> directly before <paramref name="beforeId"/> within <paramref name="ordered"/> (the list as the user sees it).</summary>
    public void MoveTask(Guid id, Guid beforeId, IReadOnlyList<TaskItem> ordered)
    {
        if (TaskOperations.SortOrderPlacing(id, beforeId, ordered) is not { } order) return;
        UpdateTask(id, "Move Task", t => t with { SortOrder = order });
    }

    // Attachments

    public void AttachFiles(IEnumerable<string> paths, Guid taskId)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var added = new List<Attachment>();
        foreach (var path in paths)
        {
            try
            {
                added.Add(Attachments.ImportFile(path, _clock.UtcNow));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                AppLog.Error($"Couldn't attach {SafeFileName(path)}", ex);
            }
        }

        if (added.Count == 0) return;
        UpdateTask(taskId, "Add Attachment", t => t with { Attachments = t.Attachments.AddRange(added) });
    }

    /// <summary>Returns false when the text isn't a usable web address.</summary>
    public bool AttachLink(string text, Guid taskId)
    {
        if (AttachmentStorage.Link(text, _clock.UtcNow) is not { } attachment) return false;
        UpdateTask(taskId, "Add Link", t => t with { Attachments = t.Attachments.Add(attachment) });
        return true;
    }

    public void RemoveAttachment(Guid attachmentId, Guid taskId)
    {
        UpdateTask(taskId, "Remove Attachment", t => t with { Attachments = t.Attachments.RemoveAll(a => a.Id == attachmentId) });
    }

    private static string SafeFileName(string path)
    {
        try { return Path.GetFileName(path); }
        catch (ArgumentException) { return path; }
    }

    // Projects

    public Project CreateProject(string name, string icon, ProjectColor color)
    {
        var project = new Project { Name = name, Icon = icon, Color = color };
        Undoable("New Project", (_, projects) => projects.Add(project));
        return project;
    }

    public void UpdateProject(Guid id, Func<Project, Project> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        var index = IndexOfProject(id);
        if (index < 0) return;
        var current = _projects[index];
        var project = change(current);
        if (project == current) return;
        Undoable("Edit Project", (_, projects) => projects[index] = project);
    }

    /// <summary>Deletes the project; its tasks move to the Inbox rather than disappearing.</summary>
    public void DeleteProject(Guid id)
    {
        Undoable("Delete Project", (tasks, projects) =>
        {
            projects.RemoveAll(p => p.Id == id);
            for (var index = 0; index < tasks.Count; index++)
            {
                if (tasks[index].ProjectId == id) tasks[index] = tasks[index] with { ProjectId = null };
            }
        });
    }

    public void MoveProject(Guid id, Guid beforeId)
    {
        var from = IndexOfProject(id);
        if (id == beforeId || from < 0) return;
        Undoable("Move Project", (_, projects) =>
        {
            var project = projects[from];
            projects.RemoveAt(from);
            var to = projects.FindIndex(p => p.Id == beforeId);
            projects.Insert(to < 0 ? projects.Count : to, project);
        });
    }

    // Undo

    private readonly record struct Snapshot(IReadOnlyList<TaskItem> Tasks, IReadOnlyList<Project> Projects);

    private int IndexOfTask(Guid id)
    {
        for (var index = 0; index < _tasks.Count; index++)
        {
            if (_tasks[index].Id == id) return index;
        }

        return -1;
    }

    private int IndexOfProject(Guid id)
    {
        for (var index = 0; index < _projects.Count; index++)
        {
            if (_projects[index].Id == id) return index;
        }

        return -1;
    }

    /// <summary>Runs <paramref name="body"/> on working copies, then publishes them as the new state.</summary>
    private void Apply(Action<List<TaskItem>, List<Project>> body)
    {
        var tasks = _tasks.ToList();
        var projects = _projects.ToList();
        body(tasks, projects);
        _tasks = tasks.AsReadOnly();
        _projects = projects.AsReadOnly();
    }

    /// <summary>Runs <paramref name="body"/>, records the previous state for undo, and saves.</summary>
    private void Undoable(string actionName, Action<List<TaskItem>, List<Project>> body)
    {
        var before = new Snapshot(_tasks, _projects);
        Apply(body);
        RegisterRestore(before, actionName);
        DidChange();
    }

    private void RegisterRestore(Snapshot snapshot, string actionName)
    {
        Undo.Register(actionName, () =>
        {
            var current = new Snapshot(_tasks, _projects);
            var live = new Dictionary<Guid, TaskItem>();
            foreach (var task in _tasks) live.TryAdd(task.Id, task);
            _tasks = snapshot.Tasks.Select(t => KeepingText(live.GetValueOrDefault(t.Id), t)).ToList().AsReadOnly();
            _projects = snapshot.Projects;
            RegisterRestore(current, actionName);
            DidChange();
        });
    }

    /// <summary>
    /// Typing has its own undo in the text system, so restoring a snapshot keeps the current wording of tasks
    /// that still exist rather than rolling it back too.
    /// </summary>
    private static TaskItem KeepingText(TaskItem? live, TaskItem restored)
    {
        if (live is null) return restored;
        var liveTitles = new Dictionary<Guid, string>();
        foreach (var subtask in live.Subtasks) liveTitles.TryAdd(subtask.Id, subtask.Title);
        return restored with
        {
            Title = live.Title,
            Notes = live.Notes,
            Subtasks = new ValueList<Subtask>(restored.Subtasks.Select(s =>
                liveTitles.TryGetValue(s.Id, out var title) ? s with { Title = title } : s)),
        };
    }

    // Persistence

    private void Load()
    {
        AppData? saved;
        try
        {
            saved = _fileStore.Load();
        }
        catch (Exception ex)
        {
            AppLog.Error("Couldn't read saved tasks", ex);
            RecoverFromUnreadableFile();
            // No attachment cleanup here: the unreadable file may still refer to every attachment.
            return;
        }

        if (saved is not null)
        {
            _tasks = saved.Tasks.ToList().AsReadOnly();
            _projects = saved.Projects.ToList().AsReadOnly();
            _reminders.Sync(_tasks, _projects);
        }
        else
        {
            var sample = AppData.Sample(_clock.UtcNow, _clock.Calendar);
            _tasks = sample.Tasks.ToList().AsReadOnly();
            _projects = sample.Projects.ToList().AsReadOnly();
            SaveNow();
        }

        var referenced = _tasks
            .SelectMany(t => t.Attachments)
            .Where(a => a.Kind == AttachmentKind.File)
            .Select(a => a.Location)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Attachments.RemoveFiles(referenced);
    }

    /// <summary>
    /// Sets the unreadable file aside so a fresh list can be saved. If even that fails, saving is switched off
    /// for the session so the original is never overwritten.
    /// </summary>
    private void RecoverFromUnreadableFile()
    {
        var folder = _fileStore.Directory;
        try
        {
            var moved = _fileStore.QuarantineDataFile(_clock.UtcNow);
            LoadIssue = "Your saved tasks couldn’t be read, so a fresh list was started. "
                + $"The original file was kept as “{Path.GetFileName(moved)}” in {folder}.";
        }
        catch (Exception ex)
        {
            AppLog.Error("Couldn't move the unreadable file aside", ex);
            _canSave = false;
            LoadIssue = "Your saved tasks couldn’t be read, and the file couldn’t be set aside. "
                + "To protect it, changes won’t be saved until TodoApp is restarted. "
                + $"The file is tasks.json in {folder}.";
        }
    }

    private void DidChange()
    {
        _saveDebouncer.Schedule(SaveNow);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SaveNow()
    {
        _saveDebouncer.Cancel();
        if (_canSave)
        {
            try
            {
                _fileStore.Save(new AppData(AppData.CurrentVersion, _projects, _tasks));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Error("Couldn't save tasks", ex);
            }
        }

        _reminders.Sync(_tasks, _projects);
    }
}
