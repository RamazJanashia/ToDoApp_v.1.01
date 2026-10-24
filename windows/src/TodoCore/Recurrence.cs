namespace TodoCore;

public enum RecurrenceFrequency
{
    Daily,
    Weekly,
    Monthly,
    Yearly,
}

// Stub so TaskItem compiles; Task 2 completes this file.
public sealed record RecurrenceRule(RecurrenceFrequency Frequency, int Interval, ValueList<int> Weekdays);
