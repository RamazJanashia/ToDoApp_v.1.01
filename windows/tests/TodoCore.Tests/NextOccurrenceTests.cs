using TodoCore;
using static TodoCore.Tests.TestSupport;

namespace TodoCore.Tests;

public class NextOccurrenceTests
{
    private static TaskItem DailyTask(DateTime due) => new()
    {
        Title = "Water plants",
        DueDate = due,
        Recurrence = RecurrenceRule.Daily,
        Subtasks = ValueList.Of(new Subtask { Title = "Ferns", IsDone = true }),
    };

    [Fact]
    public void OverdueDailyTask_JumpsPastToday()
    {
        var daily = DailyTask(D(2026, 10, 20));
        Assert.Equal(D(2026, 10, 24), daily.NextOccurrence(Now, Berlin)?.DueDate);
    }

    [Fact]
    public void NextOccurrence_IsANewOpenTask()
    {
        var daily = DailyTask(D(2026, 10, 20));
        var next = daily.NextOccurrence(Now, Berlin);
        Assert.NotNull(next);
        Assert.NotEqual(daily.Id, next.Id);
        Assert.False(next.IsCompleted);
    }

    [Fact]
    public void SubtasksReset()
    {
        var next = DailyTask(D(2026, 10, 20)).NextOccurrence(Now, Berlin);
        Assert.NotNull(next);
        Assert.Single(next.Subtasks);
        Assert.All(next.Subtasks, s => Assert.False(s.IsDone));
    }

    [Fact]
    public void EarlyCompletion_KeepsTheCadence() =>
        Assert.Equal(D(2026, 10, 31), DailyTask(D(2026, 10, 30)).NextOccurrence(Now, Berlin)?.DueDate);

    [Fact]
    public void TimedTask_KeepsItsTime()
    {
        var timed = new TaskItem
        {
            Title = "Standup",
            DueDate = D(2026, 10, 23, 9),
            HasDueTime = true,
            Recurrence = RecurrenceRule.Daily,
        };
        Assert.Equal(D(2026, 10, 24, 9), timed.NextOccurrence(Now, Berlin)?.DueDate);
    }

    [Fact]
    public void NonRepeatingTask_HasNoNext() =>
        Assert.Null(new TaskItem { Title = "Once" }.NextOccurrence(Now, Berlin));

    [Fact]
    public void NextOccurrence_ResetsStatusAndCompletion()
    {
        var done = DailyTask(D(2026, 10, 23)).WithCompleted(true, Now) with { NextOccurrenceId = Guid.NewGuid() };
        var next = done.NextOccurrence(Now, Berlin);
        Assert.NotNull(next);
        Assert.Null(next.CompletedAt);
        Assert.Null(next.NextOccurrenceId);
        Assert.Equal(Now, next.CreatedAt);
    }

    // Complete each occurrence on its due day so catch-up logic doesn't skip ahead.
    private static List<DateTime> Chain(TaskItem task, int count)
    {
        var current = task;
        var dates = new List<DateTime>();
        for (var i = 0; i < count; i++)
        {
            var next = current.NextOccurrence(current.DueDate!.Value, Berlin);
            if (next is null) break;
            dates.Add(next.DueDate!.Value);
            current = next;
        }
        return dates;
    }

    [Fact]
    public void MonthlyOn31st_ReturnsToMonthEndAfterFebruary()
    {
        var task = new TaskItem { Title = "Invoice", DueDate = D(2026, 1, 31), Recurrence = RecurrenceRule.Monthly };
        Assert.Equal(new[] { D(2026, 2, 28), D(2026, 3, 31), D(2026, 4, 30) }, Chain(task, 3));
    }

    [Fact]
    public void YearlyOnFeb29_ReturnsToFeb29InNextLeapYear()
    {
        var task = new TaskItem { Title = "Leap", DueDate = D(2028, 2, 29), Recurrence = RecurrenceRule.Yearly };
        Assert.Equal(D(2032, 2, 29), Chain(task, 4).Last());
    }

    [Fact]
    public void ADayTheUserPicked_BecomesTheNewTarget()
    {
        var task = new TaskItem { Title = "Moved", DueDate = D(2026, 3, 15), Recurrence = RecurrenceRule.Monthly };
        Assert.Equal(new[] { D(2026, 4, 15) }, Chain(task, 1));
    }

    [Fact]
    public void PreferredDay_RememberedWinsOnlyOnClampedMonthEnd()
    {
        Assert.Equal(31, TaskItem.PreferredDay(D(2026, 2, 28), 31, Berlin));
        Assert.Equal(15, TaskItem.PreferredDay(D(2026, 3, 15), 31, Berlin));
        Assert.Equal(28, TaskItem.PreferredDay(D(2026, 2, 28), null, Berlin));
    }
}
