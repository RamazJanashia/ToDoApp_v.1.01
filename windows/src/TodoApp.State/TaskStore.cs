using TodoCore;
using TaskStatus = TodoCore.TaskStatus;

namespace TodoApp.State;

/// <summary>Something the user needs to know that a log line wouldn't tell them.</summary>
public sealed record StoreAlert(string Title, string Message);

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

    /// <summary>How long backups are kept, and how long a file attached but no longer used stays around.</summary>
    private const int RetentionDays = 30;

    // Off for the session when saving would overwrite a file that must be kept: one that couldn't be set aside, or
    // one from a newer version of the app.
    private bool _canSave = true;

    // One alert per run of failures, not one per save.
    private bool _saveFailureReported;

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

    /// <summary>Shown once to the user, then dismissed.</summary>
    public StoreAlert? Alert { get; private set; }

    /// <summary>A new <see cref="Alert"/> is waiting to be shown.</summary>
    public event EventHandler? AlertRaised;

    /// <summary>The one folder that holds everything: tasks, backups and attachments.</summary>
    public string DataDirectory => _fileStore.Directory;

    public void DismissAlert() => Alert = null;

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
        var following = _tasks.Select(t => t.SortOrder).Where(order => order > original.SortOrder).ToList();
        var copy = original.Duplicated(_clock.UtcNow) with
        {
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
        if (!_tasks[index].IsBlank) return;
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
        ArgumentNullException.ThrowIfNull(ordered);
        if (TaskOperations.SortOrderPlacing(id, beforeId, ordered) is null) return;
        Undoable("Move Task", (tasks, _) => TaskOperations.Move(id, beforeId, ordered, tasks));
    }

    // Attachments

    /// <summary>
    /// Copies on a background thread, because attachments can be large enough to freeze the window, then adds them
    /// in one undoable step. Call it on the UI thread: it continues there.
    /// </summary>
    public async Task AttachFilesAsync(IEnumerable<string> paths, Guid taskId)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var files = paths.ToList();
        if (files.Count == 0) return;
        var now = _clock.UtcNow;
        var calendar = _clock.Calendar;
        var storage = Attachments;

        var (added, failed) = await Task.Run(() =>
        {
            var copied = new List<Attachment>();
            var refused = new List<(string Name, Exception Error)>();
            foreach (var path in files)
            {
                try
                {
                    copied.Add(storage.ImportFile(path, now, calendar));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
                {
                    refused.Add((SafeFileName(path), ex));
                }
            }

            return (copied, refused);
        });

        if (added.Count > 0) UpdateTask(taskId, "Add Attachment", t => t with { Attachments = t.Attachments.AddRange(added) });
        foreach (var (name, error) in failed) AppLog.Error($"Couldn't attach {name}", error);
        if (failed.Count > 0)
        {
            Show(
                "Couldn’t attach a file",
                $"{string.Join(", ", failed.Select(f => f.Name))} couldn’t be copied. {failed[0].Error.Message}");
        }
    }

    /// <summary>Returns false when the text isn't a usable web or mail address.</summary>
    public bool AttachLink(string text, Guid taskId)
    {
        if (AttachmentLink.FromText(text, _clock.UtcNow) is not { } attachment) return false;
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

    public Project CreateProject(string name, string icon, ProjectColor color, Guid? parentId = null)
    {
        var project = new Project { Name = name, Icon = icon, Color = color, ParentId = parentId };
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

    /// <summary>Deletes the project; what's under it moves up rather than disappearing.</summary>
    public void DeleteProject(Guid id)
    {
        Undoable("Delete Project", (tasks, projects) => TaskOperations.DeleteProject(id, projects, tasks));
    }

    /// <summary>Puts the project directly above <paramref name="beforeId"/>, as its sibling; never inside itself.</summary>
    public void MoveProject(Guid id, Guid beforeId)
    {
        var reordered = _projects.ToList();
        if (!reordered.MoveProject(id, beforeId)) return;
        Undoable("Move Project", (_, projects) =>
        {
            projects.Clear();
            projects.AddRange(reordered);
        });
    }

    // Reminders

    /// <summary>When the user sets a reminder: says so if Windows won't show it.</summary>
    public void ConfirmRemindersAllowed()
    {
        if (!_reminders.AreNotificationsBlocked) return;
        Show(
            "Reminders are turned off",
            "Allow notifications for TodoApp in Settings ▸ System ▸ Notifications, or this reminder won’t appear.");
    }

    /// <summary>Picks up what time alone changes: reminders past the limit coming into range as earlier ones fire.</summary>
    public void ResyncReminders() => _reminders.Sync(_tasks, _projects);

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
            // A blank task that's gone is a discarded draft, not something to bring back.
            _tasks = snapshot.Tasks
                .Where(t => !t.IsBlank || live.ContainsKey(t.Id))
                .Select(t => KeepingText(live.GetValueOrDefault(t.Id), t))
                .ToList()
                .AsReadOnly();
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
        catch (NewerDataFileException ex)
        {
            AppLog.Error($"Data file has newer version {ex.Version}", ex);
            _canSave = false;
            Show(
                "Couldn’t open your tasks",
                "Your tasks were saved by a newer version of TodoApp. To protect them, changes won’t be saved here. "
                    + $"Please update TodoApp. The file is tasks.json in {_fileStore.Directory}.");
            return;
        }
        catch (Exception ex)
        {
            AppLog.Error("Couldn't read saved tasks", ex);
            RecoverFromUnreadableFile();
            return;
        }

        if (saved is null)
        {
            var sample = AppData.Sample(_clock.UtcNow, _clock.Calendar);
            _tasks = sample.Tasks.ToList().AsReadOnly();
            _projects = sample.Projects.ToList().AsReadOnly();
            SaveNow();
            // No attachment cleanup: with no data file there's no telling what is still needed.
            return;
        }

        // A blank task here is a draft left behind when the app quit.
        _tasks = saved.Tasks.Where(t => !t.IsBlank).ToList().AsReadOnly();
        _projects = saved.Projects.ToList().AsReadOnly();
        _reminders.Sync(_tasks, _projects);
        // Cleaning up only at launch keeps undo of a removed attachment safe for the whole session.
        if (BackUpIfNewDay()) RemoveUnusedAttachments();
    }

    /// <summary>Makes today's backup if there isn't one yet, from the file as it stands. Returns whether it did.</summary>
    private bool BackUpIfNewDay()
    {
        try
        {
            if (_fileStore.BackUpDataFile(_clock.UtcNow, _clock.Calendar) is null) return false;
            _fileStore.PruneBackups(RetentionDays);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Error("Couldn't back up tasks", ex);
            return false;
        }
    }

    /// <summary>
    /// Removes stored files nothing needs any more: not the data file, not a backup, and not attached recently.
    /// When what's needed can't be established, removes nothing.
    /// </summary>
    private void RemoveUnusedAttachments()
    {
        if (_fileStore.ReferencedAttachmentPaths() is not { } referenced) return;
        var cutoff = _clock.Calendar.AddDays(_clock.UtcNow, -RetentionDays);
        Attachments.RemoveFiles(referenced, DatedFolder.Name(cutoff, _clock.Calendar));
    }

    /// <summary>Keeps whatever is already showing: the first problem is usually the one that matters.</summary>
    private void Show(string title, string message)
    {
        if (Alert is not null) return;
        Alert = new StoreAlert(title, message);
        AlertRaised?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Sets the unreadable file aside so a fresh list can be saved. If even that fails, saving is switched off
    /// for the session so the original is never overwritten.
    /// </summary>
    private void RecoverFromUnreadableFile()
    {
        var folder = _fileStore.Directory;
        const string backups = "Copies from recent days are in its Backups folder.";
        try
        {
            var moved = _fileStore.QuarantineDataFile(_clock.UtcNow);
            Show(
                "Couldn’t open your tasks",
                "Your saved tasks couldn’t be read, so a fresh list was started. "
                    + $"The original file was kept as “{Path.GetFileName(moved)}” in {folder}. {backups}");
        }
        catch (Exception ex)
        {
            AppLog.Error("Couldn't move the unreadable file aside", ex);
            _canSave = false;
            Show(
                "Couldn’t open your tasks",
                "Your saved tasks couldn’t be read, and the file couldn’t be set aside. "
                    + "To protect it, changes won’t be saved until TodoApp is restarted. "
                    + $"The file is tasks.json in {folder}. {backups}");
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
            // Before writing, so the backup holds the data as the day began, even if the app has been open since
            // yesterday.
            BackUpIfNewDay();
            try
            {
                _fileStore.Save(new AppData(AppData.CurrentVersion, _projects, _tasks));
                _saveFailureReported = false;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Error("Couldn't save tasks", ex);
                if (!_saveFailureReported)
                {
                    _saveFailureReported = true;
                    Show(
                        "Couldn’t save your tasks",
                        $"Recent changes aren’t on disk yet. TodoApp will keep trying as you make changes. {ex.Message}");
                }
            }
        }

        _reminders.Sync(_tasks, _projects);
    }
}
