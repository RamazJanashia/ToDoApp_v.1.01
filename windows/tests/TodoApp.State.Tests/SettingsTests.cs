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
    public void NewKeys_RoundTrip()
    {
        var path = PathIn("settings.json");
        var project = Guid.Parse("0905AB33-A30F-4D94-8D6F-7BE7F7B0CA90");
        var task = Guid.Parse("5A8AD87C-2B61-4D86-B8E7-321521337615");
        var custom = new AppSettings
        {
            SidebarSelection = "project:0905AB33-A30F-4D94-8D6F-7BE7F7B0CA90",
            SelectedTask = task,
            SidebarVisible = false,
            PriorityFilter = ValueList.Of(3, 1),
            HiddenElements = ValueList.Of("rowNotes", "toolbarSearch"),
            CollapsedProjects = ValueList.Of(project),
        };

        custom.Save(path);

        Assert.Equal(custom, AppSettings.Load(path));
        var json = File.ReadAllText(path);
        Assert.Contains("\"sidebarSelection\": \"project:0905AB33-A30F-4D94-8D6F-7BE7F7B0CA90\"", json);
        Assert.Contains("\"selectedTask\": \"5A8AD87C-2B61-4D86-B8E7-321521337615\"", json);
        Assert.Contains("\"sidebarVisible\": false", json);
        Assert.Contains("\"priorityFilter\"", json);
        Assert.Contains("\"hiddenElements\"", json);
        Assert.Contains("\"collapsedProjects\"", json);
    }

    [Fact]
    public void NewKeys_AbsentWhenUnset()
    {
        var path = PathIn("settings.json");
        new AppSettings().Save(path);
        var json = File.ReadAllText(path);
        Assert.DoesNotContain("sidebarSelection", json);
        Assert.DoesNotContain("selectedTask", json);
        Assert.Equal(new AppSettings(), AppSettings.Load(path));
    }

    [Fact]
    public void NewKeys_BadEntriesDropped()
    {
        var path = PathIn("settings.json");
        File.WriteAllText(path, """
            { "hiddenElements": ["rowNotes", "bogus", 7], "priorityFilter": [3, 9, "x"],
              "collapsedProjects": ["x", "0905AB33-A30F-4D94-8D6F-7BE7F7B0CA90"], "selectedTask": "nope",
              "sidebarSelection": 12, "sidebarVisible": "no" }
            """);

        var settings = AppSettings.Load(path);

        Assert.Equal(new[] { "rowNotes" }, settings.HiddenElements);
        Assert.Equal(new[] { 3 }, settings.PriorityFilter);
        Assert.Equal(new[] { Guid.Parse("0905AB33-A30F-4D94-8D6F-7BE7F7B0CA90") }, settings.CollapsedProjects);
        Assert.Null(settings.SelectedTask);
        Assert.Null(settings.SidebarSelection);
        Assert.True(settings.SidebarVisible);
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
