using TodoApp.State;
using TodoCore;

namespace TodoApp;

/// <summary>Schedules nothing. Stands in until reminders are delivered as Windows notifications.</summary>
public sealed class NullReminderScheduler : IReminderScheduler
{
    // Never raised, so the accessors don't keep handlers.
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
