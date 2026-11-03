using TodoCore;

namespace TodoApp.State;

public interface IReminderScheduler
{
    /// <summary>Clears reminders left over from a previous run. Called once, before the first <see cref="Sync"/>.</summary>
    void Activate();

    void Sync(IReadOnlyList<TaskItem> tasks, IReadOnlyList<Project> projects);

    /// <summary>True when Windows won't show this app's notifications, so a reminder the user sets wouldn't appear.</summary>
    bool AreNotificationsBlocked { get; }

    /// <summary>Raised when the user clicks a reminder; carries the task to show.</summary>
    event EventHandler<Guid>? OpenTaskRequested;
}
