using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using TodoApp.State;
using TodoCore;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace TodoApp;

/// <summary>
/// What the Mac gets from NSWorkspace and Quick Look: opening a file or link, revealing a file in File Explorer, the
/// kind of file ("PDF document") and a thumbnail for the attachment rows. Every call copes with a file that has gone.
/// </summary>
public static class WindowsShell
{
    private static readonly ConcurrentDictionary<string, BitmapImage> ImageThumbnails = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The main window's handle, which pickers need to show themselves over it. Set once by the window.</summary>
    public static IntPtr WindowHandle { get; set; }

    /// <summary>
    /// Opens a local file, or a web or mail address, with whatever Windows associates with it. A link can come from
    /// a drop or a hand-edited tasks.json, so any other scheme (ms-msdt:, file://, a network share…) is refused.
    /// Failures are logged without the full path or address.
    /// </summary>
    public static Task OpenAsync(string pathOrUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pathOrUrl);
        string target;
        if (Uri.TryCreate(pathOrUrl, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "mailto")
        {
            target = uri.AbsoluteUri;
        }
        else if (LocalFile(pathOrUrl) is { } local)
        {
            target = local;
        }
        else
        {
            AppLog.Error("Refused to open an attachment that is neither a local file nor a web or mail address", null);
            return Task.CompletedTask;
        }

        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is Win32Exception or FileNotFoundException or InvalidOperationException)
        {
            AppLog.Error($"Couldn't open {Describe(target)}", ex);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// The full path when <paramref name="path"/> names a file on a local drive ("C:\…"), otherwise null. Normalising
    /// first means "//server/share", "\\?\UNC\…", "..\" tricks and the like end up as what they really are, and
    /// anything that isn't a drive-letter path is refused.
    /// </summary>
    internal static string? LocalFile(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            return full.Length >= 3 && char.IsAsciiLetter(full[0]) && full[1] == ':' && full[2] == '\\' ? full : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or System.Security.SecurityException)
        {
            return null;
        }
    }

    /// <summary>What is safe to put in the log: a file's name, or an address's scheme and host.</summary>
    private static string Describe(string target) =>
        Uri.TryCreate(target, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "mailto"
            ? uri.Scheme + "://" + uri.Host
            : Path.GetFileName(target);

    /// <summary>Opens File Explorer with the file selected.</summary>
    public static void RevealInExplorer(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe") { Arguments = $"/select,\"{path}\"", UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            AppLog.Error($"Couldn't show {Path.GetFileName(path)} in File Explorer", ex);
        }
    }

    /// <summary>The kind Explorer shows ("PDF document"); "File" when the file can't be read.</summary>
    public static async Task<string> FileTypeDescriptionAsync(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            return string.IsNullOrWhiteSpace(file.DisplayType) ? "File" : file.DisplayType;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Runtime.InteropServices.COMException)
        {
            return "File";
        }
    }

    /// <summary>
    /// A picture for an attachment row: the image itself, scaled down, for pictures; Explorer's thumbnail or icon for
    /// other files. Null for links and for files that can't be read.
    /// </summary>
    public static async Task<ImageSource?> ThumbnailAsync(Attachment attachment, string path)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (attachment.Kind != AttachmentKind.File) return null;

        try
        {
            if (ImageThumbnails.TryGetValue(attachment.Location, out var cached)) return cached;
            var file = await StorageFile.GetFileFromPathAsync(path);
            var image = new BitmapImage();
            if (file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                image.DecodePixelWidth = 60;
                using var stream = await file.OpenReadAsync();
                await image.SetSourceAsync(stream);
                ImageThumbnails[attachment.Location] = image;
                return image;
            }

            using var thumbnail = await file.GetThumbnailAsync(ThumbnailMode.SingleItem, 32);
            if (thumbnail is null) return null;
            await image.SetSourceAsync(thumbnail);
            return image;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }
}
