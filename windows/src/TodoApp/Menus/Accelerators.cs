using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using TodoApp.State;
using TodoCore;
using Windows.System;

namespace TodoApp;

/// <summary>
/// The keyboard shortcuts of the shortcut table, as hidden accelerators on the window's root (the Mac's menu bar
/// shortcuts, with Ctrl for ⌘). The "More" menu shows the same keys as text. Shortcuts that need a task do nothing
/// without a selection. The bare keys (arrows, Space, Enter, Delete, Esc) are not here: <see cref="MainWindow"/> hands
/// them to <see cref="AppState.HandleKey"/>, which leaves them to text boxes.
/// </summary>
public static class Accelerators
{
    /// <summary>The comma key; <see cref="VirtualKey"/> has no name for it.</summary>
    private const VirtualKey Comma = (VirtualKey)188;

    private const VirtualKeyModifiers Ctrl = VirtualKeyModifiers.Control;
    private const VirtualKeyModifiers CtrlShift = VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift;

    public static void Install(UIElement root, MainWindow window)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(window);
        root.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;
        var state = window.State;

        void Add(VirtualKey key, VirtualKeyModifiers modifiers, Action action)
        {
            var accelerator = new KeyboardAccelerator { Key = key, Modifiers = modifiers };
            accelerator.Invoked += (_, e) =>
            {
                e.Handled = true;
                action();
            };
            root.KeyboardAccelerators.Add(accelerator);
        }

        void WithTask(Action<Guid> action)
        {
            if (state.SelectedTaskId is { } id) action(id);
        }

        // Creating and finding
        Add(VirtualKey.N, Ctrl, state.BeginNewTask);
        Add(VirtualKey.N, CtrlShift, () => state.RequestProjectEditor(null));
        Add(VirtualKey.F, Ctrl, () =>
        {
            if (state.Shows(WorkspaceElement.ToolbarSearch)) state.FocusSearch();
        });
        Add(Comma, Ctrl, window.ShowSettings);

        // The selected task
        Add(VirtualKey.Enter, Ctrl, () => WithTask(_ => state.ToggleSelectedCompleted()));
        Add(VirtualKey.E, Ctrl, () => WithTask(id => state.RevealInInspector(id, focusTitle: true)));
        Add(VirtualKey.D, Ctrl, () => WithTask(_ => state.DuplicateSelected()));
        Add(VirtualKey.T, Ctrl, () => WithTask(id => state.SetDue(QuickDue.Today, new[] { id })));
        Add(VirtualKey.T, CtrlShift, () => WithTask(id => state.SetDue(QuickDue.Tomorrow, new[] { id })));
        // Ctrl+Shift+3 / 2 / 1 / 0: High, Medium, Low, No Priority (Alt+1…5 are the Go menu).
        foreach (var priority in Priorities.Descending)
        {
            var key = VirtualKey.Number0 + (int)priority;
            Add(key, CtrlShift, () => WithTask(id => state.SetPriority(priority, [id])));
        }

        // Views and panels
        // A view or place switched off in Settings has no shortcut either, as its menu item is disabled.
        foreach (var mode in Enum.GetValues<ViewMode>())
        {
            Add(VirtualKey.Number1 + (int)mode, Ctrl, () =>
            {
                if (state.Shows(mode.Element())) state.ViewMode = mode;
            });
        }

        Add(VirtualKey.S, CtrlShift, () => state.IsSidebarVisible = !state.IsSidebarVisible);
        Add(VirtualKey.I, CtrlShift, () => state.IsInspectorVisible = !state.IsInspectorVisible);

        // Go: All Tasks, Today, Upcoming, Completed, Dashboard
        SidebarItem[] places = [SidebarItem.AllTasks, SidebarItem.Today, SidebarItem.Upcoming, SidebarItem.Completed, SidebarItem.Dashboard];
        for (var i = 0; i < places.Length; i++)
        {
            var place = places[i];
            Add(VirtualKey.Number1 + i, VirtualKeyModifiers.Menu, () => state.SelectIfShown(place));
        }

        // Undo and redo
        Add(VirtualKey.Z, Ctrl, () => Undo(window));
        Add(VirtualKey.Y, Ctrl, () => Redo(window));
        Add(VirtualKey.Z, CtrlShift, () => Redo(window));
    }

    /// <summary>
    /// Ctrl+Z: while a text box has focus and something to undo, it undoes the typing (the box's own history);
    /// otherwise the last change to the tasks.
    /// </summary>
    public static void Undo(MainWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (FocusedTextBox(window) is { CanUndo: true } box) box.Undo();
        else window.State.Store.Undo.Undo();
    }

    public static void Redo(MainWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (FocusedTextBox(window) is { CanRedo: true } box) box.Redo();
        else window.State.Store.Undo.Redo();
    }

    private static TextBox? FocusedTextBox(MainWindow window) =>
        window.Content?.XamlRoot is { } xamlRoot ? FocusManager.GetFocusedElement(xamlRoot) as TextBox : null;
}
