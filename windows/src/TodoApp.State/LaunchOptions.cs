using System.Globalization;

namespace TodoApp.State;

/// <summary>
/// What the command line and environment ask for: the data folder override, plus the switches
/// used to open the app in a given state for screenshots. Unknown or invalid input is ignored,
/// because Windows may add its own activation arguments.
/// </summary>
public sealed record LaunchOptions(
    string? DataDirectory,
    SidebarItem? Sidebar,
    ViewMode? View,
    AppearancePreference? Theme,
    bool SelectFirst,
    bool ShowSettings,
    (int Width, int Height)? Size)
{
    public const string DataDirectoryVariable = "TODOAPP_DATA_DIR";

    /// <summary><c>--data-dir</c> wins over <c>TODOAPP_DATA_DIR</c>.</summary>
    public static LaunchOptions Parse(IReadOnlyList<string> args, Func<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(environment);

        string? dataDirectory = null;
        SidebarItem? sidebar = null;
        ViewMode? view = null;
        AppearancePreference? theme = null;
        var selectFirst = false;
        var showSettings = false;
        (int Width, int Height)? size = null;

        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--select-first":
                    selectFirst = true;
                    break;
                case "--settings":
                    showSettings = true;
                    break;
                case "--data-dir":
                    if (TakeValue(args, ref i) is { } directory && !string.IsNullOrWhiteSpace(directory)) dataDirectory = directory;
                    break;
                case "--sidebar":
                    if (ParseSidebar(TakeValue(args, ref i)) is { } item) sidebar = item;
                    break;
                case "--view":
                    if (ParseView(TakeValue(args, ref i)) is { } mode) view = mode;
                    break;
                case "--theme":
                    if (ParseTheme(TakeValue(args, ref i)) is { } preference) theme = preference;
                    break;
                case "--size":
                    if (ParseSize(TakeValue(args, ref i)) is { } parsed) size = parsed;
                    break;
            }
        }

        if (dataDirectory is null && environment(DataDirectoryVariable) is { } fromEnvironment
            && !string.IsNullOrWhiteSpace(fromEnvironment))
        {
            dataDirectory = fromEnvironment;
        }

        return new LaunchOptions(dataDirectory, sidebar, view, theme, selectFirst, showSettings, size);
    }

    /// <summary>The argument after the option, unless it is missing or is itself an option.</summary>
    private static string? TakeValue(IReadOnlyList<string> args, ref int index)
    {
        if (index + 1 >= args.Count || args[index + 1].StartsWith("--", StringComparison.Ordinal)) return null;
        index++;
        return args[index];
    }

    private static SidebarItem? ParseSidebar(string? value) => value?.ToLowerInvariant() switch
    {
        "alltasks" => SidebarItem.AllTasks,
        "today" => SidebarItem.Today,
        "upcoming" => SidebarItem.Upcoming,
        "completed" => SidebarItem.Completed,
        "dashboard" => SidebarItem.Dashboard,
        _ => null,
    };

    private static ViewMode? ParseView(string? value) => value?.ToLowerInvariant() switch
    {
        "list" => ViewMode.List,
        "board" => ViewMode.Board,
        "calendar" => ViewMode.Calendar,
        _ => null,
    };

    private static AppearancePreference? ParseTheme(string? value) => value?.ToLowerInvariant() switch
    {
        "system" => AppearancePreference.System,
        "light" => AppearancePreference.Light,
        "dark" => AppearancePreference.Dark,
        _ => null,
    };

    /// <summary><c>WIDTHxHEIGHT</c>, both positive whole numbers.</summary>
    private static (int Width, int Height)? ParseSize(string? value)
    {
        var parts = value?.Split('x', 'X');
        if (parts is not { Length: 2 }) return null;
        if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var width) || width <= 0) return null;
        if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var height) || height <= 0) return null;
        return (width, height);
    }
}
