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

    /// <summary>The open place, as <see cref="SidebarItems.StorageKey"/> writes it; null for the default (Today).</summary>
    public string? SidebarSelection { get; init; }

    public Guid? SelectedTask { get; init; }

    public bool SidebarVisible { get; init; } = true;

    /// <summary>Priority raw values (0 none … 3 high).</summary>
    public ValueList<int> PriorityFilter { get; init; } = ValueList<int>.Empty;

    /// <summary>What the user switched off in Settings, as <see cref="WorkspaceElements.RawValue"/> names.</summary>
    public ValueList<string> HiddenElements { get; init; } = ValueList<string>.Empty;

    /// <summary>Projects whose sub-projects are folded away in the sidebar.</summary>
    public ValueList<Guid> CollapsedProjects { get; init; } = ValueList<Guid>.Empty;

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
                ReadWidth(root, "inspectorWidth", defaults.InspectorWidth))
            {
                SidebarSelection = ReadString(root, "sidebarSelection"),
                SelectedTask = ReadString(root, "selectedTask") is { } task && Guid.TryParseExact(task, "D", out var id) ? id : null,
                SidebarVisible = ReadBool(root, "sidebarVisible", defaults.SidebarVisible),
                PriorityFilter = ReadArray(root, "priorityFilter", element =>
                    element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var value) && Enum.IsDefined((Priority)value)
                        ? value
                        : (int?)null),
                HiddenElements = ReadArray(root, "hiddenElements", element =>
                    element.ValueKind == JsonValueKind.String && WorkspaceElements.Parse(element.GetString()!) is not null
                        ? element.GetString()
                        : null),
                CollapsedProjects = ReadArray(root, "collapsedProjects", element =>
                    element.ValueKind == JsonValueKind.String && Guid.TryParseExact(element.GetString(), "D", out var project)
                        ? project
                        : (Guid?)null),
            };
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
                ["sidebarVisible"] = SidebarVisible,
                ["priorityFilter"] = PriorityFilter.ToArray(),
                ["hiddenElements"] = HiddenElements.ToArray(),
                ["collapsedProjects"] = CollapsedProjects.Select(Upper).ToArray(),
            };
            if (SidebarSelection is not null) values["sidebarSelection"] = SidebarSelection;
            if (SelectedTask is { } selected) values["selectedTask"] = Upper(selected);
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

    /// <summary>UUIDs as the Mac writes them.</summary>
    private static string Upper(Guid id) => id.ToString("D").ToUpperInvariant();

    private static string? ReadString(JsonElement root, string key) =>
        root.TryGetProperty(key, out var element) && element.ValueKind == JsonValueKind.String ? element.GetString() : null;

    /// <summary>The entries <paramref name="read"/> accepts; anything else in the array is dropped on its own.</summary>
    private static ValueList<T> ReadArray<T>(JsonElement root, string key, Func<JsonElement, T?> read) where T : struct
    {
        if (!root.TryGetProperty(key, out var element) || element.ValueKind != JsonValueKind.Array) return ValueList<T>.Empty;
        return new ValueList<T>(element.EnumerateArray().Select(read).Where(v => v is not null).Select(v => v!.Value));
    }

    private static ValueList<string> ReadArray(JsonElement root, string key, Func<JsonElement, string?> read)
    {
        if (!root.TryGetProperty(key, out var element) || element.ValueKind != JsonValueKind.Array) return ValueList<string>.Empty;
        return new ValueList<string>(element.EnumerateArray().Select(read).OfType<string>());
    }

    private static double ReadWidth(JsonElement root, string key, double fallback) =>
        root.TryGetProperty(key, out var element) && element.ValueKind == JsonValueKind.Number
            && element.TryGetDouble(out var value) && double.IsFinite(value) && value > 0
            ? value
            : fallback;
}
