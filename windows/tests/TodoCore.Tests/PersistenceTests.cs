using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using TodoCore;
using static TodoCore.Tests.TestSupport;

namespace TodoCore.Tests;

public sealed class PersistenceTests : IDisposable
{
    private static readonly string FixturePath =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "mac-sample.json");

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "TodoCore.Tests-" + Guid.NewGuid().ToString("N"));

    private readonly DataFileStore _store;

    public PersistenceTests()
    {
        _store = new DataFileStore(_directory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private static AppData Sample() => AppData.Sample(Now, Berlin);

    private void WriteRaw(string text)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(_store.FilePath, text);
    }

    [Fact]
    public void Load_MissingFile_IsNull()
    {
        Assert.Null(_store.Load());
    }

    [Fact]
    public void FilePath_IsTasksJsonInsideTheDirectory()
    {
        Assert.Equal(_directory, _store.Directory);
        Assert.Equal(Path.Combine(_directory, "tasks.json"), _store.FilePath);
    }

    [Fact]
    public void RoundTrip_KeepsEveryTask()
    {
        var sample = Sample();
        _store.Save(sample);
        var loaded = _store.Load();
        Assert.NotNull(loaded);
        Assert.Equal(sample.Tasks.Select(t => t.Id), loaded.Tasks.Select(t => t.Id));
    }

    [Fact]
    public void RoundTrip_KeepsRecurrenceAndSubtasks()
    {
        _store.Save(Sample());
        var loaded = _store.Load();
        Assert.NotNull(loaded);
        Assert.Equal(2, loaded.Tasks[0].Subtasks.Count);
        Assert.Equal(RecurrenceRule.Weekly, loaded.Tasks[1].Recurrence);
    }

    [Fact]
    public void Load_MissingKeys_DecodeWithDefaults()
    {
        WriteRaw("""{"version":1,"projects":[],"tasks":[{"id":"6F2C1C1E-0E59-4C38-9D57-5B0B7C1F2A10","title":"Old"}]}""");
        var migrated = _store.Load();
        Assert.NotNull(migrated);
        var task = Assert.Single(migrated.Tasks);
        Assert.Equal(Guid.Parse("6F2C1C1E-0E59-4C38-9D57-5B0B7C1F2A10"), task.Id);
        Assert.Equal("Old", task.Title);
        Assert.Equal("", task.Notes);
        Assert.Equal(TodoCore.TaskStatus.Todo, task.Status);
        Assert.Equal(Priority.None, task.Priority);
        Assert.False(task.HasDueTime);
        Assert.Empty(task.Subtasks);
        Assert.Empty(task.Attachments);
        Assert.Equal(0, task.SortOrder);
        Assert.Null(task.DueDate);
    }

    [Fact]
    public void Load_TaskWithoutId_Throws()
    {
        WriteRaw("""{"version":1,"projects":[],"tasks":[{"title":"No id"}]}""");
        Assert.Throws<JsonException>(() => _store.Load());
    }

    [Fact]
    public void Load_CorruptFile_Throws()
    {
        WriteRaw("not json");
        Assert.ThrowsAny<Exception>(() => _store.Load());
    }

    [Fact]
    public void Quarantine_MovesTheFileAsideInsteadOfDeletingIt()
    {
        WriteRaw("not json");
        var moved = _store.QuarantineDataFile(Now);
        Assert.True(File.Exists(moved));
        Assert.False(File.Exists(_store.FilePath));
        Assert.Equal("not json", File.ReadAllText(moved));
        Assert.Matches(@"^tasks\.unreadable-\d+-[0-9A-Fa-f]{8}\.json$", Path.GetFileName(moved));
    }

    [Fact]
    public void Quarantine_TwoFailuresInTheSameSecond_DoNotCollide()
    {
        WriteRaw("not json");
        var moved = _store.QuarantineDataFile(Now);
        WriteRaw("again");
        var movedAgain = _store.QuarantineDataFile(Now);
        Assert.NotEqual(moved, movedAgain);
        Assert.True(File.Exists(moved));
        Assert.True(File.Exists(movedAgain));
    }

    [Fact]
    public void Load_MacFixture()
    {
        Directory.CreateDirectory(_directory);
        File.Copy(FixturePath, _store.FilePath);
        var data = _store.Load();
        Assert.NotNull(data);
        Assert.Equal(AppData.CurrentVersion, data.Version);
        Assert.Equal(14, data.Tasks.Count);
        Assert.Equal(3, data.Projects.Count);

        var report = data.Tasks.Single(t => t.Title == "Send weekly report");
        Assert.Equal(D(2026, 10, 23, 16), report.DueDate);
        Assert.Equal(DateTimeKind.Utc, report.DueDate!.Value.Kind);
        Assert.Equal(900, report.ReminderOffset);
        Assert.Equal(RecurrenceFrequency.Weekly, report.Recurrence!.Frequency);
        Assert.Equal(1, report.Recurrence.Interval);
        Assert.True(report.HasDueTime);
        Assert.Equal(Priority.Medium, report.Priority);

        var gym = data.Tasks.Single(t => t.Title == "Gym");
        Assert.Equal(new[] { 2, 4, 6 }, gym.Recurrence!.Weekdays);

        Assert.Equal(TodoCore.TaskStatus.Done, data.Tasks.Single(t => t.Title == "Review invoices").Status);
        Assert.Equal(TodoCore.TaskStatus.InProgress, data.Tasks.Single(t => t.Title == "Redesign portfolio site").Status);
        var link = Assert.Single(data.Tasks.Single(t => t.Title == "Read article on interface typography").Attachments);
        Assert.Equal(AttachmentKind.Link, link.Kind);
        Assert.Equal(new[] { "Work", "Personal", "Projects" }, data.Projects.Select(p => p.Name));
    }

    [Fact]
    public void RoundTrip_MacFixture_IsLossless()
    {
        Directory.CreateDirectory(_directory);
        File.Copy(FixturePath, _store.FilePath);
        var first = _store.Load();
        Assert.NotNull(first);

        _store.Save(first);
        var second = _store.Load();
        Assert.NotNull(second);

        Assert.Equal(first.Version, second.Version);
        Assert.True(first.Tasks.SequenceEqual(second.Tasks));
        Assert.True(first.Projects.SequenceEqual(second.Projects));
    }

    [Fact]
    public void Save_WritesMacShapedJson()
    {
        var projectId = Guid.Parse("0905ab33-a30f-4d94-8d6f-7be7f7b0ca90");
        var data = new AppData(AppData.CurrentVersion,
            new[] { new Project { Id = projectId, Name = "Work" } },
            new[]
            {
                new TaskItem
                {
                    Title = "Plain",
                    Priority = Priority.High,
                    ProjectId = projectId,
                    CreatedAt = Now,
                },
            });
        _store.Save(data);
        var text = File.ReadAllText(_store.FilePath);

        Assert.Contains("\"projectID\"", text);
        Assert.DoesNotContain("nextOccurrenceID", text);
        Assert.DoesNotContain("\"projectId\"", text);
        Assert.Contains("0905AB33-A30F-4D94-8D6F-7BE7F7B0CA90", text);
        Assert.DoesNotContain("0905ab33", text);
        Assert.Contains("\"priority\": 3", text);
        Assert.DoesNotContain("\"priority\": \"", text);

        using var doc = JsonDocument.Parse(text);
        var created = doc.RootElement.GetProperty("tasks")[0].GetProperty("createdAt").GetString();
        Assert.Matches(@"^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\dZ$", created);
        Assert.Equal("2026-10-23T13:30:00Z", created);
        Assert.Equal(1, doc.RootElement.GetProperty("version").GetInt32());
    }

    [Fact]
    public void Save_WritesCamelCaseEnumStringsAndNeverFractionalSeconds()
    {
        var data = new AppData(AppData.CurrentVersion,
            new[] { new Project { Name = "P", Color = ProjectColor.Graphite } },
            new[]
            {
                new TaskItem
                {
                    Status = TodoCore.TaskStatus.InProgress,
                    CreatedAt = Now.AddMilliseconds(987),
                    Recurrence = RecurrenceRule.Create(RecurrenceFrequency.Monthly, 2),
                    Attachments = ValueList.Of(new Attachment
                    {
                        Kind = AttachmentKind.File, Name = "a.txt", Location = "x", AddedAt = Now,
                    }),
                },
            });
        _store.Save(data);
        var text = File.ReadAllText(_store.FilePath);

        Assert.Contains("\"status\": \"inProgress\"", text);
        Assert.Contains("\"color\": \"graphite\"", text);
        Assert.Contains("\"kind\": \"file\"", text);
        Assert.Contains("\"frequency\": \"monthly\"", text);
        Assert.DoesNotMatch(@"\d\.\d+Z", text);
        Assert.Contains("\"createdAt\": \"2026-10-23T13:30:00Z\"", text);
    }

    [Fact]
    public void Load_UnknownStatusThrows()
    {
        WriteRaw("""{"version":1,"projects":[],"tasks":[{"id":"6F2C1C1E-0E59-4C38-9D57-5B0B7C1F2A10","status":"archived"}]}""");
        Assert.Throws<JsonException>(() => _store.Load());
    }

    [Fact]
    public void Load_DatesAreReadLeniently()
    {
        WriteRaw("""
            {"version":1,"projects":[],"tasks":[
              {"id":"6F2C1C1E-0E59-4C38-9D57-5B0B7C1F2A10","createdAt":"2026-10-23T14:00:00.250Z","dueDate":"2026-10-23T16:00:00+02:00"}
            ]}
            """);
        var task = Assert.Single(_store.Load()!.Tasks);
        Assert.Equal(DateTimeKind.Utc, task.CreatedAt.Kind);
        Assert.Equal(D(2026, 10, 23, 16), task.CreatedAt.AddMilliseconds(-250));
        Assert.Equal(D(2026, 10, 23, 16), task.DueDate);
    }

    [Fact]
    public void Save_UnderHijriCultureWritesGregorian()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ar-SA");
            var data = new AppData(AppData.CurrentVersion, Array.Empty<Project>(),
                new[] { new TaskItem { Title = "T", CreatedAt = Now, DueDate = Today, SortOrder = 1.5 } });
            _store.Save(data);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }

        using var doc = JsonDocument.Parse(File.ReadAllText(_store.FilePath));
        var task = doc.RootElement.GetProperty("tasks")[0];
        Assert.StartsWith("2026-", task.GetProperty("createdAt").GetString());
        Assert.StartsWith("2026-", task.GetProperty("dueDate").GetString());
        Assert.Equal(1.5, task.GetProperty("sortOrder").GetDouble());
    }

    [Fact]
    public void Save_IsAtomic()
    {
        _store.Save(Sample());
        var smaller = new AppData(AppData.CurrentVersion, Array.Empty<Project>(),
            new[] { new TaskItem { Title = "Only one", CreatedAt = Now } });
        _store.Save(smaller);

        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
        Assert.Equal(new[] { "tasks.json" }, Directory.GetFiles(_directory).Select(Path.GetFileName));
        var loaded = _store.Load();
        Assert.NotNull(loaded);
        Assert.Equal("Only one", Assert.Single(loaded.Tasks).Title);
    }
}
