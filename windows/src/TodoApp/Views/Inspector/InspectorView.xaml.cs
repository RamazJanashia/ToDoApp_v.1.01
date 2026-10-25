using System.Globalization;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TodoApp.State;
using TodoCore;
using TaskStatus = TodoCore.TaskStatus;

namespace TodoApp;

/// <summary>
/// The right-hand "Task details" panel (InspectorView.swift): the header with ×, then either "No task selected" or the
/// selected task's circle and title, its six property rows with their flyouts, the notes and the footer.
/// Typing in the title or notes saves on every keystroke without registering an undo step, and a render never writes
/// into a text box that has focus (<see cref="TextSync"/>), so fast typing keeps its caret and characters.
/// Public surface: <see cref="Render"/> and <see cref="FocusTitle"/>.
/// </summary>
public sealed partial class InspectorView : UserControl
{
    private readonly EditableText _title;
    private readonly EditableText _notes;
    private AppState? _state;
    private Guid? _taskId;
    private bool _focusPending;

    public InspectorView()
    {
        InitializeComponent();
        _title = new EditableText(TitleBox);
        _notes = new EditableText(NotesBox);
        CloseButton.Content = Glyphs.For("xmark");
        DuplicateButton.Content = Glyphs.For("plus.square.on.square");
        DeleteButton.Content = Glyphs.For("trash");
        Empty.Show(new EmptyStateModel("sidebar.right", "No task selected", "Click a task to see and edit its details here."));

        CloseButton.Click += (_, _) =>
        {
            if (_state is not null) _state.IsInspectorVisible = false;
        };
        DuplicateButton.Click += (_, _) =>
        {
            if (_state is { } state && _taskId is { } id && state.Store.DuplicateTask(id) is { } copy) state.SelectTask(copy.Id);
        };
        DeleteButton.Click += (_, _) =>
        {
            if (_state is { } state && _taskId is { } id) state.Delete(new HashSet<Guid> { id });
        };
        Check.Toggled += (_, _) =>
        {
            if (_state is { } state && _taskId is { } id) state.ToggleCompleted(id);
        };

        TitleBox.TextChanged += (_, _) => SaveText(_title, t => t.Title, (t, text) => t with { Title = text });
        NotesBox.TextChanged += (_, _) => SaveText(_notes, t => t.Notes, (t, text) => t with { Notes = text });
        // A title that wasn't in the tree yet (the panel was hidden or empty) takes the pending focus once it arrives.
        TitleBox.Loaded += (_, _) => FocusTitleIfPending();

        DueRow.FlyoutOpening += (_, _) =>
        {
            DueFlyout.ResetMonth();
            FillFlyout(DueRow);
        };
        ReminderRow.FlyoutOpening += (_, _) => FillFlyout(ReminderRow);
        PriorityRow.FlyoutOpening += (_, _) => FillFlyout(PriorityRow);
        ProjectRow.FlyoutOpening += (_, _) => FillFlyout(ProjectRow);
        RepeatRow.FlyoutOpening += (_, _) =>
        {
            RepeatEditor.ResetCustomizing();
            FillFlyout(RepeatRow);
        };
        StatusRow.FlyoutOpening += (_, _) => FillFlyout(StatusRow);
    }

    private IEnumerable<PropertyRow> Rows => [DueRow, ReminderRow, PriorityRow, ProjectRow, RepeatRow, StatusRow];

    public void Render(AppState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _state = state;
        var model = InspectorModel.Build(state);
        if (model is null)
        {
            if (_taskId is not null) CloseFlyouts();
            _taskId = null;
            Form.Visibility = Visibility.Collapsed;
            EmptyPanel.Visibility = Visibility.Visible;
            return;
        }

        // Another task: the Mac rebuilds the form, so flyouts close and the panel scrolls back to the top.
        var switched = model.TaskId != _taskId;
        if (switched)
        {
            CloseFlyouts();
            _taskId = model.TaskId;
            Form.ChangeView(null, 0, null, true);
        }

        EmptyPanel.Visibility = Visibility.Collapsed;
        Form.Visibility = Visibility.Visible;

        _title.Push(model.Title, switched);
        // Secondary ink is primary at about 0.6 in both themes; opacity keeps it when the box is hovered or focused.
        TitleBox.Opacity = model.IsCompleted ? 0.6 : 1;
        Check.IsDone = model.IsCompleted;

        DueRow.Show(model.Due);
        ReminderRow.Show(model.Reminder);
        PriorityRow.Show(model.Priority);
        ProjectRow.Show(model.Project);
        RepeatRow.Show(model.Repeat);
        StatusRow.Show(model.Status);

        _notes.Push(model.Notes, switched);
        CreatedText.Text = model.CreatedText;

        foreach (var row in Rows)
        {
            if (row.IsFlyoutOpen) FillFlyout(row);
        }
    }

