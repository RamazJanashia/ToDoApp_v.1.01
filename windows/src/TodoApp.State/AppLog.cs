using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace TodoApp.State;

/// <summary>A small append-only log file. Logging never throws.</summary>
public static class AppLog
{
    private const string FileName = "TodoApp.log";
    private const long MaxBytes = 1024 * 1024;
    private const int KeepBytes = 512 * 1024;

    private static readonly object Gate = new();
    private static string? _path;

    public static void Configure(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        lock (Gate) _path = Path.Combine(directory, FileName);
    }

    public static void Error(string message, Exception? ex = null)
    {
        var text = string.Create(CultureInfo.InvariantCulture, $"{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ} ERROR {message}");
        if (ex is not null) text += $": {ex.GetType().Name}: {ex.Message}";
        text += Environment.NewLine;
        if (ex is not null && ex.StackTrace is { } stack) text += stack + Environment.NewLine;

        lock (Gate)
        {
            Debug.Write(text);
            if (_path is null) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                Trim(_path);
                File.AppendAllText(_path, text, Encoding.UTF8);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>When the file is over 1 MB, keeps its last 512 KB, starting at a line boundary.</summary>
    private static void Trim(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length <= MaxBytes) return;

        var bytes = File.ReadAllBytes(path);
        var start = bytes.Length - KeepBytes;
        var newline = Array.IndexOf(bytes, (byte)'\n', start);
        if (newline >= 0) start = newline + 1;
        File.WriteAllBytes(path, bytes[start..]);
    }
}
