namespace TodoApp.State;

/// <summary>
/// An undo/redo stack with the semantics of <c>NSUndoManager</c>: an action registered while undoing lands on
/// the redo stack, one registered while redoing lands on the undo stack (keeping redo), and any other
/// registration lands on the undo stack and clears redo.
/// </summary>
public sealed class UndoManager
{
    private readonly Stack<Entry> _undo = new();
    private readonly Stack<Entry> _redo = new();
    private bool _undoing;
    private bool _redoing;

    private readonly record struct Entry(string Name, Action Action);

    public event EventHandler? Changed;

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public string? UndoActionName => _undo.Count > 0 ? _undo.Peek().Name : null;

    public string? RedoActionName => _redo.Count > 0 ? _redo.Peek().Name : null;

    public void Register(string actionName, Action undo)
    {
        ArgumentNullException.ThrowIfNull(actionName);
        ArgumentNullException.ThrowIfNull(undo);
        var entry = new Entry(actionName, undo);
        if (_undoing)
        {
            _redo.Push(entry);
            return;
        }

        _undo.Push(entry);
        if (_redoing) return;
        _redo.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Undo()
    {
        if (_undo.Count == 0) return;
        var entry = _undo.Pop();
        _undoing = true;
        try { entry.Action(); }
        finally { _undoing = false; }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Redo()
    {
        if (_redo.Count == 0) return;
        var entry = _redo.Pop();
        _redoing = true;
        try { entry.Action(); }
        finally { _redoing = false; }
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
