using System.Globalization;

namespace TodoCore;

/// <summary>
/// All calendar maths. Instants are UTC <see cref="DateTime"/>s; "local" values are wall-clock
/// times in <see cref="TimeZone"/> with <see cref="DateTimeKind.Unspecified"/>.
/// </summary>
public sealed class TodoCalendar
{
    private readonly DayOfWeek _firstDayOfWeek;

    public TodoCalendar(TimeZoneInfo timeZone, DayOfWeek firstDayOfWeek)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        TimeZone = timeZone;
        _firstDayOfWeek = firstDayOfWeek;
    }

    /// <summary>The machine's time zone and the current culture's first day of the week, read on every access.</summary>
    public static TodoCalendar Current =>
        new(TimeZoneInfo.Local, CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek);

    public TimeZoneInfo TimeZone { get; }

    /// <summary>1 = Sunday … 7 = Saturday.</summary>
    public int FirstWeekday => (int)_firstDayOfWeek + 1;

    public DateTime ToLocal(DateTime utc)
    {
        var instant = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        var local = TimeZoneInfo.ConvertTimeFromUtc(instant, TimeZone);
        return DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
    }

    public DateTime FromLocal(DateTime local)
    {
        var wall = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        TimeSpan offset;
        if (TimeZone.IsInvalidTime(wall))
        {
            // In a gap: interpret the wall clock with the offset from before the gap, which
            // lands the same amount after the gap (02:30 becomes 03:30).
            offset = TimeZone.GetUtcOffset(wall.AddDays(-1));
        }
        else if (TimeZone.IsAmbiguousTime(wall))
        {
            // Happens twice: take the earlier instant, which has the larger offset.
            offset = TimeZone.GetAmbiguousTimeOffsets(wall).Max();
        }
        else
        {
            offset = TimeZone.GetUtcOffset(wall);
        }
        return DateTime.SpecifyKind(wall - offset, DateTimeKind.Utc);
    }

    public DateTime Make(int year, int month, int day, int hour = 0, int minute = 0) =>
        FromLocal(new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified));

    public DateTime StartOfDay(DateTime utc) => FromLocal(ToLocal(utc).Date);

    /// <summary>Wall-clock arithmetic: the time of day is kept across DST changes.</summary>
    public DateTime AddDays(DateTime utc, int days) => FromLocal(ToLocal(utc).AddDays(days));

    /// <summary>Clamps to the end of the target month.</summary>
    public DateTime AddMonths(DateTime utc, int months) => FromLocal(ToLocal(utc).AddMonths(months));

    public DateTime AddYears(DateTime utc, int years) => FromLocal(ToLocal(utc).AddYears(years));

    /// <summary>That local day at hh:mm.</summary>
    public DateTime SettingTime(DateTime utc, int hour, int minute) =>
        FromLocal(ToLocal(utc).Date.Add(new TimeSpan(hour, minute, 0)));

    /// <summary>1 = Sunday … 7 = Saturday.</summary>
    public int Weekday(DateTime utc) => (int)ToLocal(utc).DayOfWeek + 1;

    public int Day(DateTime utc) => ToLocal(utc).Day;

    public int DaysInMonth(DateTime utc)
    {
        var local = ToLocal(utc);
        return DateTime.DaysInMonth(local.Year, local.Month);
    }

    public DateTime StartOfMonth(DateTime utc)
    {
        var local = ToLocal(utc);
        return Make(local.Year, local.Month, 1);
    }

    public DateTime StartOfWeek(DateTime utc)
    {
        var day = ToLocal(utc).Date;
        var back = ((int)day.DayOfWeek - (int)_firstDayOfWeek + 7) % 7;
        return FromLocal(day.AddDays(-back));
    }

    public bool IsSameDay(DateTime a, DateTime b) => ToLocal(a).Date == ToLocal(b).Date;

    public bool IsSameMonth(DateTime a, DateTime b)
    {
        var x = ToLocal(a);
        var y = ToLocal(b);
        return x.Year == y.Year && x.Month == y.Month;
    }

    public bool IsSameYear(DateTime a, DateTime b) => ToLocal(a).Year == ToLocal(b).Year;

    /// <summary>Whole days between the start of each day.</summary>
    public int DayDifference(DateTime from, DateTime to) =>
        (ToLocal(to).Date - ToLocal(from).Date).Days;
}
