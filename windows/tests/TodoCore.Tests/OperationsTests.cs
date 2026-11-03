using TodoCore;
using TaskStatus = TodoCore.TaskStatus;
using static TodoCore.Tests.TestSupport;

namespace TodoCore.Tests;

public class OperationsTests
{
    private static HashSet<Guid> Ids(params Guid[] ids) => new(ids);

    private static List<TaskItem> CompletedSeries()
    {
        var series = new List<TaskItem> { new() { Title = "Gym", DueDate = Today, Recurrence = RecurrenceRule.Daily, SortOrder = 1 } };
        TaskOperations.SetStatus(TaskStatus.Done, Ids(series[0].Id), series, Now, Berlin);
        return series;
    }

    private static void Reopen(List<TaskItem> series, Guid id) =>
        TaskOperations.SetStatus(TaskStatus.Todo, Ids(id), series, Now, Berlin);

    [Fact]
    public void Completing_RecordsOccurrenceDueDateAndRule()
    {
        var series = CompletedSeries();
        Assert.Equal(series[1].DueDate, series[0].NextOccurrenceDueDate);
        Assert.Equal(series[1].Recurrence, series[0].NextOccurrenceRule);
    }

    [Fact]
    public void Reopening_ClearsTheOccurrenceLink()
    {
        var series = CompletedSeries();
        Reopen(series, series[0].Id);
        Assert.Null(series[0].NextOccurrenceDueDate);
        Assert.Null(series[0].NextOccurrenceRule);
    }

    [Fact]
    public void Reopening_KeepsRescheduledOccurrence()
    {
        var series = CompletedSeries();
        series[1] = series[1] with { DueDate = D(2026, 12, 1) };
        Reopen(series, series[0].Id);
        Assert.Equal(2, series.Count);
        Assert.Equal(D(2026, 12, 1), series[1].DueDate);
    }

    [Fact]
    public void Reopening_KeepsOccurrenceWithNewReminder()
    {
        var series = CompletedSeries();
        series[1] = series[1] with { ReminderOffset = 900 };
        Reopen(series, series[0].Id);
        Assert.Equal(2, series.Count);
    }

    [Fact]
    public void Reopening_KeepsOccurrenceWithNewRule()
    {
        var series = CompletedSeries();
        series[1] = series[1] with { Recurrence = RecurrenceRule.Monthly };
        Reopen(series, series[0].Id);
        Assert.Equal(2, series.Count);
        Assert.Equal(RecurrenceRule.Monthly, series[1].Recurrence);
    }

    [Fact]
    public void Reopening_AfterOccurrenceDeleted_TakesRuleBack()
    {
        var series = CompletedSeries();
        series.RemoveAt(1);
        Reopen(series, series[0].Id);
        Assert.Equal(RecurrenceRule.Daily, series[0].Recurrence);
        Assert.Null(series[0].NextOccurrenceId);
    }

    [Fact]
    public void Reopening_LegacyLinkWithoutDueDate_KeepsOccurrence()
    {
        var series = CompletedSeries();
        series[0] = series[0] with { NextOccurrenceDueDate = null, NextOccurrenceRule = null };
        Reopen(series, series[0].Id);
        Assert.Equal(2, series.Count);
    }

    [Fact]
    public void Reopening_DuplicateLeavesOccurrenceAlone()
    {
        var series = CompletedSeries();
        var copy = series[0].Duplicated(Now);
        series.Add(copy);
        Reopen(series, copy.Id);
        Assert.Equal(3, series.Count);
    }

    private static readonly Project Parent = new() { Name = "Parent" };
    private static readonly Project Child = new() { Name = "Child", ParentId = Parent.Id };
    private static readonly Project Grandchild = new() { Name = "Grandchild", ParentId = Child.Id };

    [Fact]
    public void DeleteProject_MovesSubprojectsAndTasksUp()
    {
        var projects = new List<Project> { Parent, Child, Grandchild, new() { Name = "Other" } };
        var tasks = new List<TaskItem> { new() { Title = "In child", ProjectId = Child.Id } };

        TaskOperations.DeleteProject(Child.Id, projects, tasks);

        Assert.Equal(3, projects.Count);
        Assert.Equal(Parent.Id, projects.Single(p => p.Id == Grandchild.Id).ParentId);
        Assert.Equal(Parent.Id, tasks[0].ProjectId);
    }

    [Fact]
    public void DeleteProject_TopLevel_TasksLoseProject()
    {
        var projects = new List<Project> { Parent, Child };
        var tasks = new List<TaskItem> { new() { Title = "In parent", ProjectId = Parent.Id } };

        TaskOperations.DeleteProject(Parent.Id, projects, tasks);

        Assert.Null(tasks[0].ProjectId);
        Assert.Null(projects.Single().ParentId);
    }

    [Fact]
    public void Move_WithoutRoom_RenumbersThenPlaces()
    {
        var a = new TaskItem { Title = "A", SortOrder = 1, CreatedAt = D(2026, 10, 1) };
        var b = new TaskItem { Title = "B", SortOrder = 1, CreatedAt = D(2026, 10, 2) };
        var c = new TaskItem { Title = "C", SortOrder = 1, CreatedAt = D(2026, 10, 3) };
        var tasks = new List<TaskItem> { a, b, c };

        TaskOperations.Move(c.Id, b.Id, new[] { a, b, c }, tasks);

        var shown = new TaskQuery(TaskScope.All).Run(tasks, Array.Empty<Project>(), Now, Berlin);
        Assert.Equal(new[] { "A", "C", "B" }, shown.Select(t => t.Title));
        Assert.Equal(3, tasks.Select(t => t.SortOrder).Distinct().Count());
    }

    [Fact]
    public void Move_WithRoom_OnlyTouchesTheMovedTask()
    {
        var a = new TaskItem { Title = "A", SortOrder = 1 };
        var b = new TaskItem { Title = "B", SortOrder = 2 };
        var c = new TaskItem { Title = "C", SortOrder = 3 };
        var tasks = new List<TaskItem> { a, b, c };

        TaskOperations.Move(c.Id, b.Id, new[] { a, b, c }, tasks);

        Assert.Equal(new[] { 1.0, 2.0, 1.5 }, tasks.Select(t => t.SortOrder));
    }

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
