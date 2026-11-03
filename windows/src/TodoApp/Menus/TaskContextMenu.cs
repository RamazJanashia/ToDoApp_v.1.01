using Microsoft.UI.Xaml.Controls;
using TodoApp.State;
using TodoCore;
using TaskStatus = TodoCore.TaskStatus;

namespace TodoApp;

/// <summary>
/// Right-click actions shared by list rows, board cards and calendar chips (TaskContextMenu in TaskRowView.swift).
/// The items are rebuilt each time the menu opens, so the labels, the priority check and the project list are current.
/// </summary>
public static class TaskContextMenu
{
    public static MenuFlyout Build(TaskItem task, AppState state)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(state);

        var flyout = new MenuFlyout();
        var id = task.Id;
        flyout.Opening += (_, _) => Fill(flyout, state.Store.FindTask(id) ?? task, state);
        Fill(flyout, task, state);
        return flyout;
    }

    private static void Fill(MenuFlyout flyout, TaskItem task, AppState state)
    {
        var id = task.Id;
        var ids = new[] { id };
        var items = flyout.Items;
        items.Clear();

        items.Add(Item("Show Details", () => state.RevealInInspector(id, focusTitle: false)));
        items.Add(Item(task.IsCompleted ? "Mark as Incomplete" : "Complete", () => state.ToggleCompleted(id)));
        items.Add(new MenuFlyoutSeparator());

        var due = new MenuFlyoutSubItem { Text = "Due Date" };
        due.Items.Add(Item("Today", () => state.SetDue(QuickDue.Today, new[] { id })));
        due.Items.Add(Item("Tomorrow", () => state.SetDue(QuickDue.Tomorrow, new[] { id })));
        due.Items.Add(Item("Next Week", () => state.SetDue(QuickDue.NextWeek, new[] { id })));
        if (task.DueDate is not null)
        {
            due.Items.Add(new MenuFlyoutSeparator());
            due.Items.Add(Item("Remove Due Date", () => state.SetDueDay(null, ids)));
        }

        items.Add(due);

        var priority = new MenuFlyoutSubItem { Text = "Priority" };
        foreach (var level in Priorities.Descending)
        {
            var item = new ToggleMenuFlyoutItem { Text = level.Title(), IsChecked = task.Priority == level };
            item.Click += (_, _) => state.SetPriority(level, ids);
            priority.Items.Add(item);
        }

        items.Add(priority);

        var move = new MenuFlyoutSubItem { Text = "Move to" };
        move.Items.Add(Item("No Project", () => state.Move(ids, null)));
        if (state.Store.Projects.Count > 0) move.Items.Add(new MenuFlyoutSeparator());
        foreach (var row in state.Store.Projects.Outline())
        {
            var projectId = row.Id;
            move.Items.Add(Item(new string(' ', 4 * row.Depth) + row.Project.Name, () => state.Move(ids, projectId)));
        }

        items.Add(move);

        var status = new MenuFlyoutSubItem { Text = "Status" };
        foreach (var value in Enum.GetValues<TaskStatus>())
        {
            status.Items.Add(Item(value.Title(), () => state.Store.SetStatus(ids, value)));
        }

        items.Add(status);
        items.Add(new MenuFlyoutSeparator());

        items.Add(Item("Duplicate", () =>
        {
            if (state.Store.DuplicateTask(id) is { } copy) state.SelectTask(copy.Id);
        }));
        items.Add(Item("Delete", () => state.Delete(new HashSet<Guid> { id })));
    }

    private static MenuFlyoutItem Item(string text, Action action)
    {
        var item = new MenuFlyoutItem { Text = text };
        item.Click += (_, _) => action();
        return item;
    }
}
