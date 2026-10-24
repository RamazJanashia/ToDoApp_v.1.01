namespace TodoCore;

public static class MonthGrid
{
    /// <summary>Six full weeks covering the month that contains <paramref name="containing"/>, starting on the calendar's first weekday.</summary>
    public static IReadOnlyList<DateTime> Days(DateTime containing, TodoCalendar calendar)
    {
        var monthStart = calendar.StartOfMonth(containing);
        var leading = (calendar.Weekday(monthStart) - calendar.FirstWeekday + 7) % 7;
        var gridStart = calendar.AddDays(monthStart, -leading);
        return Enumerable.Range(0, 42).Select(i => calendar.AddDays(gridStart, i)).ToList();
    }
}
