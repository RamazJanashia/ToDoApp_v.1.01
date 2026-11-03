using System.Globalization;

namespace TodoCore;

// The app keeps everything in one folder, so copying that folder backs up everything:
//
//     TodoApp\
//       tasks.json                     the live data
//       Backups\2026-11-02\tasks.json  the data as it stood when that day began
//       Attachments\2026-11-02\...     files attached on that day

public static class DatedFolder
{
    /// <summary>"2026-11-02", the day in the user's own time zone.</summary>
    public static string Name(DateTime utc, TodoCalendar calendar)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        return calendar.ToLocal(utc).ToString("yyyy'-'MM'-'dd", CultureInfo.InvariantCulture);
    }

    public static bool IsDatedName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var parts = name.Split('-');
        return parts.Length == 3
            && parts[0].Length == 4 && parts[1].Length == 2 && parts[2].Length == 2
            && parts.All(part => part.All(char.IsAsciiDigit));
    }
}

/// <summary>Thrown when the data file was written by a newer version of the app.</summary>
public sealed class NewerDataFileException : Exception
{
    public NewerDataFileException(int version)
        : base(string.Create(CultureInfo.InvariantCulture, $"tasks.json has version {version}, newer than this app understands."))
    {
        Version = version;
    }

    public NewerDataFileException()
    {
    }

    public NewerDataFileException(string message)
        : base(message)
    {
    }

    public NewerDataFileException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public int Version { get; }
}

public static class AttachmentFiles
{
    /// <summary>
    /// Where a stored file lives, or null when <paramref name="location"/> points outside
    /// <paramref name="directory"/>. The location comes from the data file, which can be edited by hand or synced
    /// from a Mac (which writes "/" between folder and file). A rooted location is refused rather than re-rooted.
    /// </summary>
    public static string? PathForLocation(string location, string directory)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (location.Length == 0) return null;
        try
        {
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
            var full = Path.GetFullPath(Path.Combine(root, location));
            var prefix = root + Path.DirectorySeparatorChar;
            return full.Length > prefix.Length && full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? full : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>
    /// Deletes stored copies no task refers to. <paramref name="referenced"/> holds locations relative to
    /// <paramref name="directory"/>. Looks one level into dated folders and removes those left empty. Dated folders
    /// named <paramref name="keepingFoldersFrom"/> or later are left alone, so recent attachments get a grace period.
    /// </summary>
    public static void RemoveUnreferenced(string directory, IReadOnlySet<string> referenced, string? keepingFoldersFrom)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(referenced);
        var keep = new HashSet<string>(referenced.Select(Normalized), StringComparer.OrdinalIgnoreCase);

        foreach (var entry in Entries(directory))
        {
            var name = Path.GetFileName(entry);
            if (name.StartsWith('.')) continue;
            if (!Directory.Exists(entry) || !DatedFolder.IsDatedName(name))
            {
                if (!keep.Contains(name)) Remove(entry);
                continue;
            }

            // Dated names sort as dates.
            if (keepingFoldersFrom is not null && string.CompareOrdinal(name, keepingFoldersFrom) >= 0) continue;
            foreach (var inner in Entries(entry))
            {
                var file = Path.GetFileName(inner);
                if (!file.StartsWith('.') && !keep.Contains(name + "/" + file)) Remove(inner);
            }

            if (Entries(entry).Length == 0) Remove(entry);
        }
    }

    /// <summary>"2026-11-02\a.pdf" and "2026-11-02/a.pdf" name the same file.</summary>
    internal static string Normalized(string location) => location.Replace('\\', '/');

    private static string[] Entries(string directory)
    {
        try
        {
            return Directory.Exists(directory) ? Directory.GetFileSystemEntries(directory) : Array.Empty<string>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    private static void Remove(string entry)
    {
        try
        {
            if (Directory.Exists(entry)) Directory.Delete(entry, recursive: true);
            else File.Delete(entry);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left for the next launch to try again.
        }
    }
}

/// <summary>
/// Which links may be attached and opened. Opening a link hands it to whatever app claims its scheme, so anything
/// other than the web and mail is refused, both on the way in and on the way out (the data file can be edited by hand).
/// </summary>
public static class AttachmentLink
{
    public static bool CanOpen(string location)
    {
        ArgumentNullException.ThrowIfNull(location);
        return Uri.TryCreate(location, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "mailto";
    }

    /// <summary>Accepts "example.com/page" and "mailto:a@b.c" as well as full web addresses.</summary>
    public static Attachment? FromText(string text, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(text);
        var trimmed = text.Trim();
        if (trimmed.Length == 0 || trimmed.Any(char.IsWhiteSpace)) return null;

        if (trimmed.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
        {
            var address = trimmed["mailto:".Length..];
            var query = address.IndexOf('?', StringComparison.Ordinal);
            if (query >= 0) address = address[..query];
            if (!address.Contains('@', StringComparison.Ordinal) || !Uri.TryCreate(trimmed, UriKind.Absolute, out _)) return null;
            return new Attachment { Kind = AttachmentKind.Link, Name = address, Location = trimmed, AddedAt = now };
        }

        // The typed text is kept as the location: Uri.AbsoluteUri would add a trailing "/" the Mac doesn't.
        var candidate = trimmed.Contains("://", StringComparison.Ordinal) ? trimmed : "https://" + trimmed;
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0) return null;
        var host = uri.Host;
        if (!host.Contains('.', StringComparison.Ordinal) && host != "localhost") return null;
        var name = host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;
        return new Attachment { Kind = AttachmentKind.Link, Name = name, Location = candidate, AddedAt = now };
    }
}
