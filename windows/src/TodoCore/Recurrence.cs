using System.Globalization;

namespace TodoCore;

public enum RecurrenceFrequency
{
    Daily,
    Weekly,
    Monthly,
    Yearly,
}

public static class RecurrenceFrequencyExtensions
{
    public static string Title(this RecurrenceFrequency frequency) => frequency switch
    {
        RecurrenceFrequency.Daily => "Daily",
        RecurrenceFrequency.Weekly => "Weekly",
        RecurrenceFrequency.Monthly => "Monthly",
        RecurrenceFrequency.Yearly => "Yearly",
        _ => throw new ArgumentOutOfRangeException(nameof(frequency)),
    };

    public static string Unit(this RecurrenceFrequency frequency, bool plural) => frequency switch
    {
        RecurrenceFrequency.Daily => plural ? "days" : "day",
        RecurrenceFrequency.Weekly => plural ? "weeks" : "week",
        RecurrenceFrequency.Monthly => plural ? "months" : "month",
        RecurrenceFrequency.Yearly => plural ? "years" : "year",
        _ => throw new ArgumentOutOfRangeException(nameof(frequency)),
    };
}

/// <summary>How a task repeats: every <c>Interval</c> days, weeks, months or years.</summary>
/// <param name="Frequency">The unit of repetition.</param>
/// <param name="Interval">How many units between occurrences (at least 1 when built with <see cref="Create"/>).</param>
/// <param name="Weekdays">
/// Weekdays for weekly rules (1 = Sunday … 7 = Saturday). Empty means "the same weekday as the due date".
/// </param>
public sealed record RecurrenceRule(RecurrenceFrequency Frequency, int Interval, ValueList<int> Weekdays)
{
    private static readonly int[] WeekdayDays = { 2, 3, 4, 5, 6 };

    /// <summary>Far beyond anything the editor offers; keeps date arithmetic safe from a hand-edited file.</summary>
    public const int MaximumInterval = 999;

    /// <summary>Clamps the interval to 1…<see cref="MaximumInterval"/> and keeps only distinct weekdays 1…7, sorted.</summary>
    public static RecurrenceRule Create(RecurrenceFrequency frequency, int interval = 1, IEnumerable<int>? weekdays = null) =>
        new(
            frequency,
            Math.Clamp(interval, 1, MaximumInterval),
            new ValueList<int>((weekdays ?? Array.Empty<int>()).Where(d => d is >= 1 and <= 7).Distinct().OrderBy(d => d)));

    public static RecurrenceRule Daily { get; } = Create(RecurrenceFrequency.Daily);

    /// <summary>Monday to Friday. Named <c>EveryWeekday</c> because <c>Weekdays</c> is the instance property.</summary>
    public static RecurrenceRule EveryWeekday { get; } = Create(RecurrenceFrequency.Weekly, 1, WeekdayDays);

    public static RecurrenceRule Weekly { get; } = Create(RecurrenceFrequency.Weekly);

    public static RecurrenceRule Monthly { get; } = Create(RecurrenceFrequency.Monthly);

    public static RecurrenceRule Yearly { get; } = Create(RecurrenceFrequency.Yearly);

    /// <summary>The ready-made rules offered before "Custom…".</summary>
    public static IReadOnlyList<(string Title, RecurrenceRule Rule)> Presets { get; } = new (string, RecurrenceRule)[]
    {
        ("Every Day", Daily),
        ("Every Weekday", EveryWeekday),
        ("Every Week", Weekly),
        ("Every Month", Monthly),
        ("Every Year", Yearly),
    };

    /// <summary>
    /// The first occurrence strictly after <paramref name="after"/>.
    /// For monthly and yearly rules, <paramref name="preferredDay"/> is the day of the month the series aims
    /// for. Without it, a series that passes through a short month (Jan 31 → Feb 28) would
    /// stay on the 28th for good.
    /// </summary>
    public DateTime NextDate(DateTime after, int? preferredDay, TodoCalendar calendar)
    {
        var step = Math.Max(1, Interval);
        switch (Frequency)
        {
            case RecurrenceFrequency.Daily:
                return calendar.AddDays(after, step);
            case RecurrenceFrequency.Monthly:
                return SettingDay(preferredDay, calendar.AddMonths(after, step), calendar);
            case RecurrenceFrequency.Yearly:
                return SettingDay(preferredDay, calendar.AddYears(after, step), calendar);
            case RecurrenceFrequency.Weekly:
                if (Weekdays.Count == 0) return calendar.AddDays(after, 7 * step);
                var startWeek = calendar.StartOfWeek(after);
                for (var offset = 1; offset <= 7 * step + 7; offset++)
                {
                    var candidate = calendar.AddDays(after, offset);
                    if (!Weekdays.Contains(calendar.Weekday(candidate))) continue;
                    var days = calendar.DayDifference(startWeek, calendar.StartOfWeek(candidate));
                    var weeks = (int)Math.Round(days / 7.0, MidpointRounding.AwayFromZero);
                    if (weeks % step == 0) return candidate;
                }
                return calendar.AddDays(after, 7 * step);
            default:
                throw new InvalidOperationException($"Unknown frequency {Frequency}.");
        }
    }

    public string Summary
    {
        get
        {
            if (Frequency == RecurrenceFrequency.Weekly)
            {
                if (Interval == 1 && Weekdays.SequenceEqual(WeekdayDays)) return "Every weekday";
                var baseText = Interval == 1 ? "Weekly" : $"Every {Interval} weeks";
                if (Weekdays.Count == 0) return baseText;
                var symbols = CultureInfo.CurrentCulture.DateTimeFormat.AbbreviatedDayNames;
                var names = Weekdays.Select(d => symbols[d - 1]);
                return $"{baseText} on {string.Join(", ", names)}";
            }
            if (Interval == 1) return Frequency.Title();
            return $"Every {Interval} {Frequency.Unit(true)}";
        }
    }

    /// <summary>Moves <paramref name="date"/> to <paramref name="day"/> of its month, or the month's last day when the month is shorter.</summary>
    private static DateTime SettingDay(int? day, DateTime date, TodoCalendar calendar)
    {
        if (day is not { } wanted) return date;
        var target = Math.Min(wanted, calendar.DaysInMonth(date));
        var local = calendar.ToLocal(date);
        return calendar.FromLocal(local.AddDays(target - local.Day));
    }
}
