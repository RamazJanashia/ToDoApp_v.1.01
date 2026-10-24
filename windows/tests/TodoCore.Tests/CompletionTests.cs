using TodoCore;
using TaskStatus = TodoCore.TaskStatus;
using static TodoCore.Tests.TestSupport;

namespace TodoCore.Tests;

public class CompletionTests
{
    [Fact]
    public void Completing_SetsStatusAndDate()
    {
        var task = new TaskItem { Title = "Ship" }.WithCompleted(true, Now);
        Assert.Equal(TaskStatus.Done, task.Status);
        Assert.Equal(Now, task.CompletedAt);
    }

    [Fact]
    public void LeavingDone_ClearsCompletedAt()
    {
        var task = new TaskItem { Title = "Ship" }.WithCompleted(true, Now).WithStatus(TaskStatus.InProgress, Now);
        Assert.Null(task.CompletedAt);
        Assert.False(task.IsCompleted);
    }

    [Fact]
    public void AllDayReminder_AnchorsAtNine()
    {
        var task = new TaskItem { Title = "All day", DueDate = Today, ReminderOffset = 3_600 };
        Assert.Equal(D(2026, 10, 23, 8), task.ReminderDate(Berlin));
    }

    [Fact]
    public void TimedReminder_SubtractsOffset()
    {
        var task = new TaskItem { Title = "Timed", DueDate = D(2026, 10, 23, 16), HasDueTime = true, ReminderOffset = 900 };
        Assert.Equal(D(2026, 10, 23, 15, 45), task.ReminderDate(Berlin));
    }

    [Fact]
    public void PastTimedTask_IsOverdue()
    {
        var task = new TaskItem { Title = "x", DueDate = D(2026, 10, 23, 9), HasDueTime = true };
        Assert.True(task.IsOverdue(Now, Berlin));
    }

    [Fact]
    public void AllDayDueToday_IsNotOverdue()
    {
        var task = new TaskItem { Title = "x", DueDate = Today };
        Assert.False(task.IsOverdue(Now, Berlin));
    }

    [Fact]
    public void Rescheduling_KeepsTimeOfDay()
    {
        var task = new TaskItem { Title = "Move me", DueDate = D(2026, 10, 23, 16, 30), HasDueTime = true };
        var moved = task.RescheduledTo(D(2026, 10, 27), Berlin);
        Assert.Equal(D(2026, 10, 27, 16, 30), moved.DueDate);
    }
}
