using TodoCore;
using static TodoCore.Tests.TestSupport;

namespace TodoCore.Tests;

public sealed class DataFolderTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "TodoCore.Tests-" + Guid.NewGuid().ToString("N"));

    private readonly DataFileStore _store;

    public DataFolderTests()
    {
        _store = new DataFileStore(_directory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private string Attachments => Path.Combine(_directory, "Attachments");

    private void Put(string relative, string text = "x")
    {
        var path = Path.Combine(_directory, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private bool Present(string relative) =>
        File.Exists(Path.Combine(_directory, relative)) || Directory.Exists(Path.Combine(_directory, relative));

    private static AppData WithTitle(string title) =>
        new(AppData.CurrentVersion, Array.Empty<Project>(), new[] { new TaskItem { Title = title, CreatedAt = Now } });

    private static AppData WithAttachments(params string[] fileLocations) =>
        new(AppData.CurrentVersion, Array.Empty<Project>(), new[]
        {
            new TaskItem
            {
                Title = "x",
                Attachments = new ValueList<Attachment>(fileLocations
                    .Select(location => new Attachment { Kind = AttachmentKind.File, Name = "f", Location = location })
                    .Append(new Attachment { Kind = AttachmentKind.Link, Name = "a.b", Location = "https://a.b" })),
            },
        });

    // Dated folders

    [Fact]
    public void DatedFolder_NameIsLocalDay() =>
        Assert.Equal("2026-03-07", DatedFolder.Name(D(2026, 3, 7, 23, 59), Berlin));

    [Fact]
    public void DatedFolder_RecognisesOnlyDatedNames()
    {
        Assert.True(DatedFolder.IsDatedName("2026-03-07"));
        Assert.False(DatedFolder.IsDatedName("notes"));
        Assert.False(DatedFolder.IsDatedName("2026-3-7"));
        Assert.False(DatedFolder.IsDatedName("ABCD-EF-GH"));
        Assert.False(DatedFolder.IsDatedName("２０２６-03-07"));
    }

    // Backups

    [Fact]
    public void Backup_NothingYet_IsNull() => Assert.Null(_store.BackUpDataFile(Now, Berlin));

    [Fact]
    public void Backup_LandsInDatedFolder_OncePerDay()
    {
        _store.Save(WithTitle("First"));
        var backup = _store.BackUpDataFile(Now, Berlin);
        Assert.NotNull(backup);
        Assert.EndsWith(Path.Combine("Backups", "2026-10-23", "tasks.json"), backup);

        _store.Save(WithTitle("Second"));
        Assert.Null(_store.BackUpDataFile(Now, Berlin));
        Assert.Equal("First", new DataFileStore(Path.Combine(_directory, "Backups", "2026-10-23")).Load()!.Tasks[0].Title);
        Assert.Empty(Directory.GetFiles(Path.Combine(_directory, "Backups", "2026-10-23"), "*.tmp"));
    }

    [Fact]
    public void Backup_NextDayGetsItsOwn()
    {
        _store.Save(WithTitle("First"));
        _store.BackUpDataFile(Now, Berlin);
        Assert.NotNull(_store.BackUpDataFile(D(2026, 10, 24, 8), Berlin));
    }

    [Fact]
    public void PruneBackups_KeepsNewest()
    {
        var first = D(2026, 9, 1);
        for (var day = 0; day < 32; day++) Put(Path.Combine("Backups", DatedFolder.Name(Berlin.AddDays(first, day), Berlin), "tasks.json"));
        Put(Path.Combine("Backups", "notes", "keep.txt"));

        _store.PruneBackups(30);

        var left = Directory.GetDirectories(_store.BackupsDirectory).Select(Path.GetFileName).ToList();
        Assert.Equal(31, left.Count);
        Assert.Contains("notes", left);
        Assert.DoesNotContain("2026-09-01", left);
        Assert.DoesNotContain("2026-09-02", left);
        Assert.Contains("2026-09-03", left);
    }

    // Removing unused attachments

    private void PutAttachments()
    {
        Put(@"Attachments\old-kept.pdf");
        Put(@"Attachments\old-unused.pdf");
        Put(@"Attachments\2026-10-23\kept.pdf");
        Put(@"Attachments\2026-10-23\unused.pdf");
        Put(@"Attachments\2026-10-22\gone.pdf");
        Put(@"Attachments\2026-10-21\.hidden");
    }

    [Fact]
    public void RemoveUnreferenced_KeepsUsedRemovesUnused()
    {
        PutAttachments();

        AttachmentFiles.RemoveUnreferenced(Attachments, new HashSet<string> { "old-kept.pdf", "2026-10-23/kept.pdf" }, null);

        Assert.True(Present(@"Attachments\old-kept.pdf"));
        Assert.True(Present(@"Attachments\2026-10-23\kept.pdf"));
        Assert.False(Present(@"Attachments\old-unused.pdf"));
        Assert.False(Present(@"Attachments\2026-10-23\unused.pdf"));
        Assert.False(Present(@"Attachments\2026-10-22"));
        Assert.True(Present(@"Attachments\2026-10-21\.hidden"));
    }

    [Fact]
    public void RemoveUnreferenced_MatchesWindowsSpellings()
    {
        PutAttachments();

        AttachmentFiles.RemoveUnreferenced(Attachments, new HashSet<string> { "OLD-KEPT.pdf", @"2026-10-23\Kept.PDF" }, null);

        Assert.True(Present(@"Attachments\old-kept.pdf"));
        Assert.True(Present(@"Attachments\2026-10-23\kept.pdf"));
    }

    [Fact]
    public void RemoveUnreferenced_GracePeriodFolderUntouched()
    {
        PutAttachments();

        AttachmentFiles.RemoveUnreferenced(Attachments, new HashSet<string>(), "2026-10-23");

        Assert.True(Present(@"Attachments\2026-10-23\unused.pdf"));
        Assert.False(Present(@"Attachments\2026-10-22"));
    }

    [Fact]
    public void RemoveUnreferenced_MissingFolderDoesNothing() =>
        AttachmentFiles.RemoveUnreferenced(Attachments, new HashSet<string>(), null);

    // What the data file and backups still need

    [Fact]
    public void Referenced_NullWithoutDataFile() => Assert.Null(_store.ReferencedAttachmentPaths());

    [Fact]
    public void Referenced_LiveFileCountsLinksDont()
    {
        _store.Save(WithAttachments("2026-01-01/live.pdf"));
        Assert.Equal(new[] { "2026-01-01/live.pdf" }, _store.ReferencedAttachmentPaths()!.ToArray());
    }

    [Fact]
    public void Referenced_IncludesBackups()
    {
        _store.Save(WithAttachments("2026-01-01/live.pdf"));
        new DataFileStore(Path.Combine(_store.BackupsDirectory, "2026-01-02")).Save(WithAttachments("2026-01-02/old.pdf"));

        var referenced = _store.ReferencedAttachmentPaths()!;
        Assert.Contains("2026-01-01/live.pdf", referenced);
        Assert.Contains("2026-01-02/old.pdf", referenced);
    }

    [Fact]
    public void Referenced_NullWhenBackupDamaged()
    {
        _store.Save(WithAttachments("2026-01-01/live.pdf"));
        Put(@"Backups\2026-01-02\tasks.json", "{ not json");
        Assert.Null(_store.ReferencedAttachmentPaths());
    }

    [Fact]
    public void Referenced_NullWhenQuarantinedFilePresent()
    {
        _store.Save(WithAttachments("2026-01-01/live.pdf"));
        Put("tasks.unreadable-1-ABCD.json", "{}");
        Assert.Null(_store.ReferencedAttachmentPaths());
    }

    // A file from a newer version

    [Fact]
    public void Load_NewerVersion_ThrowsNewerDataFile()
    {
        Put("tasks.json", """{"version":99,"projects":[],"tasks":[{"id":"0A240A86-0CE0-4A2F-A825-C63683229382","status":"archived"}]}""");
        var error = Assert.Throws<NewerDataFileException>(() => _store.Load());
        Assert.Equal(99, error.Version);
    }

    [Fact]
    public void Load_CurrentVersion_StillLoads()
    {
        _store.Save(WithTitle("Fine"));
        Assert.Equal("Fine", _store.Load()!.Tasks[0].Title);
    }

    // Links

    private static string? LinkLocation(string text) => AttachmentLink.FromText(text, Now)?.Location;

    [Fact]
    public void Link_Accepts()
    {
        Assert.Equal("https://example.com/page", LinkLocation("example.com/page"));
        Assert.Equal("https://example.com:8080/x", LinkLocation("example.com:8080/x"));
        Assert.Equal("http://example.com", LinkLocation("http://example.com"));
        Assert.Equal("https://www.example.com", LinkLocation("https://www.example.com"));
        Assert.Equal("http://localhost:3000", LinkLocation("http://localhost:3000"));
        Assert.Equal("mailto:a@b.c", LinkLocation("mailto:a@b.c"));
        Assert.Equal("a@b.c", AttachmentLink.FromText("mailto:a@b.c", Now)!.Name);
        Assert.Equal("example.com", AttachmentLink.FromText("https://www.example.com", Now)!.Name);
        Assert.Equal(AttachmentKind.Link, AttachmentLink.FromText("example.com", Now)!.Kind);
    }

    [Theory]
    [InlineData("file://example.com/etc/passwd")]
    [InlineData("file:///C:/Windows/notepad.exe")]
    [InlineData("ssh://evil.example.com")]
    [InlineData("smb://evil.example.com/share")]
    [InlineData("vnc://a.b")]
    [InlineData("myapp://do.thing/x")]
    [InlineData("FILE://example.com/x")]
    [InlineData("user:pw@evil.example.com")]
    [InlineData("https://user:pw@example.com")]
    [InlineData("javascript:alert(1)")]
    [InlineData("two words.com")]
    [InlineData("")]
    [InlineData("mailto:nobody")]
    public void Link_Refuses(string text) => Assert.Null(AttachmentLink.FromText(text, Now));

    [Fact]
    public void CanOpen_OnlyWebAndMail()
    {
        Assert.True(AttachmentLink.CanOpen("https://a.b"));
        Assert.True(AttachmentLink.CanOpen("HTTP://a.b"));
        Assert.True(AttachmentLink.CanOpen("mailto:a@b.c"));
        Assert.False(AttachmentLink.CanOpen("file:///C:/Windows/notepad.exe"));
        Assert.False(AttachmentLink.CanOpen("smb://a.b/c"));
        Assert.False(AttachmentLink.CanOpen("not a url"));
        Assert.False(AttachmentLink.CanOpen(@"C:\Windows\notepad.exe"));
    }

    // Attachment paths

    [Fact]
    public void PathForLocation_StaysInside()
    {
        var inside = Path.Combine(Path.GetFullPath(Attachments), "2026-11-02", "a.pdf");
        Assert.Equal(inside, AttachmentFiles.PathForLocation("2026-11-02/a.pdf", Attachments));
        Assert.Equal(inside, AttachmentFiles.PathForLocation(@"2026-11-02\a.pdf", Attachments));
        Assert.NotNull(AttachmentFiles.PathForLocation("a.pdf", Attachments));
        Assert.NotNull(AttachmentFiles.PathForLocation("2026-11-02/a..b.pdf", Attachments));
    }

    [Theory]
    [InlineData(@"..\..\x.exe")]
    [InlineData("../../../../../../Windows/notepad.exe")]
    [InlineData("2026-11-02/../../tasks.json")]
    [InlineData(@"C:\Windows\notepad.exe")]
    [InlineData(@"\\server\share\x")]
    [InlineData("/etc/passwd")]
    [InlineData("")]
    [InlineData(".")]
    public void PathForLocation_RefusesOutside(string location) =>
        Assert.Null(AttachmentFiles.PathForLocation(location, Attachments));
}
