using TodoApp.State;
using TodoCore;

namespace TodoApp;

/// <summary>
/// The reminder choices (ReminderPopover in SchedulePopovers.swift): "None" and the eight presets. Without a due date it
/// only explains that one is needed; an all-day task gets a note that it's reminded relative to 9:00 AM. Picking one
/// closes the flyout.
/// </summary>
public sealed partial class ReminderFlyout : OptionList
{
    public void Render(AppState state, TaskItem task, Action dismiss)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(dismiss);

        if (task.DueDate is null)
        {
            Show([OptionItem.Caption("Set a due date first — reminders are sent relative to it.")]);
            return;
        }

        var store = state.Store;
        var id = task.Id;
        var items = new List<OptionItem>
        {
            OptionItem.Choice(
                "None",
                () =>
                {
                    store.UpdateTask(id, "Remove Reminder", t => t with { ReminderOffset = null });
                    dismiss();
                },
                icon: "bell.slash",
                isSelected: task.ReminderOffset is null),
            OptionItem.Separator(),
        };

        foreach (var offset in ReminderPreset.Offsets)
        {
            items.Add(OptionItem.Choice(
                ReminderPreset.Title(offset),
                () =>
                {
                    store.UpdateTask(id, "Set Reminder", t => t with { ReminderOffset = offset });
                    dismiss();
                },
                isSelected: task.ReminderOffset == offset));
        }

        if (!task.HasDueTime) items.Add(OptionItem.Footnote("All-day tasks are reminded relative to 9:00 AM."));
        Show(items);
    }
}
