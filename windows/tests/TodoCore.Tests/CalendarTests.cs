using TodoCore;
using static TodoCore.Tests.TestSupport;

namespace TodoCore.Tests;

public class CalendarTests
{
    [Fact]
    public void Calendar_AddDaysAcrossDst_KeepsWallClock()
    {
        var before = D(2026, 3, 28, 9);
        var after = Berlin.AddDays(before, 1);
        Assert.Equal(D(2026, 3, 29, 9), after);
        Assert.Equal(TimeSpan.FromHours(23), after - before);
    }

    [Fact]
    public void Calendar_GapTime_MovesForward()
    {
        // 2026-03-29 02:30 doesn't exist in Berlin; it becomes 03:30 CEST (01:30 UTC).
        var made = Berlin.Make(2026, 3, 29, 2, 30);
        Assert.Equal(new DateTime(2026, 3, 29, 1, 30, 0, DateTimeKind.Utc), made);
        Assert.Equal(Berlin.Make(2026, 3, 29, 3, 30), made);
    }

    [Fact]
    public void Calendar_OverlapTime_PicksEarlier()
    {
        // 2026-10-25 02:30 happens twice; the first one is CEST (00:30 UTC).
        var made = Berlin.Make(2026, 10, 25, 2, 30);
        Assert.Equal(new DateTime(2026, 10, 25, 0, 30, 0, DateTimeKind.Utc), made);
    }

    [Fact]
    public void Calendar_StartOfWeek_FollowsFirstWeekday()
    {
        var friday = D(2026, 10, 23, 15, 30);
        Assert.Equal(D(2026, 10, 19), Berlin.StartOfWeek(friday));

        var sundayFirst = new TodoCalendar(Berlin.TimeZone, DayOfWeek.Sunday);
        Assert.Equal(D(2026, 10, 18), sundayFirst.StartOfWeek(friday));
        Assert.Equal(1, sundayFirst.FirstWeekday);
        Assert.Equal(2, Berlin.FirstWeekday);
    }

    [Fact]
    public void ValueList_EqualityIsStructural()
    {
        var a = ValueList.Of(1, 2, 3);
        var b = new ValueList<int>(new[] { 1, 2, 3 });
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, b.Add(4));
        Assert.NotEqual(a, ValueList<int>.Empty);
        Assert.Equal(ValueList<int>.Empty, new ValueList<int>(Array.Empty<int>()));
    }

    [Fact]
    public void TaskItemsWithEqualListsAreEqual()
    {
        var id = Guid.NewGuid();
        var created = Now;
        var sub = new Subtask { Title = "s" };
        TaskItem Make() => new()
        {
            Id = id, Title = "t", CreatedAt = created,
            Subtasks = ValueList.Of(sub),
        };
        Assert.Equal(Make(), Make());
        Assert.NotEqual(Make(), Make() with { Subtasks = ValueList<Subtask>.Empty });
    }
}
