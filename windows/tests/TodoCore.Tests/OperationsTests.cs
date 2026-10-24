using TodoCore;
using TaskStatus = TodoCore.TaskStatus;
using static TodoCore.Tests.TestSupport;

namespace TodoCore.Tests;

public class OperationsTests
{
    private static HashSet<Guid> Ids(params Guid[] ids) => new(ids);

    [Fact]
    public void SetStatus_CompletingSpawnsNextOccurrence()
    {
        var series = new List<TaskItem> { new() { Title = "Gym", DueDate = Today, Recurrence = RecurrenceRule.Daily, SortOrder = 1 } };
        var gymId = series[0].Id;

        TaskOperations.SetStatus(TaskStatus.Done, Ids(gymId), series, Now, Berlin);

        Assert.Equal(2, series.Count);
        Assert.Null(series[0].Recurrence);
        Assert.Equal(series[1].Id, series[0].NextOccurrenceId);
    }

    [Fact]
    public void SetStatus_ReopeningRemovesUntouchedOccurrenceAndRestoresRule()
    {
        var series = new List<TaskItem> { new() { Title = "Gym", DueDate = Today, Recurrence = RecurrenceRule.Daily, SortOrder = 1 } };
        var gymId = series[0].Id;

        TaskOperations.SetStatus(TaskStatus.Done, Ids(gymId), series, Now, Berlin);
        TaskOperations.SetStatus(TaskStatus.Todo, Ids(gymId), series, Now, Berlin);

        Assert.Single(series);
        Assert.Equal(RecurrenceRule.Daily, series[0].Recurrence);
        Assert.Null(series[0].NextOccurrenceId);
        Assert.False(series[0].IsCompleted);
    }

    [Fact]
    public void SetStatus_ReopeningKeepsEditedOccurrence()
    {
        var series = new List<TaskItem> { new() { Title = "Gym", DueDate = Today, Recurrence = RecurrenceRule.Daily, SortOrder = 1 } };
        var gymId = series[0].Id;

        TaskOperations.SetStatus(TaskStatus.Done, Ids(gymId), series, Now, Berlin);
        series[1] = series[1] with { Notes = "Leg day" };
        TaskOperations.SetStatus(TaskStatus.InProgress, Ids(gymId), series, Now, Berlin);

        Assert.Equal(2, series.Count);
        Assert.Equal("Leg day", series[1].Notes);
    }

    [Fact]
    public void SetStatus_SpawnedOccurrenceIsAppendedAtEnd()
    {
        var gym = new TaskItem { Title = "Gym", DueDate = Today, Recurrence = RecurrenceRule.Daily, SortOrder = 1 };
        var other = new TaskItem { Title = "Other", SortOrder = 2 };
        var tasks = new List<TaskItem> { gym, other };

        TaskOperations.SetStatus(TaskStatus.Done, Ids(gym.Id), tasks, Now, Berlin);

        Assert.Equal(3, tasks.Count);
        Assert.Equal(gym.Id, tasks[0].Id);
        Assert.Equal(other.Id, tasks[1].Id);
        Assert.Equal(tasks[0].NextOccurrenceId, tasks[2].Id);
    }

    private static readonly TaskItem Open1 = new() { Title = "a", SortOrder = 5 };
    private static readonly TaskItem Open2 = new() { Title = "b", SortOrder = 10 };
    private static readonly TaskItem Open3 = new() { Title = "c", SortOrder = 20 };
    private static readonly TaskItem Done1 = new() { Title = "done", Status = TaskStatus.Done, SortOrder = 3, CompletedAt = Now };
    private static readonly TaskItem[] Shown = { Open1, Open2, Open3, Done1 };

    [Fact]
    public void SortOrder_DroppingAboveFirstCompletedRowKeepsTaskLastAmongOpenOnes()
    {
        var boundary = TaskOperations.SortOrderPlacing(Open3.Id, Done1.Id, Shown);
        Assert.True((boundary ?? 0) > 10, $"{boundary}");
    }

    [Fact]
    public void SortOrder_DroppingBetweenTwoRowsLandsBetweenThem() =>
        Assert.Equal(7.5, TaskOperations.SortOrderPlacing(Open3.Id, Open2.Id, Shown));

    [Fact]
    public void SortOrder_DroppingOnFirstRowGoesAboveIt() =>
        Assert.Equal(4, TaskOperations.SortOrderPlacing(Open3.Id, Open1.Id, Shown));

    [Fact]
    public void SortOrder_DroppingOnItselfDoesNothing() =>
        Assert.Null(TaskOperations.SortOrderPlacing(Open1.Id, Open1.Id, Shown));
}
