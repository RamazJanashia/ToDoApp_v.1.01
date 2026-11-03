using TodoCore;
using static TodoApp.State.Tests.TestSupport;
using TaskStatus = TodoCore.TaskStatus;

namespace TodoApp.State.Tests;

public sealed class InspectorModelTests : IDisposable
{
    private readonly PresentationFixture _f = new();

    public void Dispose() => _f.Dispose();

    private InspectorModel Build(TaskItem task)
    {
        _f.State.SelectedTaskId = task.Id;
        return InspectorModel.Build(_f.State)!;
    }

    [Fact]
    public void NoSelection_GivesNull()
    {
        _f.Store.CreateTask("A");
        _f.State.SelectedTaskId = null;

        Assert.Null(InspectorModel.Build(_f.State));
    }

    [Fact]
    public void PlainTask_ShowsPlaceholders()
    {
        var model = Build(_f.Store.CreateTask("Write report"));

        Assert.Equal("Write report", model.Title);
        Assert.False(model.IsCompleted);
        Assert.Equal(new PropertyRowModel("calendar", "Secondary", null, "Add due date", true), model.Due);
        Assert.Equal(new PropertyRowModel("bell", "Secondary", null, "Add reminder", true), model.Reminder);
        Assert.Equal(new PropertyRowModel("flag", "Secondary", null, "Add priority", true), model.Priority);
        Assert.Equal(new PropertyRowModel("tray", "Secondary", null, "No Project", false), model.Project);
        Assert.Equal(new PropertyRowModel("repeat", "Secondary", null, "Doesn’t repeat", true), model.Repeat);
        Assert.Equal(new PropertyRowModel("circle", "Secondary", null, "To Do", false), model.Status);
        Assert.Equal("", model.Notes);
        Assert.Empty(model.Subtasks);
        Assert.Null(model.SubtaskProgress);
        Assert.Empty(model.Attachments);
    }

    [Fact]
    public void CreatedText_IsAbbreviatedDate()
    {
        var model = Build(_f.Store.CreateTask("A"));

        Assert.Equal("Created Oct 23, 2026", model.CreatedText);
    }

    [Fact]
    public void DueDate_ShowsTheLabelAndTurnsRedWhenOverdue()
    {
        var upcoming = Build(_f.Store.CreateTask("A", null, _f.Day(1)));
        Assert.Equal(new PropertyRowModel("calendar", "Secondary", null, "Tomorrow", false), upcoming.Due);

        var overdue = Build(_f.Store.CreateTask("B", null, _f.Day(-1)));
        Assert.Equal(new PropertyRowModel("calendar", "Red", null, "Yesterday", false), overdue.Due);

        var timed = _f.Store.CreateTask("C", null, _f.Day(1));
        _f.Store.UpdateTask(timed.Id, null, t => t with { DueDate = D(2026, 10, 24, 16), HasDueTime = true });
        Assert.Equal("Tomorrow, 4:00 PM", Build(timed).Due.Text);
    }

    [Fact]
    public void Reminder_NeedsADueDate()
    {
        var task = _f.Store.CreateTask("A", null, _f.Day(1));
        _f.Store.UpdateTask(task.Id, null, t => t with { ReminderOffset = 900 });
        Assert.Equal(new PropertyRowModel("bell.fill", "Secondary", null, "15 minutes before", false), Build(task).Reminder);

        // A reminder without a due date can't fire, so the row reads as empty.
        _f.Store.UpdateTask(task.Id, null, t => t with { DueDate = null });
        var model = Build(task);
        Assert.Equal("Add reminder", model.Reminder.Text);
        Assert.True(model.Reminder.IsPlaceholder);
    }

    [Fact]
    public void Priority_UsesTheTintToken()
    {
        var task = _f.Store.CreateTask("A");
        void Set(Priority p) => _f.Store.UpdateTask(task.Id, null, t => t with { Priority = p });

        Set(Priority.High);
        Assert.Equal(new PropertyRowModel("flag", "Red", null, "High", false), Build(task).Priority);
        Set(Priority.Medium);
        Assert.Equal(new PropertyRowModel("flag", "PriorityMedium", null, "Medium", false), Build(task).Priority);
        Set(Priority.Low);
        Assert.Equal(new PropertyRowModel("flag", "PriorityLow", null, "Low", false), Build(task).Priority);
        Set(Priority.None);
        Assert.Equal(new PropertyRowModel("flag", "Secondary", null, "Add priority", true), Build(task).Priority);
    }

    [Fact]
    public void Project_ShowsItsIconColourAndName()
    {
        var project = _f.Store.CreateProject("Home", "house", ProjectColor.Purple);
        var model = Build(_f.Store.CreateTask("A", project.Id));

        Assert.Equal(new PropertyRowModel("house", "Project", ProjectColor.Purple, "Home", false), model.Project);
    }

    [Fact]
    public void Repeat_ShowsTheSummary()
    {
        var task = _f.Store.CreateTask("A", null, _f.Day(1));
        _f.Store.UpdateTask(task.Id, null, t => t with { Recurrence = RecurrenceRule.EveryWeekday });

        Assert.Equal(new PropertyRowModel("repeat", "Secondary", null, "Every weekday", false), Build(task).Repeat);
    }

    [Fact]
    public void Status_DoneIsGreen()
    {
        var task = _f.Store.CreateTask("A", status: TaskStatus.InProgress);
        Assert.Equal(new PropertyRowModel("circle.lefthalf.filled", "Secondary", null, "In Progress", false), Build(task).Status);

        _f.Store.SetStatus(new[] { task.Id }, TaskStatus.Done);
        var model = Build(task);
        Assert.Equal(new PropertyRowModel("checkmark.circle", "Green", null, "Done", false), model.Status);
        Assert.True(model.IsCompleted);
    }

    [Fact]
    public void NotesSubtasksAndAttachments_AreCarriedOver()
    {
        var task = _f.Store.CreateTask("A");
        var link = new Attachment { Kind = AttachmentKind.Link, Name = "a.com", Location = "https://a.com" };
        _f.Store.UpdateTask(task.Id, null, t => t with
        {
            Notes = "Remember",
            Subtasks = new ValueList<Subtask>(new[] { new Subtask { Title = "x", IsDone = true }, new Subtask { Title = "y" } }),
            Attachments = new ValueList<Attachment>(new[] { link }),
        });

        var model = Build(task);

        Assert.Equal("Remember", model.Notes);
        Assert.Equal(new[] { "x", "y" }, model.Subtasks.Select(s => s.Title).ToArray());
        Assert.Equal("1/2", model.SubtaskProgress);
        Assert.Equal(link, Assert.Single(model.Attachments));
    }

    [Fact]
    public void InspectorModel_TextIsNotPushedWhileEditing()
    {
        Assert.False(TextSync.ShouldPush("abc", "ab", hasFocus: true));
        Assert.True(TextSync.ShouldPush("abc", "ab", hasFocus: false));
        Assert.False(TextSync.ShouldPush("abc", "abc", hasFocus: false));
    }
}
