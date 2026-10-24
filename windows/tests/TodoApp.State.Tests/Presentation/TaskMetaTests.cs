using TodoCore;
using static TodoApp.State.Tests.TestSupport;

namespace TodoApp.State.Tests;

public sealed class TaskMetaTests
{
    private static TaskMetaInfo Meta(TaskItem task, Project? project = null, bool hidesDueToday = false) =>
        TaskMeta.Build(task, project, hidesDueToday, Now, Berlin);


    private static readonly DateTime Today = Berlin.StartOfDay(Now);

    [Fact]
    public void PlainTask_HasNothing()
    {
        var meta = Meta(new TaskItem { Title = "T" });

        Assert.False(meta.HasAnything);
        Assert.Null(meta.ProjectName);
        Assert.Null(meta.DueText);
        Assert.Null(meta.RepeatSummary);
        Assert.False(meta.HasReminder);
        Assert.Null(meta.SubtaskProgress);
        Assert.Equal(0, meta.AttachmentCount);
        Assert.False(meta.ShowsNotesIcon);
    }

    [Fact]
    public void DueDate_UsesTheMacLabels()
    {
        Assert.Equal("Today", Meta(new TaskItem { Title = "T", DueDate = Today }).DueText);
        Assert.Equal("Tomorrow", Meta(new TaskItem { Title = "T", DueDate = Berlin.AddDays(Today, 1) }).DueText);
        Assert.Equal("Tomorrow, 4:00 PM", Meta(new TaskItem { Title = "T", DueDate = D(2026, 10, 24, 16), HasDueTime = true }).DueText);
    }

    [Fact]
    public void TodayScope_HidesAnAllDayDueDate()
    {
        var meta = Meta(new TaskItem { Title = "T", DueDate = Today }, hidesDueToday: true);

        Assert.Null(meta.DueText);
        Assert.False(meta.DueIsOverdue);
        Assert.False(meta.HasAnything);
    }

    [Fact]
    public void TodayScope_ShowsOnlyTheTimeForATimedTask()
    {
        var meta = Meta(new TaskItem { Title = "T", DueDate = D(2026, 10, 23, 16), HasDueTime = true }, hidesDueToday: true);

        Assert.Equal("4:00 PM", meta.DueText);
        Assert.False(meta.DueIsOverdue);
    }

    [Fact]
    public void TodayScope_StillLabelsOtherDays()
    {
        var meta = Meta(new TaskItem { Title = "T", DueDate = Berlin.AddDays(Today, -1) }, hidesDueToday: true);

        Assert.Equal("Yesterday", meta.DueText);
        Assert.True(meta.DueIsOverdue);
    }

    [Fact]
    public void TodayScope_TimedTaskThatPassedIsOverdue()
    {
        var meta = Meta(new TaskItem { Title = "T", DueDate = D(2026, 10, 23, 14), HasDueTime = true }, hidesDueToday: true);

        Assert.Equal("2:00 PM", meta.DueText);
        Assert.True(meta.DueIsOverdue);
    }

    [Fact]
    public void Overdue_IsFlagged()
    {
        var overdue = Meta(new TaskItem { Title = "T", DueDate = Berlin.AddDays(Today, -3) });
        var fine = Meta(new TaskItem { Title = "T", DueDate = Berlin.AddDays(Today, 3) });
        var done = Meta(new TaskItem { Title = "T", DueDate = Berlin.AddDays(Today, -3), Status = TodoCore.TaskStatus.Done });

        Assert.True(overdue.DueIsOverdue);
        Assert.False(fine.DueIsOverdue);
        Assert.False(done.DueIsOverdue);
    }

    [Fact]
    public void NotesIcon_OnlyWithoutProjectAndDueDate()
    {
        var project = new Project { Name = "Home" };

        Assert.True(Meta(new TaskItem { Title = "T", Notes = "n" }).ShowsNotesIcon);
        Assert.True(Meta(new TaskItem { Title = "T", Notes = "n" }).HasAnything);
        Assert.False(Meta(new TaskItem { Title = "T", Notes = "n", ProjectId = project.Id }, project).ShowsNotesIcon);
        Assert.False(Meta(new TaskItem { Title = "T", Notes = "n", DueDate = Berlin.AddDays(Today, 2) }).ShowsNotesIcon);
        Assert.False(Meta(new TaskItem { Title = "T" }).ShowsNotesIcon);
    }

    [Fact]
    public void NotesIcon_ReturnsWhenTheTodayScopeHidesTheOnlyDueDate()
    {
        // The Mac decides on the label it will show, not on the date, so the icon takes the label's place.
        var meta = Meta(new TaskItem { Title = "T", Notes = "n", DueDate = Today }, hidesDueToday: true);

        Assert.True(meta.ShowsNotesIcon);
        Assert.True(meta.HasAnything);
    }

    [Fact]
    public void Project_ShowsItsName()
    {
        var project = new Project { Name = "Home" };

        var meta = Meta(new TaskItem { Title = "T", ProjectId = project.Id }, project);

        Assert.Equal("Home", meta.ProjectName);
        Assert.True(meta.HasAnything);
    }

    [Fact]
    public void Subtasks_ShowDoneOverTotal()
    {
        var task = new TaskItem
        {
            Title = "T",
            Subtasks = new ValueList<Subtask>(new[]
            {
                new Subtask { Title = "a", IsDone = true },
                new Subtask { Title = "b" },
            }),
        };

        var meta = Meta(task);

        Assert.Equal("1/2", meta.SubtaskProgress);
        Assert.True(meta.HasAnything);
    }

    [Fact]
    public void Attachments_AreCounted()
    {
        var link = new Attachment { Kind = AttachmentKind.Link, Name = "a.com", Location = "https://a.com" };
        var file = new Attachment { Kind = AttachmentKind.File, Name = "b.pdf", Location = "b.pdf" };

        var meta = Meta(new TaskItem { Title = "T", Attachments = new ValueList<Attachment>(new[] { link, file }) });

        Assert.Equal(2, meta.AttachmentCount);
        Assert.True(meta.HasAnything);
    }

    [Fact]
    public void Repeat_ShowsTheSummary()
    {
        var meta = Meta(new TaskItem { Title = "T", Recurrence = RecurrenceRule.Weekly });

        Assert.Equal("Weekly", meta.RepeatSummary);
        Assert.True(meta.HasAnything);
    }

    [Fact]
    public void Reminder_NeedsADueDate()
    {
        Assert.True(Meta(new TaskItem { Title = "T", DueDate = Berlin.AddDays(Today, 1), ReminderOffset = 900 }).HasReminder);
        Assert.False(Meta(new TaskItem { Title = "T", ReminderOffset = 900 }).HasReminder);
        Assert.False(Meta(new TaskItem { Title = "T", DueDate = Berlin.AddDays(Today, 1) }).HasReminder);
    }
}
