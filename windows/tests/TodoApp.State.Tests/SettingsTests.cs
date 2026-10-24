using TodoCore;

namespace TodoApp.State.Tests;

public sealed class SettingsTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private string PathIn(string name) => Path.Combine(_temp.Path, name);

    [Fact]
    public void Settings_RoundTrip_AndCorruptGivesDefaults()
    {
        var path = PathIn("settings.json");
        var custom = new AppSettings(
            ViewMode.Calendar, TaskSort.DueDate, ShowCompleted: false, InspectorVisible: false,
            AppearancePreference.Dark, SidebarWidth: 300.5, InspectorWidth: 410);

        custom.Save(path);

        Assert.Equal(custom, AppSettings.Load(path));
        var json = File.ReadAllText(path);
        Assert.Contains("\"viewMode\": \"calendar\"", json);
        Assert.Contains("\"sort\": \"dueDate\"", json);
        Assert.Contains("\"appearance\": \"dark\"", json);
        Assert.Contains("\"showCompleted\": false", json);
        Assert.False(File.Exists(path + ".tmp"));

        File.WriteAllText(path, "{ not json");
        Assert.Equal(new AppSettings(), AppSettings.Load(path));
        File.WriteAllText(path, "[1,2]");
        Assert.Equal(new AppSettings(), AppSettings.Load(path));
        File.WriteAllBytes(path, Array.Empty<byte>());
        Assert.Equal(new AppSettings(), AppSettings.Load(path));
        Assert.Equal(new AppSettings(), AppSettings.Load(PathIn("missing.json")));
    }

    [Fact]
    public void Defaults_MatchTheMac()
    {
        var defaults = new AppSettings();

        Assert.Equal(ViewMode.List, defaults.ViewMode);
        Assert.Equal(TaskSort.Manual, defaults.Sort);
        Assert.True(defaults.ShowCompleted);
        Assert.True(defaults.InspectorVisible);
        Assert.Equal(AppearancePreference.System, defaults.Appearance);
        Assert.Equal(250, defaults.SidebarWidth);
        Assert.Equal(340, defaults.InspectorWidth);
    }

    [Fact]
    public void Load_UnknownEnumStringFallsBackPerField()
    {
        var path = PathIn("settings.json");
        File.WriteAllText(path, """
            { "viewMode": "kanban", "sort": "priority", "appearance": "sepia",
              "showCompleted": false, "inspectorVisible": "yes", "sidebarWidth": 280 }
            """);

        var loaded = AppSettings.Load(path);

        Assert.Equal(new AppSettings(Sort: TaskSort.Priority, ShowCompleted: false, SidebarWidth: 280), loaded);
    }

    [Fact]
    public void Save_CreatesDirectory()
    {
        var path = Path.Combine(_temp.Path, "nested", "settings.json");

        new AppSettings(ViewMode: ViewMode.Board).Save(path);

        Assert.Equal(ViewMode.Board, AppSettings.Load(path).ViewMode);
    }
}
