namespace TodoCore;

/// <summary>Everything the app persists, in one value.</summary>
public sealed record AppData(int Version, IReadOnlyList<Project> Projects, IReadOnlyList<TaskItem> Tasks)
{
    public const int CurrentVersion = 1;

    /// <summary>The first-launch content: a small, realistic workspace.</summary>
    public static AppData Sample(DateTime now, TodoCalendar calendar)
    {
        ArgumentNullException.ThrowIfNull(calendar);

        var today = calendar.StartOfDay(now);
        DateTime Day(int offset) => calendar.AddDays(today, offset);
        DateTime At(int offset, int hour, int minute = 0) => calendar.SettingTime(Day(offset), hour, minute);

        var work = new Project { Name = "Work", Icon = "folder", Color = ProjectColor.Blue };
        var personal = new Project { Name = "Personal", Icon = "folder", Color = ProjectColor.Green };
        var projects = new Project { Name = "Projects", Icon = "folder", Color = ProjectColor.White };

        var gym = RecurrenceRule.Create(RecurrenceFrequency.Weekly, 1, new[] { 2, 4, 6 });
        var order = 0.0;
        double Next() => ++order;

        var tasks = new List<TaskItem>
        {
            new()
            {
                Title = "Review contract",
                Notes = "Review the final agreement before sending.",
                Priority = Priority.High,
                ProjectId = work.Id,
                DueDate = today,
                Subtasks = ValueList.Of(
                    new Subtask { Title = "Check clauses" },
                    new Subtask { Title = "Confirm payment terms" }),
                SortOrder = Next(),
            },
            new()
            {
                Title = "Send weekly report",
                Priority = Priority.Medium,
                ProjectId = work.Id,
                DueDate = At(0, 16),
                HasDueTime = true,
                ReminderOffset = 900,
                Recurrence = RecurrenceRule.Weekly,
                SortOrder = Next(),
            },
            new() { Title = "Call supplier", ProjectId = personal.Id, DueDate = today, SortOrder = Next() },
            new()
            {
                Title = "Review invoices",
                Status = TaskStatus.Done,
                ProjectId = work.Id,
                DueDate = today,
                SortOrder = Next(),
                CompletedAt = now,
            },
            new()
            {
                Title = "Book dentist appointment",
                Priority = Priority.Low,
                ProjectId = personal.Id,
                DueDate = today,
                SortOrder = Next(),
            },

            new() { Title = "Buy groceries", Notes = "Oat milk, coffee beans, lemons.", SortOrder = Next() },
            new()
            {
                Title = "Read article on interface typography",
                Attachments = ValueList.Of(new Attachment
                {
                    Kind = AttachmentKind.Link,
                    Name = "developer.apple.com",
                    Location = "https://developer.apple.com/design/human-interface-guidelines/typography",
                }),
                SortOrder = Next(),
            },
            new() { Title = "Organize photo library", SortOrder = Next() },

            new()
            {
                Title = "Quarterly planning",
                Priority = Priority.High,
                ProjectId = work.Id,
                DueDate = At(2, 10),
                HasDueTime = true,
                SortOrder = Next(),
            },
            new()
            {
                Title = "Renew passport",
                Priority = Priority.Medium,
                ProjectId = personal.Id,
                DueDate = Day(5),
                SortOrder = Next(),
            },
            new() { Title = "Prepare team offsite", ProjectId = projects.Id, DueDate = Day(9), SortOrder = Next() },
            new()
            {
                Title = "Pay rent",
                ProjectId = personal.Id,
                DueDate = Day(8),
                Recurrence = RecurrenceRule.Monthly,
                SortOrder = Next(),
            },
            new()
            {
                Title = "Redesign portfolio site",
                Status = TaskStatus.InProgress,
                ProjectId = projects.Id,
                SortOrder = Next(),
            },
            new()
            {
                Title = "Gym",
                ProjectId = personal.Id,
                DueDate = gym.NextDate(today, null, calendar),
                Recurrence = gym,
                SortOrder = Next(),
            },
        };

        return new AppData(CurrentVersion, new[] { work, personal, projects }, tasks);
    }
}
