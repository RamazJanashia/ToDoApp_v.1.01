using System.Text;
using TodoCore;
using static TodoApp.State.Tests.TestSupport;

namespace TodoApp.State.Tests;

public sealed class LoadTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly FakeClock _clock = new();
    private readonly ManualDebouncer _debouncer = new();
    private readonly FakeReminders _reminders = new();

    public void Dispose() => _temp.Dispose();

    private TaskStore NewStore() => new(_temp.Path, _clock, _debouncer, _reminders);

    private string DataFile => Path.Combine(_temp.Path, "tasks.json");

    [Fact]
    public void Load_CorruptFile_IsQuarantinedAndFreshListStarts()
    {
        var garbage = Encoding.UTF8.GetBytes("{ this is not json");
        File.WriteAllBytes(DataFile, garbage);

        var store = NewStore();

        Assert.NotNull(store.Alert?.Message);
        Assert.Contains("couldn’t be read", store.Alert?.Message);
        Assert.Contains(_temp.Path, store.Alert?.Message);
        Assert.Empty(store.Tasks);
        Assert.Empty(store.Projects);
        Assert.False(File.Exists(DataFile));

        var quarantined = Directory.GetFiles(_temp.Path, "tasks.unreadable-*.json").Single();
        Assert.Contains(Path.GetFileName(quarantined), store.Alert?.Message);
        Assert.Equal(garbage, File.ReadAllBytes(quarantined));

        // The fresh list saves normally from here on.
        store.CreateTask("First");
        store.SaveNow();
        Assert.Single(new DataFileStore(_temp.Path).Load()!.Tasks);
        Assert.Equal(garbage, File.ReadAllBytes(quarantined));
    }

    [Fact]
    public void Load_QuarantineFails_DisablesSaving()
    {
        var original = Encoding.UTF8.GetBytes("{ this is not json");
        File.WriteAllBytes(DataFile, original);

        TaskStore store;
        using (new FileStream(DataFile, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            store = NewStore();
        }

        Assert.NotNull(store.Alert?.Message);
        Assert.Contains("couldn’t be set aside", store.Alert?.Message);
        Assert.Contains(_temp.Path, store.Alert?.Message);

        // The lock is gone, so only the store's own refusal keeps the file intact.
        _reminders.Syncs.Clear();
        store.CreateTask("Edit");
        store.SaveNow();
        _debouncer.Flush();

        Assert.Equal(original, File.ReadAllBytes(DataFile));
        Assert.Empty(Directory.GetFiles(_temp.Path, "tasks.unreadable-*.json"));
        Assert.NotEmpty(_reminders.Syncs);
    }

    // Ports of the Mac's TaskStoreChecks.

    private const string OldFile = @"2020-01-01\AAAA-contract.pdf";

    private void Put(string relative, string text = "pdf")
    {
        var path = Path.Combine(_temp.Path, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private bool Exists(string relative) => File.Exists(Path.Combine(_temp.Path, relative));

    private string Today => DatedFolder.Name(_clock.UtcNow, _clock.Calendar);

    private void NextDay() => _clock.UtcNow = _clock.Calendar.AddDays(_clock.UtcNow, 1);

    private static AppData With(params TaskItem[] tasks) => new(AppData.CurrentVersion, Array.Empty<Project>(), tasks);

    private static TaskItem WithFile(string title, string location) => new()
    {
        Title = title,
        Attachments = ValueList.Of(new Attachment { Kind = AttachmentKind.File, Name = Path.GetFileName(location), Location = location }),
    };

    [Fact]
    public void CorruptFile_IsReportedAndPointsAtBackups()
    {
        Put("tasks.json", "not json");
        var store = NewStore();
        Assert.Equal("Couldn’t open your tasks", store.Alert?.Title);
        Assert.Contains("Backups", store.Alert!.Message);
    }

    [Fact]
    public void CorruptFile_KeepsAttachments()
    {
        Put(Path.Combine("Attachments", OldFile));
        Put("tasks.json", "not json");
        NewStore();
        Assert.True(Exists(Path.Combine("Attachments", OldFile)));

        var second = NewStore();
        Assert.True(Exists(Path.Combine("Attachments", OldFile)));
        second.CreateTask("New");
        second.SaveNow();

        // The next day's launch makes a backup and so reaches cleanup, but the quarantined file stops it.
        NextDay();
        NewStore();
        Assert.True(Exists(Path.Combine("Backups", Today, "tasks.json")));
        Assert.True(Exists(Path.Combine("Attachments", OldFile)));
    }

    [Fact]
    public void MissingDataFile_DoesNotWipeAttachments()
    {
        Put(Path.Combine("Attachments", OldFile));
        NewStore();
        Assert.True(Exists(Path.Combine("Attachments", OldFile)));
        Assert.True(Exists("tasks.json"));
    }

    [Fact]
    public void Cleanup_RemovesOnlyTrueOrphans()
    {
        new DataFileStore(_temp.Path).Save(With(WithFile("x", "2020-01-01/kept.pdf")));
        new DataFileStore(Path.Combine(_temp.Path, "Backups", "2021-01-01")).Save(With(WithFile("y", "2020-01-01/in-backup.pdf")));
        Put(@"Attachments\2020-01-01\kept.pdf");
        Put(@"Attachments\2020-01-01\orphan.pdf");
        Put(@"Attachments\2020-01-01\in-backup.pdf");
        Put(Path.Combine("Attachments", Today, "just-added.pdf"));

        NewStore();

        Assert.True(Exists(@"Attachments\2020-01-01\kept.pdf"));
        Assert.True(Exists(@"Attachments\2020-01-01\in-backup.pdf"));
        Assert.True(Exists(Path.Combine("Attachments", Today, "just-added.pdf")));
        Assert.False(Exists(@"Attachments\2020-01-01\orphan.pdf"));
    }

    [Fact]
    public void Cleanup_OnlyOnFirstLaunchOfTheDay()
    {
        new DataFileStore(_temp.Path).Save(With(new TaskItem { Title = "x" }));
        NewStore();
        Put(@"Attachments\2020-01-01\late.pdf");

        NewStore();
        Assert.True(Exists(@"Attachments\2020-01-01\late.pdf"));

        NextDay();
        NewStore();
        Assert.False(Exists(@"Attachments\2020-01-01\late.pdf"));
    }

    [Fact]
    public void NewerFile_IsReportedNeverOverwritten()
    {
        const string future = """{"version":99,"projects":[],"tasks":[]}""";
        Put("tasks.json", future);
        Put(Path.Combine("Attachments", OldFile));

        var store = NewStore();
        Assert.Contains("newer", store.Alert?.Message);
        store.CreateTask("Nope");
        store.SaveNow();
        _debouncer.Flush();

        Assert.Equal(future, File.ReadAllText(DataFile));
        Assert.True(Exists(Path.Combine("Attachments", OldFile)));
        Assert.Empty(Directory.GetFiles(_temp.Path, "tasks.unreadable-*"));
    }

    [Fact]
    public void SaveFailure_ReportedOnceUntilSuccess()
    {
        var store = NewStore();
        Assert.Null(store.Alert);
        var blocker = DataFile + ".tmp";

        Directory.CreateDirectory(blocker);
        store.CreateTask("Unsaved");
        store.SaveNow();
        Assert.Contains("save", store.Alert?.Title);

        store.DismissAlert();
        store.SaveNow();
        Assert.Null(store.Alert);

        Directory.Delete(blocker);
        store.SaveNow();
        Assert.Contains(new DataFileStore(_temp.Path).Load()!.Tasks, t => t.Title == "Unsaved");

        Directory.CreateDirectory(blocker);
        store.CreateTask("Unsaved again");
        store.SaveNow();
        Assert.NotNull(store.Alert);
    }

    [Fact]
    public void FirstSaveOfADay_BacksUpTheDayAsItBegan()
    {
        var store = NewStore();
        store.CreateTask("Before midnight");
        store.SaveNow();

        _clock.UtcNow = _clock.Calendar.SettingTime(_clock.Calendar.AddDays(_clock.UtcNow, 1), 0, 10);
        store.CreateTask("After midnight");
        store.SaveNow();

        var kept = new DataFileStore(Path.Combine(_temp.Path, "Backups", Today)).Load()!;
        Assert.Contains(kept.Tasks, t => t.Title == "Before midnight");
        Assert.DoesNotContain(kept.Tasks, t => t.Title == "After midnight");
    }

    [Fact]
    public void BlankDraftLeftByQuit_IsDroppedAtLaunch()
    {
        new DataFileStore(_temp.Path).Save(With(new TaskItem { Title = "" }, new TaskItem { Title = "Real" }));
        Assert.Equal(new[] { "Real" }, NewStore().Tasks.Select(t => t.Title));
    }

    [Fact]
    public void Alert_KeepsTheFirstProblem()
    {
        Put("tasks.json", "not json");
        var store = NewStore();
        var first = store.Alert;
        var raised = 0;
        store.AlertRaised += (_, _) => raised++;

        Directory.CreateDirectory(DataFile + ".tmp");
        store.CreateTask("Unsaved");
        store.SaveNow();

        Assert.Equal(first, store.Alert);
        Assert.Equal(0, raised);
    }

    [Fact]
    public void Alert_RaisedWhenNoneIsShowing()
    {
        var store = NewStore();
        var raised = 0;
        store.AlertRaised += (_, _) => raised++;

        Directory.CreateDirectory(DataFile + ".tmp");
        store.CreateTask("Unsaved");
        store.SaveNow();

        Assert.Equal(1, raised);
    }
}
