using TodoCore;
using static TodoCore.Tests.TestSupport;

namespace TodoCore.Tests;

public class SampleTests
{
    private static readonly AppData Data = AppData.Sample(Now, Berlin);

    private static TaskItem Task(string title) => Data.Tasks.Single(t => t.Title == title);

    [Fact]
    public void Sample_MatchesMac()
    {
        Assert.Equal(AppData.CurrentVersion, Data.Version);
        Assert.Equal(new[] { "Work", "Personal", "Projects" }, Data.Projects.Select(p => p.Name));
        Assert.Equal(
            new[] { ProjectColor.Blue, ProjectColor.Green, ProjectColor.White },
            Data.Projects.Select(p => p.Color));

        Assert.Equal(
            new[]
            {
                "Review contract", "Send weekly report", "Call supplier", "Review invoices",
                "Book dentist appointment", "Buy groceries", "Read article on interface typography",
                "Organize photo library", "Quarterly planning", "Renew passport", "Prepare team offsite",
                "Pay rent", "Redesign portfolio site", "Gym",
            },
            Data.Tasks.Select(t => t.Title));
        Assert.Equal(Enumerable.Range(1, 14).Select(i => (double)i), Data.Tasks.Select(t => t.SortOrder));

        var report = Task("Send weekly report");
        Assert.Equal(Berlin.Make(2026, 10, 23, 16), report.DueDate);
        Assert.True(report.HasDueTime);
        Assert.Equal(900.0, report.ReminderOffset);
        Assert.Equal(RecurrenceRule.Weekly, report.Recurrence);

        var gym = Task("Gym");
        Assert.Equal(new[] { 2, 4, 6 }, gym.Recurrence!.Weekdays.ToArray());
        // Friday the 23rd; the next of Mon/Wed/Fri is Monday the 26th.
        Assert.Equal(D(2026, 10, 26), gym.DueDate);

        var invoices = Task("Review invoices");
        Assert.True(invoices.IsCompleted);
        Assert.Equal(Now, invoices.CompletedAt);

        var link = Assert.Single(Task("Read article on interface typography").Attachments);
        Assert.Equal(AttachmentKind.Link, link.Kind);
        Assert.Equal("https://developer.apple.com/design/human-interface-guidelines/typography", link.Location);
    }

    [Fact]
    public void Sample_ProjectsAndDates()
    {
        var work = Data.Projects[0];
        var personal = Data.Projects[1];
        var projects = Data.Projects[2];

        Assert.Equal(work.Id, Task("Review contract").ProjectId);
        Assert.Equal(Priority.High, Task("Review contract").Priority);
        Assert.Equal(new[] { "Check clauses", "Confirm payment terms" }, Task("Review contract").Subtasks.Select(s => s.Title));
        Assert.Equal(personal.Id, Task("Call supplier").ProjectId);
        Assert.Equal(Priority.Low, Task("Book dentist appointment").Priority);
        Assert.Null(Task("Buy groceries").ProjectId);
        Assert.Equal(D(2026, 10, 25, 10), Task("Quarterly planning").DueDate);
        Assert.Equal(D(2026, 10, 28), Task("Renew passport").DueDate);
        Assert.Equal(projects.Id, Task("Prepare team offsite").ProjectId);
        Assert.Equal(D(2026, 11, 1), Task("Prepare team offsite").DueDate);
        Assert.Equal(RecurrenceRule.Monthly, Task("Pay rent").Recurrence);
        Assert.Equal(D(2026, 10, 31), Task("Pay rent").DueDate);
        Assert.Equal(TaskStatus.InProgress, Task("Redesign portfolio site").Status);
    }
}
