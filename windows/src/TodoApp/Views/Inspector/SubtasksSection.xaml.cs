using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.System;
using TodoApp.State;
using TodoCore;

namespace TodoApp;

/// <summary>
/// The subtask list of the selected task and its "Add subtask" field. Rows are kept per subtask and updated in place,
/// so a render while the user types in one never rebuilds its text box. Switching task starts a fresh list.
/// </summary>
public sealed partial class SubtasksSection : UserControl
{
    private readonly Dictionary<Guid, SubtaskRow> _rows = [];
    private AppState? _state;
    private Guid? _taskId;

    public SubtasksSection()
    {
        InitializeComponent();
        PlusIcon.Glyph = Glyphs.For("plus");
        AddBox.KeyDown += (_, e) =>
        {
            if (e.Key != VirtualKey.Enter) return;
            e.Handled = true;
            Add();
        };
    }

    public void Render(AppState state, Guid taskId, IReadOnlyList<Subtask> subtasks, string? progress)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(subtasks);
        _state = state;
        if (_taskId != taskId)
        {
            _taskId = taskId;
            _rows.Clear();
            Rows.Children.Clear();
            AddBox.Text = "";
        }

        Progress.Text = progress ?? "";
        Progress.Visibility = progress is null ? Visibility.Collapsed : Visibility.Visible;

        var ids = subtasks.Select(s => s.Id).ToHashSet();
        foreach (var gone in _rows.Keys.Where(id => !ids.Contains(id)).ToList())
        {
            Rows.Children.Remove(_rows[gone]);
            _rows.Remove(gone);
        }

        for (var i = 0; i < subtasks.Count; i++)
        {
            var subtask = subtasks[i];
            if (!_rows.TryGetValue(subtask.Id, out var row))
            {
                row = new SubtaskRow();
                row.Bind(state, taskId);
                _rows[subtask.Id] = row;
                Rows.Children.Insert(Math.Min(i, Rows.Children.Count), row);
            }

            row.Show(subtask);
        }
    }

    private void Add()
    {
        if (_state is not { } state || _taskId is not { } id) return;
        var title = AddBox.Text.Trim();
        if (title.Length == 0) return;
        state.Store.UpdateTask(id, "Add Subtask", t => t with { Subtasks = t.Subtasks.Add(new Subtask { Title = title }) });
        AddBox.Text = "";
        AddBox.Focus(FocusState.Programmatic);
    }
}
