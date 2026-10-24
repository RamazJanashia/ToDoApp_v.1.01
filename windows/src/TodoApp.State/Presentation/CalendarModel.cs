using System.Globalization;
using TodoCore;

namespace TodoApp.State;

/// <summary>A task on a calendar day. <see cref="DotToken"/> is "Green" | "Red" | "Project" | "Secondary".</summary>
public sealed record CalendarChipModel(
    Guid Id,
    string Title,
    bool IsCompleted,
    bool IsSelected,
    string DotToken,
    ProjectColor? DotProjectColor,
    TaskItem Task);

/// <param name="Tooltip">The long date. Narrow cells show dots instead of titles and use <see cref="CompactTooltip"/>.</param>
public sealed record CalendarDayModel(
    DateTime Day,
    string DayNumber,
    bool IsToday,
    bool IsSelected,
    bool IsInMonth,
    IReadOnlyList<CalendarChipModel> Chips,
    string Tooltip)
{
    /// <summary>The date, then one "• title" line per task, for cells too narrow to print titles.</summary>
    public string CompactTooltip =>
        Chips.Count == 0 ? Tooltip : Tooltip + "\n" + string.Join("\n", Chips.Select(c => "• " + c.Title));
}

public sealed record CalendarModel(string MonthTitle, IReadOnlyList<string> WeekdaySymbols, IReadOnlyList<CalendarDayModel> Days)
{
    // Chips need about 21 px each under the 36 px day number; the "+N more" line takes 16 px more.
    private const double HeaderHeight = 36;
    private const double ChipHeight = 21;
    private const double MoreLineHeight = 16;

    public static CalendarModel Build(AppState s)
    {
        ArgumentNullException.ThrowIfNull(s);
        var now = s.Clock.UtcNow;
        var calendar = s.Clock.Calendar;
        var culture = CultureInfo.CurrentCulture;

        var monthStart = calendar.StartOfMonth(s.CalendarMonth);

        var names = culture.DateTimeFormat.AbbreviatedDayNames;
        var first = calendar.FirstWeekday - 1;
        var symbols = names.Skip(first).Concat(names.Take(first)).Select(n => n.ToUpper(culture)).ToList();

        var byDay = s.CalendarTasks
            .GroupBy(t => calendar.StartOfDay(t.DueDate!.Value))
            .ToDictionary(g => g.Key, g => g
                .OrderBy(t => t.IsCompleted)
                .ThenByDescending(t => t.HasDueTime)
                .ThenBy(t => t.DueDate ?? DateTime.MinValue)
                .ThenBy(t => t.SortOrder)
                .ToList());

        var days = MonthGrid.Days(monthStart, calendar).Select(day =>
        {
            var chips = byDay.TryGetValue(day, out var dayTasks)
                ? dayTasks.Select(t => Chip(t, s)).ToList()
                : new List<CalendarChipModel>();
            var local = calendar.ToLocal(day);
            return new CalendarDayModel(
                day,
                local.Day.ToString(culture),
                calendar.IsSameDay(day, now),
                s.CalendarSelectedDay is { } selected && calendar.IsSameDay(selected, day),
                calendar.IsSameMonth(day, monthStart),
                chips,
                local.ToString("D", culture));
        }).ToList();

        return new CalendarModel(calendar.ToLocal(monthStart).ToString(culture.DateTimeFormat.YearMonthPattern, culture), symbols, days);
    }

    /// <summary>How many chips fit in a day cell; when they don't all fit, room is kept for the "+N more" line.</summary>
    public static int MaxVisible(double cellHeight, int count)
    {
        var all = (int)((cellHeight - HeaderHeight) / ChipHeight);
        return count <= all ? all : Math.Max((int)((cellHeight - HeaderHeight - MoreLineHeight) / ChipHeight), 0);
    }

    private static CalendarChipModel Chip(TaskItem task, AppState s)
    {
        var project = s.Store.FindProject(task.ProjectId);
        string token;
        ProjectColor? color = null;
        if (task.IsCompleted)
        {
            token = "Green";
        }
        else if (task.Priority == Priority.High)
        {
            token = "Red";
        }
        else if (project is not null)
        {
            token = "Project";
            color = project.Color;
        }
        else
        {
            token = "Secondary";
        }

        return new CalendarChipModel(
            task.Id,
            task.Title.Length == 0 ? "Untitled" : task.Title,
            task.IsCompleted,
            s.SelectedTaskId == task.Id,
            token,
            color,
            task);
    }
}
