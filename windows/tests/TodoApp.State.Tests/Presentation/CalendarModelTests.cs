using TodoCore;
using static TodoApp.State.Tests.TestSupport;
using TaskStatus = TodoCore.TaskStatus;

namespace TodoApp.State.Tests;

public sealed class CalendarModelTests : IDisposable
{
    private readonly PresentationFixture _f = new();

    public void Dispose() => _f.Dispose();

    private CalendarModel Build() => CalendarModel.Build(_f.State);

    private static CalendarDayModel DayOf(CalendarModel model, int month, int day) =>
        model.Days.Single(d => d.Day == Berlin.Make(2026, month, day));

    [Fact]
    public void ShowsSixWeeksStartingOnTheFirstWeekday()
    {
        var model = Build();

        Assert.Equal("October 2026", model.MonthTitle);
        Assert.Equal(42, model.Days.Count);
        // 1 October 2026 is a Thursday, so a Monday-first grid starts on 28 September.
        Assert.Equal(Berlin.Make(2026, 9, 28), model.Days[0].Day);
        Assert.Equal(Berlin.Make(2026, 11, 8), model.Days[41].Day);
        Assert.Equal("28", model.Days[0].DayNumber);
        Assert.Equal("1", model.Days[3].DayNumber);
    }

    [Fact]
    public void WeekdaySymbols_StartAtTheFirstWeekdayInUppercase()
    {
        Assert.Equal(new[] { "MON", "TUE", "WED", "THU", "FRI", "SAT", "SUN" }, Build().WeekdaySymbols.ToArray());

        using var sunday = new PresentationFixture(new SundayClock());
        var model = CalendarModel.Build(sunday.State);

        Assert.Equal(new[] { "SUN", "MON", "TUE", "WED", "THU", "FRI", "SAT" }, model.WeekdaySymbols.ToArray());
        Assert.Equal(Berlin.Make(2026, 9, 27), model.Days[0].Day);
    }

    [Fact]
    public void FollowsTheShownMonth()
    {
        _f.State.CalendarMonth = D(2026, 12, 15);

        var model = Build();

        Assert.Equal("December 2026", model.MonthTitle);
        Assert.Equal(Berlin.Make(2026, 11, 30), model.Days[0].Day);
        Assert.False(model.Days[0].IsInMonth);
        Assert.True(model.Days[1].IsInMonth);
    }

    [Fact]
    public void DayFlags_TodaySelectedAndInMonth()
    {
        _f.State.CalendarSelectedDay = Berlin.Make(2026, 10, 12);

        var model = Build();

        Assert.True(DayOf(model, 10, 23).IsToday);
        Assert.Single(model.Days, d => d.IsToday);
        Assert.True(DayOf(model, 10, 12).IsSelected);
        Assert.Single(model.Days, d => d.IsSelected);
        Assert.False(DayOf(model, 9, 28).IsInMonth);
        Assert.True(DayOf(model, 10, 1).IsInMonth);
        Assert.False(DayOf(model, 11, 2).IsInMonth);
    }

    [Fact]
    public void Chips_AreSortedOpenFirstThenTimedThenDueThenSortOrder()
    {
        var day = Berlin.Make(2026, 10, 27);
        var allDayA = _f.Store.CreateTask("all-day A", null, day);
        var done = _f.Store.CreateTask("done", null, day);
        var timedLate = _f.Store.CreateTask("timed late", null, day);
        var allDayB = _f.Store.CreateTask("all-day B", null, day);
        var timedEarly = _f.Store.CreateTask("timed early", null, day);
        _f.Store.UpdateTask(timedLate.Id, null, t => t with { DueDate = D(2026, 10, 27, 15), HasDueTime = true });
        _f.Store.UpdateTask(timedEarly.Id, null, t => t with { DueDate = D(2026, 10, 27, 9), HasDueTime = true });
        _f.Store.ToggleCompleted(done.Id);

        var chips = DayOf(Build(), 10, 27).Chips;

        Assert.Equal(
            new[] { timedEarly.Id, timedLate.Id, allDayA.Id, allDayB.Id, done.Id },
            chips.Select(c => c.Id).ToArray());
    }

