using CommunityToolkit.Mvvm.ComponentModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using TodoCore;
using TaskStatus = TodoCore.TaskStatus;

namespace TodoApp.State;

public enum KeyCommand { Down, Up, Delete, Enter, Space, Escape }

/// <summary>
/// What the window is showing and the user actions that span several views. Main-thread only.
/// <see cref="Changed"/> is raised for any property change, any store change and <see cref="Tick"/>; compound
/// actions raise it once, after they finish.
/// </summary>
public sealed class AppState : ObservableObject
{
    private readonly Action<AppSettings> _saveSettings;
    private AppSettings _settings;

    private SidebarItem _sidebarSelection = SidebarItem.Today;
    private Guid? _selectedTaskId;
    private string _searchText = "";
    private IReadOnlySet<Priority> _priorityFilter = new HashSet<Priority>();
    private bool _isSidebarVisible = true;
    private DateTime _calendarMonth;
    private DateTime? _calendarSelectedDay;
    private bool _isComposing;

    /// <summary>A blank task created by "+" in the board or calendar, until it gets a title.</summary>
    private Guid? _draftTaskId;

    private int _batchDepth;
    private bool _changePending;
    private readonly List<Action> _deferred = new();

    public AppState(TaskStore store, IClock clock, AppSettings settings, Action<AppSettings> saveSettings)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(saveSettings);
        Store = store;
        Clock = clock;
        _saveSettings = saveSettings;
        _settings = settings with
        {
            SidebarWidth = ClampWidth(settings.SidebarWidth, ColumnLayout.SidebarRange, new AppSettings().SidebarWidth),
            InspectorWidth = ClampWidth(settings.InspectorWidth, ColumnLayout.InspectorRange, new AppSettings().InspectorWidth),
        };
        _calendarMonth = clock.UtcNow;
        _selectedTaskId = new TaskQuery(TaskScope.Today).Run(store.Tasks, store.Projects, clock.UtcNow, clock.Calendar)
            .FirstOrDefault()?.Id;
        Store.Changed += (_, _) => RaiseChanged();
    }

    public TaskStore Store { get; }

    public IClock Clock { get; }

    public event EventHandler? Changed;

    /// <summary>Move keyboard focus into the search field.</summary>
    public event EventHandler? SearchFocusRequested;

    /// <summary>The inspector should focus this task's title. Raised after <see cref="Changed"/>, so the inspector is showing it.</summary>
    public event EventHandler<Guid>? TitleFocusRequested;

    /// <summary>Open the project editor; null creates a new project.</summary>
    public event EventHandler<Guid?>? ProjectEditorRequested;

    /// <summary>Ask the user to confirm deleting this project.</summary>
    public event EventHandler<Project>? ProjectDeletionRequested;

    // Window state

    public SidebarItem SidebarSelection
    {
        get => _sidebarSelection;
        set => SetProperty(ref _sidebarSelection, value);
    }

    /// <summary>Moving on from an untouched draft throws it away instead of leaving "Untitled" behind.</summary>
    public Guid? SelectedTaskId
    {
        get => _selectedTaskId;
        set => Batch(() =>
        {
            SetProperty(ref _selectedTaskId, value);
            if (_draftTaskId is { } draft && draft != _selectedTaskId)
            {
                _draftTaskId = null;
                Store.DiscardIfBlank(draft);
            }
        });
    }

    public string SearchText
    {
        get => _searchText;
        set => SetProperty(ref _searchText, value ?? "");
    }

    public IReadOnlySet<Priority> PriorityFilter
    {
        get => _priorityFilter;
        set
        {
            var copy = (value ?? new HashSet<Priority>()).ToHashSet();
            if (_priorityFilter.SetEquals(copy)) return;
            SetProperty(ref _priorityFilter, copy);
        }
    }

    public bool IsSidebarVisible
    {
        get => _isSidebarVisible;
        set => SetProperty(ref _isSidebarVisible, value);
    }

    public DateTime CalendarMonth
    {
        get => _calendarMonth;
        set => SetProperty(ref _calendarMonth, value);
    }

    public DateTime? CalendarSelectedDay
    {
        get => _calendarSelectedDay;
        set => SetProperty(ref _calendarSelectedDay, value);
    }

    /// <summary>The inline "New task" field at the bottom of the list is open.</summary>
    public bool IsComposing
    {
        get => _isComposing;
        set => SetProperty(ref _isComposing, value);
    }

    // Persisted settings

    public ViewMode ViewMode
    {
        get => _settings.ViewMode;
        set => Persist(_settings with { ViewMode = value });
    }

    public TaskSort Sort
    {
        get => _settings.Sort;
        set => Persist(_settings with { Sort = value });
    }

    public bool ShowCompleted
    {
        get => _settings.ShowCompleted;
        set => Persist(_settings with { ShowCompleted = value });
    }

    public bool IsInspectorVisible
    {
        get => _settings.InspectorVisible;
        set => Persist(_settings with { InspectorVisible = value });
    }

    public AppearancePreference Appearance
    {
        get => _settings.Appearance;
        set => Persist(_settings with { Appearance = value });
    }

    public double SidebarWidth
    {
        get => _settings.SidebarWidth;
        set
        {
            if (!double.IsFinite(value)) return;
            Persist(_settings with { SidebarWidth = ClampWidth(value, ColumnLayout.SidebarRange, _settings.SidebarWidth) });
        }
    }

    public double InspectorWidth
    {
        get => _settings.InspectorWidth;
        set
        {
            if (!double.IsFinite(value)) return;
            Persist(_settings with { InspectorWidth = ClampWidth(value, ColumnLayout.InspectorRange, _settings.InspectorWidth) });
        }
    }

    // Derived

    public bool IsSearching => SearchText.Trim().Length > 0;

    public bool IsDashboard => SidebarSelection == SidebarItem.Dashboard && !IsSearching;

    public TaskScope Scope
    {
        get
        {
            if (IsSearching) return TaskScope.All;
            return SidebarSelection.Kind switch
            {
                SidebarKind.Inbox => TaskScope.Inbox,
                SidebarKind.Today => TaskScope.Today,
                SidebarKind.Upcoming => TaskScope.Upcoming,
                SidebarKind.Completed => TaskScope.Completed,
                SidebarKind.Dashboard => TaskScope.All,
                SidebarKind.Project => TaskScope.Project(SidebarSelection.ProjectId!.Value),
                _ => throw new InvalidOperationException(),
            };
        }
    }

    public TaskQuery Query => new(Scope, SearchText, PriorityFilter, ShowCompleted || IsSearching, Sort);

    /// <summary>The tasks the center column lists, in display order.</summary>
    public IReadOnlyList<TaskItem> VisibleTasks => Run(Query);

    /// <summary>Tasks in the order they appear on screen, for the arrow keys and for choosing what to select after a delete.</summary>
    public IReadOnlyList<TaskItem> NavigationOrder
    {
        get
        {
            switch (ViewMode)
            {
                case ViewMode.List:
                    return TaskSection.Make(Scope, VisibleTasks, Clock.UtcNow, Clock.Calendar).SelectMany(s => s.Tasks).ToList();
                case ViewMode.Board:
                    var tasks = VisibleTasks;
                    return Enum.GetValues<TaskStatus>().SelectMany(status => tasks.Where(t => t.Status == status)).ToList();
                case ViewMode.Calendar:
                    return CalendarTasks
                        .OrderBy(t => t.DueDate ?? DateTime.MinValue)
                        .ThenBy(t => t.SortOrder)
                        .ToList();
                default:
                    throw new InvalidOperationException();
            }
        }
    }

    /// <summary>Scheduled tasks for the calendar: the selected project's, or everyone's.</summary>
    public IReadOnlyList<TaskItem> CalendarTasks
    {
        get
        {
            var query = Query;
            if (query.Scope.Kind != TaskScopeKind.Project) query = query with { Scope = TaskScope.All };
            query = query with { ShowCompleted = ShowCompleted };
            return Run(query).Where(t => t.DueDate is not null).ToList();
        }
    }

    public string Title
    {
        get
        {
            if (IsSearching) return "Search";
            return SidebarSelection.Kind switch
            {
                SidebarKind.Inbox => "Inbox",
                SidebarKind.Today => "Today",
                SidebarKind.Upcoming => "Upcoming",
                SidebarKind.Completed => "Completed",
                SidebarKind.Dashboard => "Dashboard",
                SidebarKind.Project => Store.FindProject(SidebarSelection.ProjectId)?.Name ?? "Project",
                _ => throw new InvalidOperationException(),
            };
        }
    }

    public string WindowTitle => $"Tasks — {Title}";

    public TaskItem? SelectedTask => Store.FindTask(SelectedTaskId);

    // Navigation

    public void Select(SidebarItem item) => Batch(() =>
    {
        SearchText = "";
        IsComposing = false;
        SidebarSelection = item;
        if (SelectedTaskId is { } id && !VisibleTasks.Any(t => t.Id == id)) SelectedTaskId = null;
    });

    public void SelectTask(Guid? id) => SelectedTaskId = id;

    /// <summary>Shows a task wherever it lives — used when a reminder is clicked.</summary>
    public void Reveal(Guid id) => Batch(() =>
    {
        if (Store.FindTask(id) is not { } task) return;
        SearchText = "";
        var now = Clock.UtcNow;
        var belongsToday = TaskQuery.Belongs(task, TaskScope.Today, now, Clock.Calendar);
        if (task.ProjectId is { } projectId && !belongsToday)
        {
            SidebarSelection = SidebarItem.Project(projectId);
        }
        else if (belongsToday)
        {
            SidebarSelection = SidebarItem.Today;
        }
        else
        {
            SidebarSelection = task.IsCompleted
                ? SidebarItem.Completed
                : (task.DueDate is null ? SidebarItem.Inbox : SidebarItem.Upcoming);
        }

        SelectedTaskId = id;
        IsInspectorVisible = true;
    });

    public void MoveSelection(int offset) => Batch(() =>
    {
        var ids = NavigationOrder.Select(t => t.Id).ToList();
        if (ids.Count == 0) return;
        var index = SelectedTaskId is { } current ? ids.IndexOf(current) : -1;
        if (index < 0)
        {
            SelectedTaskId = offset > 0 ? ids[0] : ids[^1];
            return;
        }

        SelectedTaskId = ids[Math.Min(Math.Max(index + offset, 0), ids.Count - 1)];
    });

    public void FocusSearch() => SearchFocusRequested?.Invoke(this, EventArgs.Empty);

    // Creating

    /// <summary>
    /// Ctrl+N and the "+" buttons: opens the inline field in list view; elsewhere creates an untitled task
    /// and puts the cursor in its title.
    /// </summary>
    public void BeginNewTask() => Batch(() =>
    {
        SearchText = "";
        if (SidebarSelection == SidebarItem.Dashboard || SidebarSelection == SidebarItem.Completed)
        {
            SidebarSelection = SidebarItem.Inbox;
        }

        if (ViewMode == ViewMode.List) IsComposing = true;
        else CreateDraftTask();
    });

    /// <summary>
    /// Creates a blank task and puts the cursor in its title. If the user moves on without typing anything,
    /// the draft is discarded.
    /// </summary>
    public void CreateDraftTask(DateTime? dueDate = null, TaskStatus status = TaskStatus.Todo) => Batch(() =>
    {
        var task = CreateTask("", dueDate, status);
        RevealInInspector(task.Id, focusTitle: true);
        _draftTaskId = task.Id;
    });

    public TaskItem CreateTask(string title, DateTime? dueDate = null, TaskStatus status = TaskStatus.Todo) => Batch(() =>
    {
        var (projectId, defaultDue) = NewTaskDefaults();
        return Store.CreateTask(title, projectId, dueDate ?? defaultDue, status);
    });

    /// <summary>A click on a task: selects it; a double-click also opens its details.</summary>
    public void HandleClick(Guid id, bool isDoubleClick)
    {
        if (isDoubleClick) RevealInInspector(id, focusTitle: false);
        else SelectTask(id);
    }

    public void RevealInInspector(Guid id, bool focusTitle) => Batch(() =>
    {
        SelectedTaskId = id;
        IsInspectorVisible = true;
        if (focusTitle) _deferred.Add(() => TitleFocusRequested?.Invoke(this, id));
    });

    /// <summary>New tasks land where the user is looking: in the open project, due today in Today, and so on.</summary>
    private (Guid? ProjectId, DateTime? DueDate) NewTaskDefaults()
    {
        var calendar = Clock.Calendar;
        var today = calendar.StartOfDay(Clock.UtcNow);
        var projectId = SidebarSelection.Kind == SidebarKind.Project ? SidebarSelection.ProjectId : null;

        if (ViewMode == ViewMode.Calendar && CalendarSelectedDay is { } day) return (projectId, day);
        return SidebarSelection.Kind switch
        {
            SidebarKind.Today => (null, today),
            SidebarKind.Upcoming => (null, calendar.AddDays(today, 1)),
            _ => (projectId, null),
        };
    }

    // Acting on the selection

    public void ToggleCompleted(Guid id) => Store.ToggleCompleted(id);

    public void ToggleSelectedCompleted()
    {
        if (SelectedTaskId is { } id) Store.ToggleCompleted(id);
    }

    public void DuplicateSelected() => Batch(() =>
    {
        if (SelectedTaskId is not { } id || Store.DuplicateTask(id) is not { } copy) return;
        SelectedTaskId = copy.Id;
    });

    public void Delete(IReadOnlySet<Guid> ids) => Batch(() =>
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (SelectedTaskId is { } selected && ids.Contains(selected))
        {
            var order = NavigationOrder.Select(t => t.Id).ToList();
            var index = Math.Max(order.IndexOf(selected), 0);
            var remaining = order.Where(id => !ids.Contains(id)).ToList();
            SelectedTaskId = remaining.Count == 0 ? null : remaining[Math.Min(index, remaining.Count - 1)];
        }

        Store.DeleteTasks(ids);
    });

    public void DeleteSelected()
    {
        if (SelectedTaskId is { } id) Delete(new HashSet<Guid> { id });
    }

    public void SetPriority(Priority priority, IEnumerable<Guid> ids) =>
        Store.UpdateTasks(ids, "Set Priority", t => t with { Priority = priority });

    public void SetDueDay(DateTime? day, IEnumerable<Guid> ids)
    {
        var calendar = Clock.Calendar;
        Store.UpdateTasks(
            ids,
            day is null ? "Remove Due Date" : "Set Due Date",
            t => day is { } value ? t.RescheduledTo(value, calendar) : t.WithoutDueDate());
    }

    public void Move(IEnumerable<Guid> ids, Guid? projectId) =>
        Store.UpdateTasks(ids, "Move to Project", t => t with { ProjectId = projectId });

    /// <summary>The Task menu's Due Date items: today plus <paramref name="days"/>.</summary>
    public void SetDueOffset(int days, Guid id)
    {
        var calendar = Clock.Calendar;
        var today = calendar.StartOfDay(Clock.UtcNow);
        SetDueDay(calendar.AddDays(today, days), new[] { id });
    }

    /// <summary>The popover's "Next week": the coming Monday, or the first weekday of the week if that isn't Sunday.</summary>
    public DateTime NextWeekStart()
    {
        var calendar = Clock.Calendar;
        var today = calendar.StartOfDay(Clock.UtcNow);
        var target = calendar.FirstWeekday == 1 ? 2 : calendar.FirstWeekday;
        var ahead = (target - calendar.Weekday(today) + 7) % 7;
        return calendar.AddDays(today, ahead == 0 ? 7 : ahead);
    }

    // Projects

    public void RequestProjectEditor(Guid? projectId) => ProjectEditorRequested?.Invoke(this, projectId);

    public void RequestProjectDeletion(Guid projectId)
    {
        if (Store.FindProject(projectId) is { } project) ProjectDeletionRequested?.Invoke(this, project);
    }

    public void DeleteProjectConfirmed(Guid projectId) => Batch(() =>
    {
        if (SidebarSelection == SidebarItem.Project(projectId)) Select(SidebarItem.Inbox);
        Store.DeleteProject(projectId);
    });

    // Drag and drop

    private const string ProjectPrefix = "project:";

    private static Guid? ParseId(string text) =>
        Guid.TryParseExact(text, "D", out var id) ? id : null;

    private static List<Guid> TaskIds(IReadOnlyList<string> items) =>
        items.Select(ParseId).Where(id => id is not null).Select(id => id!.Value).ToList();

    private static Guid? ProjectIdFrom(string item) =>
        item.StartsWith(ProjectPrefix, StringComparison.Ordinal) ? ParseId(item[ProjectPrefix.Length..]) : null;

    /// <summary>Tasks dropped on a sidebar row are filed there; a project dropped on a project row reorders.</summary>
    public bool HandleDropOnSidebar(IReadOnlyList<string> items, SidebarItem target) => Batch(() =>
    {
        ArgumentNullException.ThrowIfNull(items);
        if (target.Kind == SidebarKind.Project && items.Count > 0 && ProjectIdFrom(items[0]) is { } projectId)
        {
            Store.MoveProject(projectId, target.ProjectId!.Value);
            return true;
        }

        var ids = TaskIds(items);
        if (ids.Count == 0) return false;
        var calendar = Clock.Calendar;
        var today = calendar.StartOfDay(Clock.UtcNow);
        switch (target.Kind)
        {
            case SidebarKind.Inbox: Move(ids, null); break;
            case SidebarKind.Today: SetDueDay(today, ids); break;
            case SidebarKind.Upcoming: SetDueDay(calendar.AddDays(today, 1), ids); break;
            case SidebarKind.Completed: Store.SetStatus(ids, TaskStatus.Done); break;
            case SidebarKind.Dashboard: return false;
            case SidebarKind.Project: Move(ids, target.ProjectId); break;
        }

        return true;
    });

    /// <summary>Tasks dropped on a row in the list: reorder them in front of it.</summary>
    public bool HandleDropBefore(IReadOnlyList<string> items, Guid targetId, IReadOnlyList<TaskItem> ordered) => Batch(() =>
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(ordered);
        var ids = TaskIds(items);
        if (ids.Count == 0 || ids[0] == targetId) return false;
        var ordering = ordered;
        if (Sort != TaskSort.Manual)
        {
            Sort = TaskSort.Manual;
            ordering = ordered.OrderBy(t => t.SortOrder).ToList();
        }

        Store.MoveTask(ids[0], targetId, ordering);
        return true;
    });

    /// <summary>Cards dropped on a board column (optionally in front of a card).</summary>
    public bool HandleDropOnStatus(
        IReadOnlyList<string> items,
        TaskStatus status,
        Guid? beforeId = null,
        IReadOnlyList<TaskItem>? ordered = null) => Batch(() =>
    {
        ArgumentNullException.ThrowIfNull(items);
        var ids = TaskIds(items);
        if (ids.Count == 0) return false;
        Store.SetStatus(ids, status);
        if (beforeId is { } before) Store.MoveTask(ids[0], before, ordered ?? Array.Empty<TaskItem>());
        return true;
    });

    /// <summary>Tasks dropped on a calendar day: reschedule them, keeping any time of day.</summary>
    public bool HandleDropOnDay(IReadOnlyList<string> items, DateTime day) => Batch(() =>
    {
        ArgumentNullException.ThrowIfNull(items);
        var ids = TaskIds(items);
        if (ids.Count == 0) return false;
        SetDueDay(day, ids);
        return true;
    });

    // Keyboard

    /// <summary>
    /// Arrow keys, Delete, Enter, Space and Escape. Returns false, changing nothing, while a text input has
    /// focus, a modifier is held, a dialog is open or the Dashboard is showing.
    /// </summary>
    public bool HandleKey(KeyCommand key, bool isTextInputFocused, bool hasModifiers, bool isDialogOpen) => Batch(() =>
    {
        if (isTextInputFocused || hasModifiers || isDialogOpen || IsDashboard) return false;

        switch (key)
        {
            case KeyCommand.Down:
                MoveSelection(1);
                break;
            case KeyCommand.Up:
                MoveSelection(-1);
                break;
            case KeyCommand.Delete:
                if (SelectedTaskId is null) return false;
                DeleteSelected();
                break;
            case KeyCommand.Enter:
                if (SelectedTaskId is not { } id) return false;
                RevealInInspector(id, focusTitle: true);
                break;
            case KeyCommand.Space:
                if (SelectedTaskId is null) return false;
                ToggleSelectedCompleted();
                break;
            case KeyCommand.Escape:
                if (SelectedTaskId is null) return false;
                SelectedTaskId = null;
                break;
            default:
                return false;
        }

        return true;
    });

    /// <summary>
    /// "Today", overdue colouring and counts are worked out when views draw, so the shell calls this when the
    /// day changes, after sleep, and once a minute for tasks with a time of day.
    /// </summary>
    public void Tick() => RaiseChanged();

    // Plumbing

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        RaiseChanged();
    }

    private IReadOnlyList<TaskItem> Run(TaskQuery query) =>
        query.Run(Store.Tasks, Store.Projects, Clock.UtcNow, Clock.Calendar);

    private void Persist(AppSettings updated, [CallerMemberName] string? property = null)
    {
        if (updated == _settings) return;
        _settings = updated;
        _saveSettings(updated);
        OnPropertyChanged(property);
    }

    private static double ClampWidth(double value, (double Min, double Max) range, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, range.Min, range.Max) : Math.Clamp(fallback, range.Min, range.Max);

    private void RaiseChanged()
    {
        if (_batchDepth > 0) _changePending = true;
        else Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Batch(Action body) => Batch(() =>
    {
        body();
        return 0;
    });

    /// <summary>Runs a user action so that <see cref="Changed"/> fires once, afterwards, followed by any queued focus requests.</summary>
    private T Batch<T>(Func<T> body)
    {
        _batchDepth++;
        try
        {
            return body();
        }
        finally
        {
            _batchDepth--;
            if (_batchDepth == 0) Flush();
        }
    }

    private void Flush()
    {
        if (_changePending)
        {
            _changePending = false;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        var queued = _deferred.ToArray();
        _deferred.Clear();
        foreach (var action in queued) action();
    }
}
