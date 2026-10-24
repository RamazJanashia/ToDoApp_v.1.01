using System.Text.Json;
using TodoCore;

namespace TodoApp.State;

public enum ViewMode { List, Board, Calendar }

public enum AppearancePreference { System, Light, Dark }

/// <summary>
/// UI preferences kept in <c>settings.json</c> (the Mac keeps the same values in UserDefaults).
/// Settings never block startup: anything missing, unreadable or unrecognised falls back to its default.
/// </summary>
public sealed record AppSettings(
    ViewMode ViewMode = ViewMode.List,
    TaskSort Sort = TaskSort.Manual,
    bool ShowCompleted = true,
    bool InspectorVisible = true,
    AppearancePreference Appearance = AppearancePreference.System,
    double SidebarWidth = 250,
    double InspectorWidth = 340)
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    /// <summary>Missing or corrupt file gives defaults; an unrecognised value only resets its own field.</summary>
    public static AppSettings Load(string path)
    {
        var defaults = new AppSettings();
        try
        {
            if (!File.Exists(path)) return defaults;
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return defaults;

            return new AppSettings(
                ReadEnum(root, "viewMode", ViewModeNames, defaults.ViewMode),
                ReadEnum(root, "sort", SortNames, defaults.Sort),
                ReadBool(root, "showCompleted", defaults.ShowCompleted),
                ReadBool(root, "inspectorVisible", defaults.InspectorVisible),
                ReadEnum(root, "appearance", AppearanceNames, defaults.Appearance),
                ReadWidth(root, "sidebarWidth", defaults.SidebarWidth),
                ReadWidth(root, "inspectorWidth", defaults.InspectorWidth));
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            AppLog.Error("Couldn't read settings; using defaults", ex);
            return defaults;
        }
    }

    /// <summary>Writes atomically. A failure is logged and swallowed: losing a preference must not crash the app.</summary>
    public void Save(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var temp = path + ".tmp";
        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
            Directory.CreateDirectory(directory);
            var values = new SortedDictionary<string, object>(StringComparer.Ordinal)
            {
                ["viewMode"] = ViewModeNames.First(pair => pair.Value == ViewMode).Key,
                ["sort"] = SortNames.First(pair => pair.Value == Sort).Key,
                ["showCompleted"] = ShowCompleted,
                ["inspectorVisible"] = InspectorVisible,
                ["appearance"] = AppearanceNames.First(pair => pair.Value == Appearance).Key,
                ["sidebarWidth"] = SidebarWidth,
                ["inspectorWidth"] = InspectorWidth,
            };
            var bytes = JsonSerializer.SerializeToUtf8Bytes(values, WriteOptions);
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            AppLog.Error("Couldn't save settings", ex);
            try { File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    // The strings are the Swift raw values, so the names are spelled out rather than derived.

    private static readonly Dictionary<string, ViewMode> ViewModeNames = new(StringComparer.Ordinal)
    {
        ["list"] = ViewMode.List,
        ["board"] = ViewMode.Board,
        ["calendar"] = ViewMode.Calendar,
    };

    private static readonly Dictionary<string, TaskSort> SortNames = new(StringComparer.Ordinal)
    {
        ["manual"] = TaskSort.Manual,
        ["dueDate"] = TaskSort.DueDate,
        ["priority"] = TaskSort.Priority,
        ["title"] = TaskSort.Title,
        ["created"] = TaskSort.Created,
    };

    private static readonly Dictionary<string, AppearancePreference> AppearanceNames = new(StringComparer.Ordinal)
    {
        ["system"] = AppearancePreference.System,
        ["light"] = AppearancePreference.Light,
        ["dark"] = AppearancePreference.Dark,
    };

    private static T ReadEnum<T>(JsonElement root, string key, Dictionary<string, T> names, T fallback)
    {
        if (!root.TryGetProperty(key, out var element) || element.ValueKind != JsonValueKind.String) return fallback;
        return names.TryGetValue(element.GetString()!, out var value) ? value : fallback;
    }

    private static bool ReadBool(JsonElement root, string key, bool fallback) =>
        root.TryGetProperty(key, out var element) && element.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? element.GetBoolean()
            : fallback;

    private static double ReadWidth(JsonElement root, string key, double fallback) =>
        root.TryGetProperty(key, out var element) && element.ValueKind == JsonValueKind.Number
            && element.TryGetDouble(out var value) && double.IsFinite(value) && value > 0
            ? value
            : fallback;
}
