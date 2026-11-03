using TodoCore;

namespace TodoApp.State;

/// <summary>
/// Reminders switched off, for a run on another data folder: notifications belong to the app as a whole, not to one
/// folder, so trying things out must not clear or replace the real ones.
/// </summary>
public sealed class DisabledReminderScheduler : IReminderScheduler
{
    public bool AreNotificationsBlocked => false;

    public event EventHandler<Guid>? OpenTaskRequested
    {
        add { }
        remove { }
    }

    public void Activate()
    {
    }

    public void Sync(IReadOnlyList<TaskItem> tasks, IReadOnlyList<Project> projects)
    {
    }
}
