using CommunityToolkit.Mvvm.ComponentModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using TodoCore;
using TaskStatus = TodoCore.TaskStatus;

namespace TodoApp.State;

public enum KeyCommand { Down, Up, Delete, Enter, Space, Escape }

/// <summary>The shortcuts offered wherever a due date can be set.</summary>
public enum QuickDue { Today, Tomorrow, NextWeek }

/// <summary>Opens the project editor. A null <see cref="ProjectId"/> creates one, under <see cref="ParentId"/> when set.</summary>
public sealed record ProjectEditorRequest(Guid? ProjectId, Guid? ParentId = null);

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
    private IReadOnlySet<Priority> _priorityFilter;
    private readonly HashSet<WorkspaceElement> _hidden;
    private readonly HashSet<Guid> _collapsed;
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
        _priorityFilter = settings.PriorityFilter.Select(value => (Priority)value).ToHashSet();
        _hidden = settings.HiddenElements
            .Select(WorkspaceElements.Parse)
            .Where(element => element is not null)
            .Select(element => element!.Value)
            .ToHashSet();
        _collapsed = settings.CollapsedProjects.ToHashSet();
        _calendarMonth = clock.UtcNow;
        RestoreWorkspace(settings);
        Store.Changed += (_, _) =>
        {
            // Undo can take away the project on screen.
            LeaveMissingProject();
            RaiseChanged();
        };
    }

    /// <summary>
    /// Back to where the user left off, as far as that place and task still exist. Written straight into the fields:
    /// what is being restored is already saved.
    /// </summary>
    private void RestoreWorkspace(AppSettings settings)
    {
        if (settings.SidebarSelection is { } key && SidebarItems.FromStorageKey(key) is { } saved)
        {
            _sidebarSelection = saved.Kind == SidebarKind.Project && Store.FindProject(saved.ProjectId) is null ? SidebarItem.Today : saved;
        }

        LeaveHiddenPlaces();
        var visible = VisibleTasks;
        _selectedTaskId = visible.Any(t => t.Id == settings.SelectedTask) ? settings.SelectedTask : visible.FirstOrDefault()?.Id;
    }

    public TaskStore Store { get; }

    public IClock Clock { get; }

    public event EventHandler? Changed;

    /// <summary>Move keyboard focus into the search field.</summary>
    public event EventHandler? SearchFocusRequested;

    /// <summary>The inspector should focus this task's title. Raised after <see cref="Changed"/>, so the inspector is showing it.</summary>
    public event EventHandler<Guid>? TitleFocusRequested;

    /// <summary>Open the project editor.</summary>
    public event EventHandler<ProjectEditorRequest>? ProjectEditorRequested;

    /// <summary>Ask the user to confirm deleting this project.</summary>
    public event EventHandler<Project>? ProjectDeletionRequested;

    // Window state

    /// <summary>Remembered between launches.</summary>
    public SidebarItem SidebarSelection
    {
        get => _sidebarSelection;
        set
        {
            SetProperty(ref _sidebarSelection, value);
            // Also when unchanged: a restored value lives in the field, not yet in what is saved.
            Remember(_settings with { SidebarSelection = value.StorageKey() });
        }
    }

    /// <summary>
    /// Remembered between launches. Moving on from an untouched draft throws it away instead of leaving "Untitled"
    /// behind.
    /// </summary>
    public Guid? SelectedTaskId
    {
        get => _selectedTaskId;
        set => Batch(() =>
        {
            SetProperty(ref _selectedTaskId, value);
            Remember(_settings with { SelectedTask = value });
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
            Remember(_settings with { PriorityFilter = new ValueList<int>(copy.Select(p => (int)p).Order()) });
        }
    }

    public bool IsSidebarVisible
    {
        get => _settings.SidebarVisible;
        set => Persist(_settings with { SidebarVisible = value });
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
                SidebarKind.AllTasks => TaskScope.AllOpen,
                SidebarKind.Today => TaskScope.Today,
                SidebarKind.Upcoming => TaskScope.Upcoming,
                SidebarKind.Completed => TaskScope.Completed,
                SidebarKind.Dashboard => TaskScope.All,
                // A project together with its sub-projects.
                SidebarKind.Project => TaskScope.Projects(Store.Projects.SubtreeIds(SidebarSelection.ProjectId!.Value)),
                _ => throw new InvalidOperationException(),
            };
        }
    }

    /// <summary>The list view also shows a project's tasks finished on earlier days.</summary>
    public TaskQuery Query =>
        new(Scope, SearchText, PriorityFilter, ShowCompleted || IsSearching, Sort, IncludesCompletedHistory: ViewMode == ViewMode.List);

    /// <summary>The tasks the center column lists, in display order.</summary>
    public IReadOnlyList<TaskItem> VisibleTasks => Run(Query);

    /// <summary>What the header counts and measures: without a project's finished tasks from earlier days.</summary>
    public IReadOnlyList<TaskItem> CurrentTasks => Run(Query with { IncludesCompletedHistory = false });

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
            if (query.Scope.Kind != TaskScopeKind.Projects) query = query with { Scope = TaskScope.All };
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
                SidebarKind.AllTasks => "All Tasks",
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

    // Customising

    /// <summary>Settings asked for, from a Customize button or menu item.</summary>
    public event EventHandler? SettingsRequested;

    public IReadOnlySet<WorkspaceElement> HiddenElements => _hidden;

    /// <summary>Projects whose sub-projects are folded away in the sidebar.</summary>
    public IReadOnlySet<Guid> CollapsedProjects => _collapsed;

    public bool Shows(WorkspaceElement element) => !_hidden.Contains(element);

    public IReadOnlyList<ViewMode> VisibleViewModes => Enum.GetValues<ViewMode>().Where(mode => Shows(mode.Element())).ToList();

    /// <summary>The one view left can't be switched off: the window would have nothing to show.</summary>
    public bool CanHide(WorkspaceElement element) =>
        element.Group() != WorkspaceGroup.Views || !Shows(element) || VisibleViewModes.Count > 1;

    public void SetVisible(bool visible, WorkspaceElement element) => Batch(() =>
    {
        if (!visible && !CanHide(element)) return;
        if (!(visible ? _hidden.Remove(element) : _hidden.Add(element))) return;
        RememberHidden();
        LeaveHiddenPlaces();
    });

    public void ShowEverything() => Batch(() =>
    {
        if (_hidden.Count == 0) return;
        _hidden.Clear();
        RememberHidden();
    });

    public void ToggleCollapsed(Guid projectId)
    {
        if (!_collapsed.Remove(projectId)) _collapsed.Add(projectId);
        Remember(_settings with { CollapsedProjects = new ValueList<Guid>(_collapsed) });
        RaiseChanged();
    }

    public void OpenSettings() => SettingsRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Where new tasks go from a place that can't list them, such as the Dashboard.</summary>
    public SidebarItem HomeForNewTasks =>
        Shows(WorkspaceElement.AllTasks) || !Shows(WorkspaceElement.Today) ? SidebarItem.AllTasks : SidebarItem.Today;

    /// <summary>For shortcuts into a place, such as a dashboard tile: a place that's been hidden stays hidden.</summary>
    public void SelectIfShown(SidebarItem item)
    {
        if (Shows(item.Element())) Select(item);
    }

    private void RememberHidden()
    {
        var names = Enum.GetValues<WorkspaceElement>().Where(_hidden.Contains).Select(WorkspaceElements.RawValue);
        Remember(_settings with { HiddenElements = new ValueList<string>(names) });
        RaiseChanged();
    }

    /// <summary>Steps off a view or sidebar place that has just been hidden.</summary>
    private void LeaveHiddenPlaces()
    {
        if (!Shows(ViewMode.Element()) && VisibleViewModes is { Count: > 0 } modes) ViewMode = modes[0];
        if (Shows(SidebarSelection.Element())) return;
        foreach (var place in new[] { SidebarItem.Today, SidebarItem.AllTasks, SidebarItem.Upcoming, SidebarItem.Completed, SidebarItem.Dashboard })
        {
            if (!Shows(place.Element())) continue;
            Select(place);
            return;
        }

        if (Shows(WorkspaceElement.Projects) && Store.Projects.Outline() is { Count: > 0 } outline) Select(SidebarItem.Project(outline[0].Id));
    }

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
        // Best place first, skipping any the user has hidden.
        var places = new List<SidebarItem>();
        if (TaskQuery.Belongs(task, TaskScope.Today, Clock.UtcNow, Clock.Calendar)) places.Add(SidebarItem.Today);
        if (task.ProjectId is { } projectId && Store.FindProject(projectId) is not null) places.Add(SidebarItem.Project(projectId));
        places.Add(task.IsCompleted ? SidebarItem.Completed : (task.DueDate is null ? SidebarItem.AllTasks : SidebarItem.Upcoming));
        if (!task.IsCompleted) places.Add(SidebarItem.AllTasks);
        foreach (var place in places)
        {
            if (!Shows(place.Element())) continue;
            SidebarSelection = place;
            break;
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
            SidebarSelection = HomeForNewTasks;
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

    /// <summary>"Next week" is the coming Monday, or the first weekday of the week where that isn't Sunday.</summary>
    public DateTime DayFor(QuickDue due)
    {
        var calendar = Clock.Calendar;
        var today = calendar.StartOfDay(Clock.UtcNow);
        return due switch
        {
            QuickDue.Today => today,
            QuickDue.Tomorrow => calendar.AddDays(today, 1),
            QuickDue.NextWeek => DueDateText.NextWeek(Clock.UtcNow, calendar),
            _ => throw new ArgumentOutOfRangeException(nameof(due), due, null),
        };
    }

    public void SetDue(QuickDue due, IEnumerable<Guid> ids) => SetDueDay(DayFor(due), ids);

    // Projects

    public void RequestProjectEditor(Guid? projectId, Guid? parentId = null) =>
        ProjectEditorRequested?.Invoke(this, new ProjectEditorRequest(projectId, parentId));

    /// <summary>What the deletion dialog says will happen to the project's contents.</summary>
    public string ProjectDeletionMessage(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var what = Store.FindProject(project.ParentId) is { } parent
            ? $"Its tasks and sub-projects will move to “{parent.Name}”."
            : "Its tasks will stay in All Tasks without a project, and its sub-projects will move to the top level.";
        return what + " You can undo this with Ctrl+Z.";
    }

    public void RequestProjectDeletion(Guid projectId)
    {
        if (Store.FindProject(projectId) is { } project) ProjectDeletionRequested?.Invoke(this, project);
    }

    /// <summary>Deletes a project. If it's open, moves on to its parent, or to the general list.</summary>
    public void DeleteProjectConfirmed(Guid projectId) => Batch(() =>
    {
        if (SidebarSelection == SidebarItem.Project(projectId))
        {
            var parent = Store.FindProject(projectId)?.ParentId;
            Select(parent is { } id && Store.FindProject(id) is not null ? SidebarItem.Project(id) : HomeForNewTasks);
        }

        Store.DeleteProject(projectId);
    });

    private void LeaveMissingProject()
    {
        if (SidebarSelection.Kind == SidebarKind.Project && Store.FindProject(SidebarSelection.ProjectId) is null) Select(HomeForNewTasks);
    }

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
            case SidebarKind.AllTasks or SidebarKind.Dashboard: return false;
            case SidebarKind.Today: SetDueDay(today, ids); break;
            case SidebarKind.Upcoming: SetDueDay(calendar.AddDays(today, 1), ids); break;
            case SidebarKind.Completed: Store.SetStatus(ids, TaskStatus.Done); break;
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
        Place(ids[0], targetId, ordered);
        return true;
    });

    /// <summary>Placing a task by hand only means something in manual order, so that's what it switches to.</summary>
    private void Place(Guid id, Guid beforeId, IReadOnlyList<TaskItem> ordered)
    {
        var ordering = ordered;
        if (Sort != TaskSort.Manual)
        {
            Sort = TaskSort.Manual;
            ordering = ordered.OrderBy(t => t.SortOrder).ToList();
        }

        Store.MoveTask(id, beforeId, ordering);
    }

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
        if (beforeId is { } before) Place(ids[0], before, ordered ?? Array.Empty<TaskItem>());
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
    /// day changes, after sleep, and once a minute for tasks with a time of day. Reminders past the limit come
    /// into range as earlier ones fire, so they are scheduled again too.
    /// </summary>
    public void Tick()
    {
        Store.ResyncReminders();
        RaiseChanged();
    }

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

    /// <summary>Saves a value whose change has already been announced.</summary>
    private void Remember(AppSettings updated)
    {
        if (updated == _settings) return;
        _settings = updated;
        _saveSettings(updated);
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
