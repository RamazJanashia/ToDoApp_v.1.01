using System.Globalization;

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

    /// <summary>Null when nothing has been saved yet. Throws when the file exists but can't be read.</summary>
    public AppData? Load()
    {
        if (!File.Exists(FilePath)) return null;
        return JsonModel.Deserialize(File.ReadAllBytes(FilePath));
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
