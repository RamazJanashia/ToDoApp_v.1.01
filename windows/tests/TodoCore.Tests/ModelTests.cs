using TodoCore;
using static TodoCore.Tests.TestSupport;
using TaskStatus = TodoCore.TaskStatus;

namespace TodoCore.Tests;

public class ModelTests
{
    private static readonly TaskItem Half = new()
    {
        Title = "Half",
        Subtasks = ValueList.Of(new Subtask { Title = "a", IsDone = true }, new Subtask { Title = "b" }),
    };

    [Fact]
    public void Progress_FollowsSubtasks()
    {
        Assert.Equal(0.5, Half.Progress);
        Assert.Equal(0, new TaskItem { Title = "x" }.Progress);
        Assert.Equal(1, new TaskItem { Title = "x", Status = TaskStatus.Done, Subtasks = ValueList.Of(new Subtask { Title = "a" }) }.Progress);
    }

    [Fact]
    public void ProgressOf_AveragesTasks()
    {
        Assert.Equal(0.75, TaskItem.ProgressOf(new[] { Half, new TaskItem { Title = "y", Status = TaskStatus.Done } }));
        Assert.Equal(0, TaskItem.ProgressOf(Array.Empty<TaskItem>()));
    }

    [Fact]
    public void IsBlank_IgnoresWhitespace()
    {
        Assert.True(new TaskItem { Title = "  \n", Notes = "\t " }.IsBlank);
        Assert.False(new TaskItem { Title = " ", Subtasks = ValueList.Of(new Subtask { Title = "" }) }.IsBlank);
        Assert.False(new TaskItem { Notes = "n" }.IsBlank);
    }

    [Fact]
    public void DisplayTitle_UntitledWhenEmpty()
    {
        Assert.Equal("Untitled", new TaskItem().DisplayTitle);
        Assert.Equal("x", new TaskItem { Title = "x" }.DisplayTitle);
    }

    [Fact]
    public void Duplicated_HasOwnIdentityAndNoOccurrenceLink()
    {
        var original = new TaskItem
        {
            Title = "Linked",
            CreatedAt = D(2026, 10, 1),
            NextOccurrenceId = Guid.NewGuid(),
            NextOccurrenceDueDate = Today,
            NextOccurrenceRule = RecurrenceRule.Daily,
            Subtasks = ValueList.Of(new Subtask { Title = "a", IsDone = true }),
            Attachments = ValueList.Of(new Attachment { Kind = AttachmentKind.File, Name = "f", Location = "2026-10-01/f", AddedAt = D(2026, 10, 1) }),
        };

        var copy = original.Duplicated(Now);

        Assert.NotEqual(original.Id, copy.Id);
        Assert.Equal(Now, copy.CreatedAt);
        Assert.Null(copy.NextOccurrenceId);
        Assert.Null(copy.NextOccurrenceDueDate);
        Assert.Null(copy.NextOccurrenceRule);
        var subtask = Assert.Single(copy.Subtasks);
        Assert.True(subtask.IsDone);
        Assert.NotEqual(original.Subtasks[0].Id, subtask.Id);
        var attachment = Assert.Single(copy.Attachments);
        Assert.NotEqual(original.Attachments[0].Id, attachment.Id);
        Assert.Equal((AttachmentKind.File, "f", "2026-10-01/f", Now), (attachment.Kind, attachment.Name, attachment.Location, attachment.AddedAt));
    }

    [Fact]
    public void NextOccurrence_ClearsOccurrenceLink()
    {
        var task = new TaskItem
        {
            Title = "Daily",
            DueDate = Today,
            Recurrence = RecurrenceRule.Daily,
            NextOccurrenceId = Guid.NewGuid(),
            NextOccurrenceDueDate = Today,
            NextOccurrenceRule = RecurrenceRule.Daily,
        };

        var next = task.NextOccurrence(Now, Berlin)!;

        Assert.Null(next.NextOccurrenceId);
        Assert.Null(next.NextOccurrenceDueDate);
        Assert.Null(next.NextOccurrenceRule);
    }

    [Fact]
    public void RecurrenceCreate_ClampsInterval()
    {
        Assert.Equal(1, RecurrenceRule.Create(RecurrenceFrequency.Daily, 0).Interval);
        Assert.Equal(999, RecurrenceRule.Create(RecurrenceFrequency.Daily, 5000).Interval);
        Assert.Equal(999, RecurrenceRule.MaximumInterval);
    }
}
