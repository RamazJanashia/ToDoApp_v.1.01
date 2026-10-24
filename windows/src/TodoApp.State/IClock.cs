using TodoCore;

namespace TodoApp.State;

/// <summary>The current instant and the calendar to read it with, so state code never touches the system clock.</summary>
public interface IClock
{
    DateTime UtcNow { get; }

    TodoCalendar Calendar { get; }
}

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;

    /// <summary>Read on every access, so a changed time zone or first weekday is picked up.</summary>
    public TodoCalendar Calendar => TodoCalendar.Current;
}
