using TodoCore;
using static TodoCore.Tests.TestSupport;

namespace TodoCore.Tests;

public class SectionTests
{
    private static readonly Project Work = new() { Name = "Work" };

    private static readonly List<TaskItem> Tasks = new()
    {
        new() { Title = "Due today", Priority = Priority.Low, ProjectId = Work.Id, DueDate = Today, SortOrder = 4 },
        new() { Title = "Overdue", Priority = Priority.High, ProjectId = Work.Id, DueDate = D(2026, 10, 21), SortOrder = 5 },
        new() { Title = "Tomorrow", ProjectId = Work.Id, DueDate = D(2026, 10, 24), SortOrder = 6 },
        new() { Title = "Next week", Notes = "quarterly numbers", DueDate = D(2026, 10, 30), SortOrder = 7 },
    };

    private static readonly List<TaskItem> History = new()
    {
        new() { Title = "Open", ProjectId = Work.Id, SortOrder = 1 },
        new() { Title = "Done today", Status = TodoCore.TaskStatus.Done, ProjectId = Work.Id, SortOrder = 2, CompletedAt = Now },
        new() { Title = "Done earlier", Status = TodoCore.TaskStatus.Done, ProjectId = Work.Id, SortOrder = 3, CompletedAt = D(2026, 10, 15, 9) },
        new() { Title = "Done earlier later", Status = TodoCore.TaskStatus.Done, ProjectId = Work.Id, SortOrder = 4, CompletedAt = D(2026, 10, 15, 18) },
    };

    [Fact]
    public void Sections_ProjectOpenThenOneSectionPerDay()
    {
        var scope = TaskScope.Project(Work.Id);
        var tasks = new TaskQuery(scope, IncludesCompletedHistory: true).Run(History, new[] { Work }, Now, Berlin);
        var sections = TaskSection.Make(scope, tasks, Now, Berlin);

        Assert.Equal(new[] { false, true, true }, sections.Select(s => s.IsCompletedHistory));
        Assert.Equal("Completed · Today", sections[1].Title);
        Assert.Equal(new[] { "Done earlier later", "Done earlier" }, sections[2].Tasks.Select(t => t.Title));
    }

    [Fact]
    public void Sections_ProjectNothingFinished_IsOneSection() =>
        Assert.Equal(new[] { "all" }, TaskSection.Make(TaskScope.Project(Work.Id), new[] { History[0] }, Now, Berlin).Select(s => s.Id));

    [Fact]
    public void Sections_ProjectAllFinished_HasNoEmptyOpenSection() =>
        Assert.Equal(new[] { true }, TaskSection.Make(TaskScope.Project(Work.Id), new[] { History[1] }, Now, Berlin).Select(s => s.IsCompletedHistory));

    [Fact]
    public void Today_SplitsOverdue()
    {
        var todayTasks = new TaskQuery(TaskScope.Today).Run(Tasks, new[] { Work }, Now, Berlin);
        var sections = TaskSection.Make(TaskScope.Today, todayTasks, Now, Berlin);

        Assert.Equal(new[] { "overdue", "today" }, sections.Select(s => s.Id));
        Assert.True(sections[0].IsWarning);
        Assert.Equal("Friday, October 23", sections[1].Detail);
    }

    [Fact]
    public void Upcoming_GroupsByDay()
    {
        var upcoming = new TaskQuery(TaskScope.Upcoming).Run(Tasks, Array.Empty<Project>(), Now, Berlin);
        var sections = TaskSection.Make(TaskScope.Upcoming, upcoming, Now, Berlin);

        Assert.Equal(new[] { "Tomorrow", "Oct 30" }, sections.Select(s => s.Title));
        // A title that already reads as the date doesn't repeat it as detail.
        Assert.Equal(new string?[] { "Oct 24", null }, sections.Select(s => s.Detail));
        Assert.Equal(D(2026, 10, 24).Ticks.ToString(), sections[0].Id);
    }

    [Fact]
    public void RelativeLabels() =>
        Assert.Equal("Yesterday", DueDateText.DayLabel(D(2026, 10, 22), Now, Berlin));

    // A reminder for a task due on the 24th, firing that morning, must say "Today", not "Tomorrow"
    // as it would if worked out on the 23rd when the reminder was scheduled.
    [Fact]
    public void ReminderWording_IsRelativeToWhenItFires() =>
        Assert.Equal("Today", DueDateText.Label(D(2026, 10, 24), false, D(2026, 10, 24, 9), Berlin));

    [Fact]
    public void Label_WithTimeAppendsTime() =>
        Assert.Equal("Tomorrow, 4:00 PM", DueDateText.Label(D(2026, 10, 24, 16), true, Now, Berlin));

    [Fact]
    public void DayLabel_WithinAWeekIsWeekdayName() =>
        Assert.Equal("Monday", DueDateText.DayLabel(D(2026, 10, 26), Now, Berlin));

    [Fact]
    public void DayLabel_OtherYear_IncludesYear() =>
        Assert.Equal("Jan 5, 2027", DueDateText.DayLabel(D(2027, 1, 5), Now, Berlin));

    [Fact]
    public void Completed_GroupsNewestDayFirst()
    {
        var done = new List<TaskItem>
        {
            new() { Title = "a", Status = TodoCore.TaskStatus.Done, CompletedAt = D(2026, 10, 23, 10) },
            new() { Title = "b", Status = TodoCore.TaskStatus.Done, CompletedAt = D(2026, 10, 15) },
        };
        var sections = TaskSection.Make(TaskScope.Completed, done, Now, Berlin);
        Assert.Equal(new[] { "Today", "Oct 15" }, sections.Select(s => s.Title));
    }

    [Fact]
    public void NoDateSection_UsesNoneId()
    {
        var done = new List<TaskItem>
        {
            new() { Title = "a", Status = TodoCore.TaskStatus.Done, CompletedAt = D(2026, 10, 23, 10) },
            new() { Title = "b", Status = TodoCore.TaskStatus.Done },
        };
        var sections = TaskSection.Make(TaskScope.Completed, done, Now, Berlin);
        Assert.Equal("none", sections[^1].Id);
        Assert.Equal("No Date", sections[^1].Title);
    }
}
