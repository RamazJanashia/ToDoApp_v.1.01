using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TodoApp.State;
using TodoCore;

namespace TodoApp;

/// <summary>
/// One row of the subtask list. The title saves on every keystroke without an undo step, and a render never writes
/// into the box while it has focus (<see cref="TextSync"/>); toggling and deleting register undo steps like the Mac.
/// </summary>
public sealed partial class SubtaskRow : UserControl
{
    private readonly EditableText _title;
    private AppState? _state;
    private Guid _taskId;
    private Guid _subtaskId;
    private bool _isDone;
    private bool _hovering;
    private bool _checkHover;

    public SubtaskRow()
    {
        InitializeComponent();
        _title = new EditableText(TitleBox);
        CheckIcon.Glyph = Glyphs.For("square");
        RemoveButton.Content = Glyphs.For("xmark");

        TitleBox.TextChanged += (_, _) =>
        {
            Strike.Text = TitleBox.Text;
            Save();
        };
        CheckHit.PointerEntered += (_, _) => SetCheckHover(true);
        CheckHit.PointerExited += (_, _) => SetCheckHover(false);
        CheckHit.Tapped += (_, e) =>
        {
            e.Handled = true;
            Edit("Toggle Subtask", s => s with { IsDone = !s.IsDone });
        };
        RemoveButton.Click += (_, _) => RemoveSubtask();
        Root.PointerEntered += (_, _) => SetHover(true);
        Root.PointerExited += (_, _) => SetHover(false);
        RemoveButton.GotFocus += (_, _) => RemoveButton.Opacity = 1;
        RemoveButton.LostFocus += (_, _) => RemoveButton.Opacity = _hovering ? 1 : 0;
    }

    public void Bind(AppState state, Guid taskId)
    {
        ArgumentNullException.ThrowIfNull(state);
        _state = state;
        _taskId = taskId;
    }

    public void Show(Subtask subtask)
    {
        ArgumentNullException.ThrowIfNull(subtask);
        _subtaskId = subtask.Id;
        _isDone = subtask.IsDone;
        _title.Push(subtask.Title, force: false);
        Strike.Text = TitleBox.Text;
        CheckIcon.Glyph = Glyphs.For(subtask.IsDone ? "checkmark.square.fill" : "square");
        UpdateStates();
    }

    private void Save()
    {
        if (_state is not { } state || state.Store.FindTask(_taskId) is not { } task) return;
        if (task.Subtasks.FirstOrDefault(s => s.Id == _subtaskId) is not { } subtask) return;
        if (_title.Edited(subtask.Title) is not { } text) return;
        state.Store.UpdateTask(_taskId, null, t => t with { Subtasks = Replace(t, s => s with { Title = text }) });
    }

    private void Edit(string undoName, Func<Subtask, Subtask> change)
    {
        if (_state is not { } state) return;
        state.Store.UpdateTask(_taskId, undoName, t => t with { Subtasks = Replace(t, change) });
    }

    private void RemoveSubtask()
    {
        if (_state is not { } state) return;
        var id = _subtaskId;
        state.Store.UpdateTask(_taskId, "Delete Subtask", t => t with { Subtasks = t.Subtasks.RemoveAll(s => s.Id == id) });
    }

    private ValueList<Subtask> Replace(TaskItem task, Func<Subtask, Subtask> change)
    {
        var index = task.Subtasks.FindIndex(s => s.Id == _subtaskId);
        return index < 0 ? task.Subtasks : task.Subtasks.SetItem(index, change(task.Subtasks[index]));
    }

    private void SetHover(bool hovering)
    {
        _hovering = hovering;
        RemoveButton.Opacity = hovering ? 1 : 0;
    }

    private void SetCheckHover(bool hovering)
    {
        _checkHover = hovering;
        UpdateStates();
    }

    private void UpdateStates()
    {
        VisualStateManager.GoToState(this, _isDone ? "CheckDone" : _checkHover ? "CheckHover" : "CheckOpen", false);
        VisualStateManager.GoToState(this, _isDone ? "TitleDone" : "TitleOpen", false);
    }
}
