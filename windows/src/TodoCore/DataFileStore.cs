using System.Globalization;
using System.Text.Json;

namespace TodoCore;

/// <summary>Reads and writes <see cref="AppData"/> as the same single JSON file the Mac app uses.</summary>
public sealed class DataFileStore
{
    private const string FileName = "tasks.json";

    public DataFileStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory = directory;
    }

    public string Directory { get; }

    public string FilePath => Path.Combine(Directory, FileName);

    /// <summary><c>%LOCALAPPDATA%\TodoApp</c></summary>
    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TodoApp");

    public string BackupsDirectory => Path.Combine(Directory, "Backups");

    /// <summary>
    /// Null when nothing has been saved yet. Throws <see cref="NewerDataFileException"/> for a file from a newer
    /// version of the app, and something else when the file exists but can't be read.
    /// </summary>
    public AppData? Load()
    {
        if (!File.Exists(FilePath)) return null;
        return Decode(FilePath);
    }

    private static AppData Decode(string path)
    {
        var bytes = File.ReadAllBytes(path);
        // Checked first: a newer file may hold values this version can't decode at all.
        if (VersionOf(bytes) is { } version && version > AppData.CurrentVersion) throw new NewerDataFileException(version);
        return JsonModel.Deserialize(bytes);
    }

    private static int? VersionOf(byte[] bytes)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("version", out var element)
                && element.ValueKind == JsonValueKind.Number
                && element.TryGetInt32(out var version)
                ? version
                : null;
        }
        catch (JsonException)
        {
            // Unreadable: the full decode reports it.
            return null;
        }
    }

    /// <summary>
    /// Keeps a copy of the data file in today's backup folder, once a day. Call it only after the file has loaded,
    /// so a backup is never a broken file. Returns the copy, or null when there was nothing to do.
    /// </summary>
    public string? BackUpDataFile(DateTime now, TodoCalendar calendar)
    {
        if (!File.Exists(FilePath)) return null;
        var folder = Path.Combine(BackupsDirectory, DatedFolder.Name(now, calendar));
        var copy = Path.Combine(folder, FileName);
        if (File.Exists(copy)) return null;
        System.IO.Directory.CreateDirectory(folder);
        // Copied aside and moved in, so a crash can't leave a half-written backup that blocks the day's real one.
        var temp = copy + ".tmp";
        try
        {
            File.Copy(FilePath, temp, overwrite: true);
            File.Move(temp, copy);
        }
        catch
        {
            try { File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }

        return copy;
    }

    /// <summary>Removes all but the newest <paramref name="keep"/> dated backups. Anything else in the folder is left alone.</summary>
    public void PruneBackups(int keep)
    {
        string[] folders;
        try
        {
            if (!System.IO.Directory.Exists(BackupsDirectory)) return;
            folders = System.IO.Directory.GetDirectories(BackupsDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        var dated = folders.Where(f => DatedFolder.IsDatedName(Path.GetFileName(f))).Order(StringComparer.Ordinal).ToList();
        foreach (var folder in dated.Take(Math.Max(0, dated.Count - keep)))
        {
            try { System.IO.Directory.Delete(folder, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>
    /// Every stored attachment the data file or a backup refers to, so restoring a backup always finds its files.
    /// Null means that can't be known (no readable data file, a quarantined file, a damaged backup), and nothing may
    /// be cleaned up.
    /// </summary>
    public IReadOnlySet<string>? ReferencedAttachmentPaths()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            if (System.IO.Directory.GetFiles(Directory, "tasks.unreadable-*").Length > 0) return null;

            var files = new List<string> { FilePath };
            if (System.IO.Directory.Exists(BackupsDirectory))
            {
                files.AddRange(System.IO.Directory.GetDirectories(BackupsDirectory)
                    .Where(folder => DatedFolder.IsDatedName(Path.GetFileName(folder)))
                    .Select(folder => Path.Combine(folder, FileName))
                    .Where(File.Exists));
            }

            var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in files)
            {
                foreach (var attachment in Decode(file).Tasks.SelectMany(t => t.Attachments))
                {
                    if (attachment.Kind == AttachmentKind.File) referenced.Add(AttachmentFiles.Normalized(attachment.Location));
                }
            }

            return referenced;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NewerDataFileException)
        {
            return null;
        }
    }

    /// <summary>Writes atomically, so a crash mid-save never leaves a half-written file.</summary>
    public void Save(AppData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        System.IO.Directory.CreateDirectory(Directory);
        var bytes = JsonModel.Serialize(data);
        var temp = FilePath + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temp, FilePath, overwrite: true);
        }
        catch
        {
            try { File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    /// <summary>Moves an unreadable data file aside so it is never overwritten, and returns its new location.</summary>
    public string QuarantineDataFile(DateTime now)
    {
        // The random suffix keeps two failures in the same second from colliding.
        var seconds = new DateTimeOffset(now).ToUnixTimeSeconds();
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var destination = Path.Combine(
            Directory,
            string.Create(CultureInfo.InvariantCulture, $"tasks.unreadable-{seconds}-{suffix}.json"));
        File.Move(FilePath, destination);
        return destination;
    }
}
