using System.Globalization;
using TodoCore;

namespace TodoApp.State;

/// <summary>
/// Keeps copies of attached files inside the app's data folder, so attachments survive the original being moved
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
    /// Copies <paramref name="sourcePath"/> in as "{GUID}-{name}". The stored name is shortened, keeping the
    /// extension, so the full path stays within 240 characters; the attachment's display name is unchanged.
    /// </summary>
    public Attachment ImportFile(string sourcePath, DateTime now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        System.IO.Directory.CreateDirectory(Directory);
        var id = Guid.NewGuid();
        var name = Path.GetFileName(sourcePath);
        var prefix = id.ToString("D").ToUpperInvariant() + "-";
        var storedName = prefix + Shortened(name, MaxPathLength - (Directory.Length + 1) - prefix.Length);
        File.Copy(sourcePath, Path.Combine(Directory, storedName));
        return new Attachment { Id = id, Kind = AttachmentKind.File, Name = name, Location = storedName, AddedAt = now };
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

    /// <summary>A file's full path inside the attachments folder; null for links.</summary>
    public string? PathFor(Attachment attachment)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        return attachment.Kind == AttachmentKind.File ? Path.Combine(Directory, attachment.Location) : null;
    }

    public bool IsImage(Attachment attachment)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        return attachment.Kind == AttachmentKind.File && ImageExtensions.Contains(Path.GetExtension(attachment.Name));
    }

    /// <summary>
    /// Deletes stored copies no task refers to any more. Runs at launch, which keeps undo of a deleted task or
    /// attachment safe for the whole session.
    /// </summary>
    public void RemoveFiles(IReadOnlySet<string> referencedLocations)
    {
        ArgumentNullException.ThrowIfNull(referencedLocations);
        string[] entries;
        try
        {
            entries = System.IO.Directory.GetFileSystemEntries(Directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (var entry in entries)
        {
            var name = Path.GetFileName(entry);
            if (referencedLocations.Contains(name) || name.StartsWith('.')) continue;
            try
            {
                if (System.IO.Directory.Exists(entry)) System.IO.Directory.Delete(entry, recursive: true);
                else File.Delete(entry);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Error($"Couldn't remove unused attachment {name}", ex);
            }
        }
    }

    /// <summary>Accepts "example.com/page" as well as full URLs.</summary>
    public static Attachment? Link(string text, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(text);
        var trimmed = text.Trim();
        if (trimmed.Length == 0 || trimmed.Any(char.IsWhiteSpace)) return null;
        var candidate = trimmed.Contains("://", StringComparison.Ordinal) ? trimmed : "https://" + trimmed;
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri)) return null;
        var host = uri.Host;
        if (!host.Contains('.') && host != "localhost") return null;
        var name = host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;
        return new Attachment { Kind = AttachmentKind.Link, Name = name, Location = candidate, AddedAt = now };
    }

    /// <summary>
    /// Like Foundation's ByteCountFormatter with the .file style: decimal units, whole bytes and kilobytes,
    /// one decimal place from megabytes up ("532 bytes", "12 KB", "1.2 MB").
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

        var text = Rounded(value, unit).ToString(unit == 0 ? "0" : "0.#", culture);
        return $"{text} {units[unit]}";
    }

    private static decimal Rounded(decimal value, int unit) =>
        Math.Round(value, unit == 0 ? 0 : 1, MidpointRounding.AwayFromZero);
}