    [Fact]
    public void Chips_OnlyForScheduledTasksOnTheirDay()
    {
        _f.Store.CreateTask("Unscheduled");
        var task = _f.Store.CreateTask("Scheduled", null, Berlin.Make(2026, 10, 14));

        var model = Build();

        var chip = Assert.Single(model.Days.SelectMany(d => d.Chips));
        Assert.Equal(task.Id, chip.Id);
        Assert.Equal(chip, Assert.Single(DayOf(model, 10, 14).Chips));
    }

    [Fact]
    public void Chip_DescribesTheTask()
    {
        var a = _f.Store.CreateTask("", null, Berlin.Make(2026, 10, 14));
        var b = _f.Store.CreateTask("Done", null, Berlin.Make(2026, 10, 14));
        _f.Store.ToggleCompleted(b.Id);
        _f.State.SelectedTaskId = a.Id;

        var chips = DayOf(Build(), 10, 14).Chips;

        Assert.Equal("Untitled", chips[0].Title);
        Assert.True(chips[0].IsSelected);
        Assert.False(chips[0].IsCompleted);
        Assert.Equal("Done", chips[1].Title);
        Assert.True(chips[1].IsCompleted);
        Assert.False(chips[1].IsSelected);
        Assert.Equal(_f.Store.FindTask(a.Id), chips[0].Task);
    }

    [Fact]
    public void DotColour_DoneThenHighThenProjectThenSecondary()
    {
        var project = _f.Store.CreateProject("Home", "house", ProjectColor.Teal);
        var day = Berlin.Make(2026, 10, 20);
        var plain = _f.Store.CreateTask("plain", null, day);
        var filed = _f.Store.CreateTask("filed", project.Id, day);
        var urgent = _f.Store.CreateTask("urgent", project.Id, day);
        var finished = _f.Store.CreateTask("finished", project.Id, day);
        _f.Store.UpdateTask(urgent.Id, null, t => t with { Priority = Priority.High });
        _f.Store.UpdateTask(finished.Id, null, t => t with { Priority = Priority.High });
        _f.Store.ToggleCompleted(finished.Id);

        var chips = DayOf(Build(), 10, 20).Chips.ToDictionary(c => c.Id);

        Assert.Equal("Secondary", chips[plain.Id].DotToken);
        Assert.Null(chips[plain.Id].DotProjectColor);
        Assert.Equal("Project", chips[filed.Id].DotToken);
        Assert.Equal(ProjectColor.Teal, chips[filed.Id].DotProjectColor);
        Assert.Equal("Red", chips[urgent.Id].DotToken);
        Assert.Equal("Green", chips[finished.Id].DotToken);
    }

    [Fact]
    public void Tooltip_IsTheLongDate_AndCompactListsTheTasks()
    {
        var empty = DayOf(Build(), 10, 23);
        Assert.Equal("Friday, October 23, 2026", empty.Tooltip);
        Assert.Equal("Friday, October 23, 2026", empty.CompactTooltip);

        _f.Store.CreateTask("Write", null, Berlin.Make(2026, 10, 23));
        _f.Store.CreateTask("", null, Berlin.Make(2026, 10, 23));

        var day = DayOf(Build(), 10, 23);
        Assert.Equal("Friday, October 23, 2026", day.Tooltip);
        Assert.Equal("Friday, October 23, 2026\n• Write\n• Untitled", day.CompactTooltip);
    }

    [Fact]
    public void Calendar_InAProjectShowsOnlyThatProject()
    {
        var project = _f.Store.CreateProject("Home", "house", ProjectColor.Teal);
        _f.Store.CreateTask("Mine", project.Id, Berlin.Make(2026, 10, 20));
        _f.Store.CreateTask("Other", null, Berlin.Make(2026, 10, 20));
        _f.State.Select(SidebarItem.Project(project.Id));

        var chip = Assert.Single(Build().Days.SelectMany(d => d.Chips));

        Assert.Equal("Mine", chip.Title);
    }

    [Theory]
    [InlineData(200, 3, 7)]
    [InlineData(100, 5, 2)]
    [InlineData(100, 2, 3)]
    [InlineData(100, 3, 3)]
    [InlineData(40, 5, 0)]
    [InlineData(0, 0, 0)]
    public void MaxVisible_KeepsRoomForTheMoreLine(double height, int count, int expected)
    {
        Assert.Equal(expected, CalendarModel.MaxVisible(height, count));
    }
}
