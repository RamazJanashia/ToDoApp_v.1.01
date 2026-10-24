using TodoCore;
using TaskStatus = TodoCore.TaskStatus;
using static TodoCore.Tests.TestSupport;

namespace TodoCore.Tests;

public class QueryTests
{
    private static readonly Project Work = new() { Name = "Work" };

    private static readonly List<TaskItem> Tasks = new()
    {
        new() { Title = "Inbox open", SortOrder = 1 },
        new() { Title = "Inbox done today", Status = TaskStatus.Done, SortOrder = 2, CompletedAt = Now },
        new() { Title = "Inbox done last week", Status = TaskStatus.Done, SortOrder = 3, CompletedAt = D(2026, 10, 15) },
        new() { Title = "Due today", Priority = Priority.Low, ProjectId = Work.Id, DueDate = Today, SortOrder = 4 },
        new() { Title = "Overdue", Priority = Priority.High, ProjectId = Work.Id, DueDate = D(2026, 10, 21), SortOrder = 5 },
        new() { Title = "Tomorrow", ProjectId = Work.Id, DueDate = D(2026, 10, 24), SortOrder = 6 },
        new() { Title = "Next week", Notes = "quarterly numbers", DueDate = D(2026, 10, 30), SortOrder = 7 },
    };

    private static List<string> Titles(TaskQuery query) =>
        query.Run(Tasks, new[] { Work }, Now, Berlin).Select(t => t.Title).ToList();

    private static List<string> TitlesOf(IReadOnlyList<TaskItem> tasks, TaskQuery query) =>
        query.Run(tasks, Array.Empty<Project>(), Now, Berlin).Select(t => t.Title).ToList();

    [Fact]
    public void Inbox_OpenFirstTodaysCompletionsKeptOldOnesHidden() =>
        Assert.Equal(new[] { "Inbox open", "Next week", "Inbox done today" }, Titles(new TaskQuery(TaskScope.Inbox)));

    [Fact]
    public void Today_IncludesOverdue() =>
        Assert.Equal(new HashSet<string> { "Due today", "Overdue" }, Titles(new TaskQuery(TaskScope.Today)).ToHashSet());

    [Fact]
    public void Upcoming_IsStrictlyAfterToday() =>
        Assert.Equal(new[] { "Tomorrow", "Next week" }, Titles(new TaskQuery(TaskScope.Upcoming)));

    [Fact]
    public void Completed_NewestFirst() =>
        Assert.Equal(new[] { "Inbox done today", "Inbox done last week" }, Titles(new TaskQuery(TaskScope.Completed)));

    [Fact]
    public void ProjectScope() =>
        Assert.Equal(
            new HashSet<string> { "Due today", "Overdue", "Tomorrow" },
            Titles(new TaskQuery(TaskScope.Project(Work.Id))).ToHashSet());

    [Fact]
    public void HideCompleted() =>
        Assert.DoesNotContain("Inbox done today", Titles(new TaskQuery(TaskScope.Inbox, ShowCompleted: false)));

    [Fact]
    public void PrioritySort() =>
        Assert.Equal("Overdue", Titles(new TaskQuery(TaskScope.Project(Work.Id), Sort: TaskSort.Priority)).First());

    [Fact]
    public void DueDateSort() =>
        Assert.Equal(
            new[] { "Overdue", "Due today", "Tomorrow" },
            Titles(new TaskQuery(TaskScope.Project(Work.Id), Sort: TaskSort.DueDate)));

    [Fact]
    public void PriorityFilter() =>
        Assert.Equal(
            new[] { "Overdue" },
            Titles(new TaskQuery(TaskScope.All, Priorities: new HashSet<Priority> { Priority.High })));

    [Fact]
    public void Search_MatchesNotes() =>
        Assert.Equal(new[] { "Next week" }, Titles(new TaskQuery(TaskScope.All, "QUARTERLY")));

    [Fact]
    public void Search_MatchesProjectName() =>
        Assert.Equal(3, Titles(new TaskQuery(TaskScope.All, "work")).Count);

    [Fact]
    public void Search_NeedsEveryWord() =>
        Assert.Empty(Titles(new TaskQuery(TaskScope.All, "inbox nope")));

    [Fact]
    public void OpenCounts() =>
        Assert.Equal(2, TaskQuery.OpenCount(TaskScope.Today, Tasks, Now, Berlin));

    [Fact]
    public void Search_IgnoresCaseAndDiacritics()
    {
        var tasks = new List<TaskItem> { new() { Title = "Café résumé" }, new() { Title = "Other" } };
        Assert.Equal(new[] { "Café résumé" }, TitlesOf(tasks, new TaskQuery(TaskScope.All, "resume")));
        Assert.Equal(new[] { "Café résumé" }, TitlesOf(tasks, new TaskQuery(TaskScope.All, "CAFE")));
    }

    [Fact]
    public void TitleSort_IsNatural()
    {
        var tasks = new List<TaskItem>
        {
            new() { Title = "Task 10", SortOrder = 1 },
            new() { Title = "Task 2", SortOrder = 2 },
        };
        Assert.Equal(new[] { "Task 2", "Task 10" }, TitlesOf(tasks, new TaskQuery(TaskScope.All, Sort: TaskSort.Title)));
    }

    [Fact]
    public void Sort_IsStableForTies()
    {
        // Same sort order and creation time, so only the input order can decide.
        var created = D(2026, 10, 1);
        var tasks = new List<TaskItem>
        {
            new() { Title = "c", SortOrder = 1, CreatedAt = created },
            new() { Title = "a", SortOrder = 1, CreatedAt = created },
            new() { Title = "b", SortOrder = 1, CreatedAt = created },
        };
        foreach (var sort in new[] { TaskSort.Manual, TaskSort.DueDate, TaskSort.Priority, TaskSort.Created })
        {
            Assert.Equal(new[] { "c", "a", "b" }, TitlesOf(tasks, new TaskQuery(TaskScope.All, Sort: sort)));
        }
    }
}
