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

        Assert.NotNull(store.LoadIssue);
        Assert.Contains("couldn’t be read", store.LoadIssue);
        Assert.Contains(_temp.Path, store.LoadIssue);
        Assert.Empty(store.Tasks);
        Assert.Empty(store.Projects);
        Assert.False(File.Exists(DataFile));

        var quarantined = Directory.GetFiles(_temp.Path, "tasks.unreadable-*.json").Single();
        Assert.Contains(Path.GetFileName(quarantined), store.LoadIssue);
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

        Assert.NotNull(store.LoadIssue);
        Assert.Contains("couldn’t be set aside", store.LoadIssue);
        Assert.Contains(_temp.Path, store.LoadIssue);

        // The lock is gone, so only the store's own refusal keeps the file intact.
        _reminders.Syncs.Clear();
        store.CreateTask("Edit");
        store.SaveNow();
        _debouncer.Flush();

        Assert.Equal(original, File.ReadAllBytes(DataFile));
        Assert.Empty(Directory.GetFiles(_temp.Path, "tasks.unreadable-*.json"));
        Assert.NotEmpty(_reminders.Syncs);
    }

    [Fact]
    public void Load_RemovesUnreferencedAttachmentFiles_OnlyAfterGoodLoad()
    {
        var source = Path.Combine(_temp.Path, "note.txt");
        File.WriteAllText(source, "kept");
        var first = NewStore();
        var task = first.CreateTask("With file");
        first.AttachFiles(new[] { source }, task.Id);
        first.SaveNow();
        var kept = first.Attachments.PathFor(first.FindTask(task.Id)!.Attachments.Single())!;
        var orphan = Path.Combine(_temp.Path, "Attachments", "ORPHAN-old.txt");
        File.WriteAllText(orphan, "stale");
        Assert.True(File.Exists(kept));

        // An unreadable file may still refer to everything, so nothing is deleted.
        var good = File.ReadAllBytes(DataFile);
        File.WriteAllText(DataFile, "{ broken");
        var recovered = new TaskStore(_temp.Path, _clock, new ManualDebouncer(), new FakeReminders());
        Assert.NotNull(recovered.LoadIssue);
        Assert.True(File.Exists(orphan));
        Assert.True(File.Exists(kept));

        File.WriteAllBytes(DataFile, good);
        var reloaded = new TaskStore(_temp.Path, _clock, new ManualDebouncer(), new FakeReminders());

        Assert.Null(reloaded.LoadIssue);
        Assert.False(File.Exists(orphan));
        Assert.True(File.Exists(kept));
    }
}
