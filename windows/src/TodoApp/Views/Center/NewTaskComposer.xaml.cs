using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using TodoApp.State;
using Windows.System;

namespace TodoApp;

/// <summary>
/// The "+ New task" line under the list (NewTaskComposer in TaskListView.swift). Open, it's a field that stays open for
/// quick entry: Enter adds the task and selects it, Esc closes, and losing focus while empty closes. The typed text is
/// the field's own, so re-renders never touch it.
/// </summary>
public sealed partial class NewTaskComposer : UserControl
{
    private AppState? _state;
    private bool _open;

    public NewTaskComposer()
    {
        InitializeComponent();
        PlusGlyph.Glyph = Glyphs.For("plus");
    }

    public void Render(AppState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _state = state;
        if (state.IsComposing == _open) return;

        _open = state.IsComposing;
        Opener.Visibility = _open ? Visibility.Collapsed : Visibility.Visible;
        Editor.Visibility = _open ? Visibility.Visible : Visibility.Collapsed;
        if (_open)
        {
            // After this layout pass, once the field is showing.
            DispatcherQueue.TryEnqueue(() =>
            {
                if (_open) Input.Focus(FocusState.Programmatic);
            });
        }
        else
        {
            Input.Text = string.Empty;
        }
    }

    private void OnOpen(object sender, RoutedEventArgs e)
    {
        if (_state is not null) _state.IsComposing = true;
    }

    private void OnInputKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            Commit();
        }
        else if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            Close();
        }
    }

    private void OnInputLostFocus(object sender, RoutedEventArgs e)
    {
        if (_open && Input.Text.Trim().Length == 0) Close();
    }

    private void Commit()
    {
        if (_state is not { } state) return;
        var title = Input.Text.Trim();
        if (title.Length == 0)
        {
            Close();
            return;
        }

        var task = state.CreateTask(title);
        state.SelectTask(task.Id);
        Input.Text = string.Empty;
        Input.Focus(FocusState.Programmatic);
    }

    private void Close()
    {
        Input.Text = string.Empty;
        if (_state is not null) _state.IsComposing = false;
    }
}
