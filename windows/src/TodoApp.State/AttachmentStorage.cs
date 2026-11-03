using System.Globalization;
using TodoCore;

namespace TodoApp.State;

/// <summary>
/// Keeps copies of attached files inside the app's data folder, one folder per day, so attachments survive the original being moved
/// or deleted.
/// </summary>
public sealed class AttachmentStorage
{
    /// <summary>Windows' classic MAX_PATH is 260; staying well below it keeps every API and tool happy.</summary>
    private const int MaxPathLength = 240;

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tif", ".tiff", ".ico", ".heic",
    };

    public AttachmentStorage(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory = Path.GetFullPath(directory);
    }

    public string Directory { get; }

    /// <summary>
    /// Copies <paramref name="sourcePath"/> in as "{day}/{GUID}-{name}", the day's folder named as the Mac names it
    /// and written with "/" as the Mac writes it. The stored name is shortened, keeping the extension, so the full
    /// path stays within 240 characters; the attachment's display name is unchanged. Safe off the UI thread.
    /// </summary>
    public Attachment ImportFile(string sourcePath, DateTime now, TodoCalendar calendar)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        var day = DatedFolder.Name(now, calendar);
        var folder = Path.Combine(Directory, day);
        System.IO.Directory.CreateDirectory(folder);
        var id = Guid.NewGuid();
        var name = Path.GetFileName(sourcePath);
        var prefix = id.ToString("D").ToUpperInvariant() + "-";
        var storedName = prefix + Shortened(name, MaxPathLength - (folder.Length + 1) - prefix.Length);
        File.Copy(sourcePath, Path.Combine(folder, storedName));
        return new Attachment { Id = id, Kind = AttachmentKind.File, Name = name, Location = day + "/" + storedName, AddedAt = now };
    }

    /// <summary>Cuts the name to at most <paramref name="budget"/> characters, keeping the extension.</summary>
    private static string Shortened(string name, int budget)
    {
        if (name.Length <= budget) return name;
        var extension = Path.GetExtension(name);
        if (extension.Length >= budget) extension = string.Empty;
        var stemLength = Math.Max(1, budget - extension.Length);
        var stem = name[..Math.Min(stemLength, name.Length - extension.Length)];
        if (stem.Length > 1 && char.IsHighSurrogate(stem[^1])) stem = stem[..^1];
        return stem + extension;
    }

    /// <summary>
    /// A file's full path inside the attachments folder; null for links, and for a location that points outside the
    /// folder (the data file can be edited by hand).
    /// </summary>
    public string? PathFor(Attachment attachment)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        return attachment.Kind == AttachmentKind.File ? AttachmentFiles.PathForLocation(attachment.Location, Directory) : null;
    }

    /// <summary>
    /// What opening the attachment hands to Windows: a stored file's path inside the attachments folder, or a web or
    /// mail address. Null for anything else, such as a link to a local program in a hand-edited data file.
    /// </summary>
    public string? OpenTarget(Attachment attachment)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        return attachment.Kind == AttachmentKind.File
            ? PathFor(attachment)
            : AttachmentLink.CanOpen(attachment.Location) ? attachment.Location : null;
    }

    public bool IsImage(Attachment attachment)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        return attachment.Kind == AttachmentKind.File && ImageExtensions.Contains(Path.GetExtension(attachment.Name));
    }

    /// <summary>
    /// Deletes stored copies nothing refers to any more, apart from those in dated folders named
    /// <paramref name="keepingFoldersFrom"/> or later.
    /// </summary>
    public void RemoveFiles(IReadOnlySet<string> referencedLocations, string keepingFoldersFrom) =>
        AttachmentFiles.RemoveUnreferenced(Directory, referencedLocations, keepingFoldersFrom);

    /// <summary>
    /// Like Foundation's ByteCountFormatter with the .file style: decimal units, whole bytes and kilobytes,
    /// one decimal for megabytes and two from gigabytes up ("532 bytes", "12 KB", "1.2 MB", "1.25 GB").
    /// </summary>
    public static string FileSize(long bytes)
    {
        var culture = CultureInfo.CurrentCulture;
        if (bytes == 0) return "Zero KB";
        if (bytes == 1) return "1 byte";
        if (bytes < 1000) return string.Create(culture, $"{bytes} bytes");

        string[] units = { "KB", "MB", "GB", "TB", "PB", "EB" };
        var value = (decimal)bytes / 1000m;
        var unit = 0;
        while (unit < units.Length - 1 && Rounded(value, unit) >= 1000m)
        {
            value /= 1000m;
            unit++;
        }

        var text = Rounded(value, unit).ToString(Decimals(unit) switch { 0 => "0", 1 => "0.#", _ => "0.##" }, culture);
        return $"{text} {units[unit]}";
    }

    /// <summary>Decimal places per unit: none for KB, one for MB, two for GB and above.</summary>
    private static int Decimals(int unit) => Math.Min(unit, 2);

    private static decimal Rounded(decimal value, int unit) =>
        Math.Round(value, Decimals(unit), MidpointRounding.AwayFromZero);
}
