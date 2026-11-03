using TodoCore;
using static TodoApp.State.Tests.TestSupport;

namespace TodoApp.State.Tests;

public sealed class AttachmentTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly FakeClock _clock = new();

    public void Dispose() => _temp.Dispose();

    private AttachmentStorage NewStorage() => new(Path.Combine(_temp.Path, "Attachments"));

    private TaskStore NewStore() => new(Path.Combine(_temp.Path, "data"), _clock, new ManualDebouncer(), new FakeReminders());

    private string Source(string name, string content = "x")
    {
        var folder = Path.Combine(_temp.Path, "sources");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Link_AcceptsBareDomain()
    {
        var link = AttachmentLink.FromText("  example.com/page ", Now);

        Assert.NotNull(link);
        Assert.Equal(AttachmentKind.Link, link.Kind);
        Assert.Equal("https://example.com/page", link.Location);
        Assert.Equal("example.com", link.Name);
        Assert.Equal(Now, link.AddedAt);
    }

    [Fact]
    public void Link_StripsWwwAndKeepsExplicitScheme()
    {
        var link = AttachmentLink.FromText("http://www.example.org/a?b=1", Now);

        Assert.NotNull(link);
        Assert.Equal("example.org", link.Name);
        Assert.Equal("http://www.example.org/a?b=1", link.Location);
        Assert.Equal("localhost", AttachmentLink.FromText("localhost:3000", Now)!.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("two words.com")]
    [InlineData("localdomain")]
    [InlineData("https://intranet/page")]
    public void Link_RejectsSpacesAndHostWithoutDot(string text)
    {
        Assert.Null(AttachmentLink.FromText(text, Now));
    }

    [Fact]
    public void Import_CopiesFileUnderGuidPrefixedNameInTodaysFolder()
    {
        var storage = NewStorage();
        var source = Source("report.pdf", "hello");

        var attachment = storage.ImportFile(source, Now, Berlin);

        Assert.Equal(AttachmentKind.File, attachment.Kind);
        Assert.Equal("report.pdf", attachment.Name);
        Assert.Equal($"2026-10-23/{attachment.Id.ToString("D").ToUpperInvariant()}-report.pdf", attachment.Location);
        Assert.Equal(
            Path.Combine(_temp.Path, "Attachments", "2026-10-23", $"{attachment.Id.ToString("D").ToUpperInvariant()}-report.pdf"),
            storage.PathFor(attachment));
        Assert.Equal(Now, attachment.AddedAt);
        Assert.Equal("hello", File.ReadAllText(storage.PathFor(attachment)!));
        Assert.True(File.Exists(source));
    }

    [Fact]
    public void Import_SameNameTwice_KeepsBoth()
    {
        var storage = NewStorage();
        var source = Source("same.txt");

        var a = storage.ImportFile(source, Now, Berlin);
        var b = storage.ImportFile(source, Now, Berlin);

        Assert.NotEqual(a.Location, b.Location);
        Assert.True(File.Exists(storage.PathFor(a)));
        Assert.True(File.Exists(storage.PathFor(b)));
    }

    [Fact]
    public void Import_VeryLongName_StaysUnderPathLimit()
    {
        var storage = NewStorage();
        var name = new string('a', 200) + ".docx";
        var source = Source(name);

        var attachment = storage.ImportFile(source, Now, Berlin);

        var stored = storage.PathFor(attachment)!;
        Assert.True(stored.Length <= 240, $"path was {stored.Length} chars");
        Assert.EndsWith(".docx", stored);
        Assert.Equal(name, attachment.Name);
        Assert.True(File.Exists(stored));
    }

    [Fact]
    public void PathFor_LinkIsNull_FileIsInsideDirectory()
    {
        var storage = NewStorage();

        Assert.Null(storage.PathFor(AttachmentLink.FromText("example.com", Now)!));
        var file = new Attachment { Kind = AttachmentKind.File, Name = "a.txt", Location = "ID-a.txt" };
        Assert.Equal(Path.Combine(_temp.Path, "Attachments", "ID-a.txt"), storage.PathFor(file));
    }

    [Theory]
    [InlineData(@"..\..\tasks.json")]
    [InlineData("2026-11-02/../../tasks.json")]
    [InlineData(@"C:\Windows\notepad.exe")]
    public void PathFor_RefusesLocationsOutsideTheFolder(string location)
    {
        var hostile = new Attachment { Kind = AttachmentKind.File, Name = "x", Location = location };
        Assert.Null(NewStorage().PathFor(hostile));
    }

    [Theory]
    [InlineData(@"C:\Windows\System32\notepad.exe")]
    [InlineData(@"..\..\x.exe")]
    [InlineData("file:///C:/x")]
    [InlineData("javascript:alert(1)")]
    [InlineData("ms-msdt:/id x")]
    public void OpenTarget_LinkThatIsNotWebOrMail_OpensNothing(string location)
    {
        var link = new Attachment { Kind = AttachmentKind.Link, Name = "x", Location = location };
        Assert.Null(NewStorage().OpenTarget(link));
    }

    [Fact]
    public void OpenTarget_WebAndMailLinksAndStoredFiles()
    {
        var storage = NewStorage();
        Assert.Equal("https://a.b", storage.OpenTarget(new Attachment { Kind = AttachmentKind.Link, Name = "a.b", Location = "https://a.b" }));
        Assert.Equal("mailto:a@b.c", storage.OpenTarget(new Attachment { Kind = AttachmentKind.Link, Name = "a@b.c", Location = "mailto:a@b.c" }));
        var file = new Attachment { Kind = AttachmentKind.File, Name = "a.pdf", Location = "2026-11-02/ID-a.pdf" };
        Assert.Equal(storage.PathFor(file), storage.OpenTarget(file));
        Assert.Null(storage.OpenTarget(new Attachment { Kind = AttachmentKind.File, Name = "x", Location = @"..\..\x.exe" }));
    }

    [Fact]
    public void PathFor_MacStyleDatedLocationResolvesInside()
    {
        var file = new Attachment { Kind = AttachmentKind.File, Name = "a.pdf", Location = "2026-11-02/ID-a.pdf" };
        Assert.Equal(Path.Combine(_temp.Path, "Attachments", "2026-11-02", "ID-a.pdf"), NewStorage().PathFor(file));
    }

    [Theory]
    [InlineData("photo.PNG", true)]
    [InlineData("a.jpg", true)]
    [InlineData("a.jpeg", true)]
    [InlineData("a.gif", true)]
    [InlineData("a.bmp", true)]
    [InlineData("a.webp", true)]
    [InlineData("a.tif", true)]
    [InlineData("a.tiff", true)]
    [InlineData("a.ico", true)]
    [InlineData("a.heic", true)]
    [InlineData("a.pdf", false)]
    [InlineData("noextension", false)]
    public void IsImage_ByExtension(string name, bool expected)
    {
        var attachment = new Attachment { Kind = AttachmentKind.File, Name = name, Location = "ID-" + name };

        Assert.Equal(expected, NewStorage().IsImage(attachment));
    }

    [Fact]
    public void IsImage_LinkIsNeverAnImage()
    {
        var link = new Attachment { Kind = AttachmentKind.Link, Name = "x.png", Location = "https://x.png" };

        Assert.False(NewStorage().IsImage(link));
    }

    [Fact]
    public void RemoveFiles_KeepsReferencedAndDotFiles()
    {
        var storage = NewStorage();
        var kept = storage.ImportFile(Source("keep.txt"), Now, Berlin);
        var dropped = storage.ImportFile(Source("drop.txt"), Now, Berlin);
        var hidden = Path.Combine(_temp.Path, "Attachments", ".hidden");
        File.WriteAllText(hidden, "x");

        storage.RemoveFiles(new HashSet<string> { kept.Location }, "9999-12-31");

        Assert.True(File.Exists(storage.PathFor(kept)));
        Assert.False(File.Exists(storage.PathFor(dropped)));
        Assert.True(File.Exists(hidden));
    }

    [Fact]
    public void RemoveFiles_MissingDirectoryIsFine()
    {
        NewStorage().RemoveFiles(new HashSet<string>(), DatedFolder.Name(Now, Berlin));
    }

    [Theory]
    [InlineData(0, "Zero KB")]
    [InlineData(1, "1 byte")]
    [InlineData(532, "532 bytes")]
    [InlineData(999, "999 bytes")]
    [InlineData(1000, "1 KB")]
    [InlineData(1499, "1 KB")]
    [InlineData(1500, "2 KB")]
    [InlineData(12345, "12 KB")]
    [InlineData(999499, "999 KB")]
    [InlineData(999999, "1 MB")]
    [InlineData(1_200_000, "1.2 MB")]
    [InlineData(1_250_000, "1.3 MB")]
    [InlineData(2_000_000, "2 MB")]
    [InlineData(12_500_000, "12.5 MB")]
    [InlineData(1_250_000_000, "1.25 GB")]
    [InlineData(1_500_000_000, "1.5 GB")]
    public void FileSize_Formats(long bytes, string expected)
    {
        Assert.Equal(expected, AttachmentStorage.FileSize(bytes));
    }

    [Fact]
    public async Task AttachFiles_AddsAttachmentWithUndo()
    {
        var store = NewStore();
        var task = store.CreateTask("T");

        await store.AttachFilesAsync(new[] { Source("a.txt"), Source("b.txt") }, task.Id);

        var attachments = store.FindTask(task.Id)!.Attachments;
        Assert.Equal(new[] { "a.txt", "b.txt" }, attachments.Select(a => a.Name));
        Assert.All(attachments, a => Assert.True(File.Exists(store.Attachments.PathFor(a))));
        Assert.Equal("Add Attachment", store.Undo.UndoActionName);

        store.Undo.Undo();
        Assert.Empty(store.FindTask(task.Id)!.Attachments);
    }

    [Fact]
    public async Task AttachFiles_CopiesIntoDatedFolder()
    {
        var store = NewStore();
        var task = store.CreateTask("T");

        await store.AttachFilesAsync(new[] { Source("report.pdf") }, task.Id);

        var attachment = store.FindTask(task.Id)!.Attachments.Single();
        Assert.Matches(@"^2026-10-23/[0-9A-F-]{36}-report\.pdf$", attachment.Location);
        Assert.True(File.Exists(store.Attachments.PathFor(attachment)));
        Assert.Null(store.Alert);
    }

    [Fact]
    public async Task AttachFiles_FailureReportedAddsNothing()
    {
        var store = NewStore();
        var task = store.CreateTask("T");
        var missing = Path.Combine(_temp.Path, "missing.pdf");

        var ex = await Record.ExceptionAsync(() => store.AttachFilesAsync(new[] { missing }, task.Id));

        Assert.Null(ex);
        Assert.Empty(store.FindTask(task.Id)!.Attachments);
        Assert.Equal("Couldn’t attach a file", store.Alert?.Title);
        Assert.Contains("missing.pdf", store.Alert!.Message);
    }

    [Fact]
    public async Task AttachFiles_OneBadSourceDoesNotStopTheOthers()
    {
        var store = NewStore();
        var task = store.CreateTask("T");
        var missing = Path.Combine(_temp.Path, "does-not-exist.txt");

        await store.AttachFilesAsync(new[] { missing, Source("ok.txt") }, task.Id);

        Assert.Equal("ok.txt", store.FindTask(task.Id)!.Attachments.Single().Name);
        Assert.Contains("does-not-exist.txt", store.Alert?.Message);
    }

    [Fact]
    public void AttachLink_AcceptsMailtoRefusesJavascript()
    {
        var store = NewStore();
        var task = store.CreateTask("T");

        Assert.True(store.AttachLink("mailto:a@b.c", task.Id));
        Assert.False(store.AttachLink("javascript:alert(1)", task.Id));
        Assert.False(store.AttachLink("file:///C:/Windows/notepad.exe", task.Id));
        Assert.Equal("a@b.c", store.FindTask(task.Id)!.Attachments.Single().Name);
    }

    [Fact]
    public void AttachLink_AddsOrRejects()
    {
        var store = NewStore();
        var task = store.CreateTask("T");

        Assert.False(store.AttachLink("not a link", task.Id));
        Assert.Empty(store.FindTask(task.Id)!.Attachments);

        Assert.True(store.AttachLink("example.com", task.Id));
        var link = store.FindTask(task.Id)!.Attachments.Single();
        Assert.Equal(AttachmentKind.Link, link.Kind);
        Assert.Equal("https://example.com", link.Location);

        store.Undo.Undo();
        Assert.Empty(store.FindTask(task.Id)!.Attachments);
    }

    [Fact]
    public void RemoveAttachment_RemovesOnlyThatOneAndIsUndoable()
    {
        var store = NewStore();
        var task = store.CreateTask("T");
        store.AttachLink("example.com", task.Id);
        store.AttachLink("example.org", task.Id);
        var first = store.FindTask(task.Id)!.Attachments[0];

        store.RemoveAttachment(first.Id, task.Id);

        Assert.Equal("example.org", store.FindTask(task.Id)!.Attachments.Single().Name);
        store.Undo.Undo();
        Assert.Equal(2, store.FindTask(task.Id)!.Attachments.Count);
    }
}