    /// <summary>Puts the cursor in the title, with its text selected, once the pending render has shown the task.</summary>
    public void FocusTitle()
    {
        // AppState raises the request right after Changed, whose render is still queued at normal priority.
        _focusPending = true;
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (TitleBox.IsLoaded) FocusTitleIfPending();
        });
    }

    /// <summary>Saves what the user typed, without an undo step (the text box has its own).</summary>
    private void SaveText(EditableText field, Func<TaskItem, string> read, Func<TaskItem, string, TaskItem> write)
    {
        if (_state is not { } state || _taskId is not { } id || state.Store.FindTask(id) is not { } task) return;
        if (field.Edited(read(task)) is not { } text) return;
        state.Store.UpdateTask(id, null, t => write(t, text));
    }

    private void FocusTitleIfPending()
    {
        if (!_focusPending) return;
        _focusPending = false;
        if (_taskId is null || Form.Visibility != Visibility.Visible) return;
        TitleBox.UpdateLayout();
        if (TitleBox.Focus(FocusState.Programmatic)) TitleBox.SelectAll();
    }

    private void CloseFlyouts()
    {
        foreach (var row in Rows) row.CloseFlyout();
    }

    /// <summary>Fills a row's flyout for the task as it is now.</summary>
    private void FillFlyout(PropertyRow row)
    {
        if (_state is not { } state || state.Store.FindTask(_taskId) is not { } task) return;
        var store = state.Store;
        var id = task.Id;

        if (row == DueRow)
        {
            DueFlyout.Render(state, task);
        }
        else if (row == ReminderRow)
        {
            ReminderOptions.Render(state, task, ReminderRow.CloseFlyout);
        }
        else if (row == PriorityRow)
        {
            PriorityOptions.Show(Priorities.Descending.Select(priority => OptionItem.Choice(
                priority.Title(),
                () =>
                {
                    store.UpdateTask(id, "Set Priority", t => t with { Priority = priority });
                    PriorityRow.CloseFlyout();
                },
                icon: "flag",
                isSelected: task.Priority == priority,
                iconToken: PriorityStyle.Token(priority),
                shortcut: "Ctrl+Shift+" + ((int)priority).ToString(CultureInfo.InvariantCulture))).ToList());
        }
        else if (row == ProjectRow)
        {
            var items = new List<OptionItem>
            {
                OptionItem.Choice(
                    "Inbox",
                    () =>
                    {
                        store.UpdateTask(id, "Move to Project", t => t with { ProjectId = null });
                        ProjectRow.CloseFlyout();
                    },
                    icon: "tray",
                    isSelected: task.ProjectId is null),
            };
            foreach (var project in store.Projects)
            {
                var projectId = project.Id;
                items.Add(OptionItem.Choice(
                    project.Name,
                    () =>
                    {
                        store.UpdateTask(id, "Move to Project", t => t with { ProjectId = projectId });
                        ProjectRow.CloseFlyout();
                    },
                    icon: project.Icon,
                    isSelected: task.ProjectId == projectId,
                    iconToken: "Project",
                    iconProjectColor: project.Color));
            }

            items.Add(OptionItem.Separator());
            items.Add(OptionItem.Choice(
                "New Project…",
                () =>
                {
                    ProjectRow.CloseFlyout();
                    state.RequestProjectEditor(null);
                },
                icon: "plus"));
            ProjectOptions.Show(items);
        }
        else if (row == RepeatRow)
        {
            RepeatEditor.Render(state, task);
        }
        else if (row == StatusRow)
        {
            StatusOptions.Show(Enum.GetValues<TaskStatus>().Select(status => OptionItem.Choice(
                status.Title(),
                () =>
                {
                    store.SetStatus([id], status);
                    StatusRow.CloseFlyout();
                },
                icon: StatusIcon(status),
                isSelected: task.Status == status)).ToList());
        }
    }

    /// <summary>The Mac's <c>TaskStatus.icon</c>, as in the status row and the board columns.</summary>
    private static string StatusIcon(TaskStatus status) => status switch
    {
        TaskStatus.Todo => "circle",
        TaskStatus.InProgress => "circle.lefthalf.filled",
        TaskStatus.Done => "checkmark.circle",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };

    /// <summary>
    /// Keeps a text box and one of the task's texts in step. The box reports line breaks as "\r" (the model keeps the
    /// Mac's "\n") and may not keep everything it's given, so what it showed right after the last push is remembered:
    /// a TextChanged that still shows exactly that, while the task still has the pushed text, is the push echoing back,
    /// not the user typing, and nothing is saved.
    /// </summary>
    private sealed class EditableText(TextBox box)
    {
        private string? _pushed;
        private string? _shown;

        /// <summary>
        /// Shows the task's text unless the user is typing in the box (<see cref="TextSync"/>) or it already shows it.
        /// <paramref name="force"/> (another task) always replaces it.
        /// </summary>
        public void Push(string text, bool force)
        {
            if (!force)
            {
                if (IsEcho(text)) return;
                var hasFocus = box.FocusState != FocusState.Unfocused;
                if (!TextSync.ShouldPush(Normalize(box.Text), Normalize(text), hasFocus)) return;
            }

            box.Text = text;
            _pushed = text;
            _shown = box.Text;
        }

        /// <summary>The box's text, with "\n" line breaks, when the user has changed it; otherwise null.</summary>
        public string? Edited(string current)
        {
            if (IsEcho(current)) return null;
            var text = Normalize(box.Text);
            return text == Normalize(current) ? null : text;
        }

        private static string Normalize(string text) =>
            text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

        private bool IsEcho(string current) =>
            string.Equals(box.Text, _shown, StringComparison.Ordinal) && string.Equals(current, _pushed, StringComparison.Ordinal);
    }
}
