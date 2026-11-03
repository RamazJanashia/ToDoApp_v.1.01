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
    public void AllOpen_OpenFromEverywhereThenTodaysCompletions() =>
        Assert.Equal(
            new[] { "Inbox open", "Due today", "Overdue", "Tomorrow", "Next week", "Inbox done today" },
            Titles(new TaskQuery(TaskScope.AllOpen)));

    private static readonly Project Parent = new() { Name = "Parent" };
    private static readonly Project Child = new() { Name = "Child", ParentId = Parent.Id };
    private static readonly Project Loner = new() { Name = "Loner" };

    [Fact]
    public void ProjectsScope_IncludesSubprojects()
    {
        var tree = new[] { Parent, Child, Loner };
        var tasks = new List<TaskItem>
        {
            new() { Title = "In child", ProjectId = Child.Id },
            new() { Title = "In parent", ProjectId = Parent.Id },
            new() { Title = "Elsewhere", ProjectId = Loner.Id },
        };
        var nested = TaskScope.Projects(tree.SubtreeIds(Parent.Id));

        Assert.Equal(
            new HashSet<string> { "In child", "In parent" },
            new TaskQuery(nested).Run(tasks, tree, Now, Berlin).Select(t => t.Title).ToHashSet());
        Assert.Equal(2, TaskQuery.OpenCount(nested, tasks, Now, Berlin));
    }

    private static readonly List<TaskItem> History = new()
    {
        new() { Title = "Open", ProjectId = Work.Id, SortOrder = 1 },
        new() { Title = "Done today", Status = TaskStatus.Done, ProjectId = Work.Id, SortOrder = 2, CompletedAt = Now },
        new() { Title = "Done earlier", Status = TaskStatus.Done, ProjectId = Work.Id, SortOrder = 3, CompletedAt = D(2026, 10, 15, 9) },
        new() { Title = "Done earlier later", Status = TaskStatus.Done, ProjectId = Work.Id, SortOrder = 4, CompletedAt = D(2026, 10, 15, 18) },
        new() { Title = "Other project", Status = TaskStatus.Done, ProjectId = Guid.NewGuid(), SortOrder = 5, CompletedAt = Now },
    };

    private static List<string> HistoryTitles(TaskQuery query) =>
        query.Run(History, new[] { Work }, Now, Berlin).Select(t => t.Title).ToList();

    [Fact]
    public void History_OffByDefault() =>
        Assert.DoesNotContain("Done earlier", HistoryTitles(new TaskQuery(TaskScope.Project(Work.Id))));

    [Fact]
    public void History_BringsInOldCompletionsForThisProjectOnly()
    {
        var titles = HistoryTitles(new TaskQuery(TaskScope.Project(Work.Id), IncludesCompletedHistory: true));
        Assert.Equal(4, titles.Count);
        Assert.DoesNotContain("Other project", titles);
    }

    [Fact]
    public void History_HiddenWhenShowCompletedOff() =>
        Assert.Equal(
            new[] { "Open" },
            HistoryTitles(new TaskQuery(TaskScope.Project(Work.Id), ShowCompleted: false, IncludesCompletedHistory: true)));

    [Fact]
    public void NextWeek_IsComingMonday()
    {
        Assert.Equal(D(2026, 10, 26), DueDateText.NextWeek(Now, Berlin));
        Assert.Equal(D(2026, 11, 2), DueDateText.NextWeek(D(2026, 10, 26, 9), Berlin));
    }

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
        Assert.DoesNotContain("Inbox done today", Titles(new TaskQuery(TaskScope.AllOpen, ShowCompleted: false)));

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
