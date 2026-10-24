namespace TodoApp.State.Tests;

public sealed class LaunchOptionsTests
{
    private static string? NoEnv(string _) => null;

    [Fact]
    public void LaunchOptions_ParsesAll()
    {
        var options = LaunchOptions.Parse(
            new[]
            {
                "--data-dir", @"D:\data", "--sidebar", "upcoming", "--view", "board", "--theme", "dark",
                "--select-first", "--settings", "--size", "1000x820",
            },
            NoEnv);

        Assert.Equal(@"D:\data", options.DataDirectory);
        Assert.Equal(SidebarItem.Upcoming, options.Sidebar);
        Assert.Equal(ViewMode.Board, options.View);
        Assert.Equal(AppearancePreference.Dark, options.Theme);
        Assert.True(options.SelectFirst);
        Assert.True(options.ShowSettings);
        Assert.Equal((1000, 820), options.Size);
    }

    [Theory]
    [InlineData("inbox", SidebarKind.Inbox)]
    [InlineData("today", SidebarKind.Today)]
    [InlineData("upcoming", SidebarKind.Upcoming)]
    [InlineData("completed", SidebarKind.Completed)]
    [InlineData("Dashboard", SidebarKind.Dashboard)]
    public void LaunchOptions_SidebarNames(string name, SidebarKind kind)
    {
        var options = LaunchOptions.Parse(new[] { "--sidebar", name }, NoEnv);

        Assert.Equal(kind, options.Sidebar!.Value.Kind);
        Assert.Null(options.Sidebar.Value.ProjectId);
    }

    [Fact]
    public void LaunchOptions_NothingGivenMeansNothingSet()
    {
        var options = LaunchOptions.Parse(Array.Empty<string>(), NoEnv);

        Assert.Equal(new LaunchOptions(null, null, null, null, false, false, null), options);
    }

    [Fact]
    public void LaunchOptions_EnvironmentFallback()
    {
        string? Env(string name) => name == "TODOAPP_DATA_DIR" ? @"E:\fromenv" : null;

        Assert.Equal(@"E:\fromenv", LaunchOptions.Parse(Array.Empty<string>(), Env).DataDirectory);
        Assert.Equal(@"D:\arg", LaunchOptions.Parse(new[] { "--data-dir", @"D:\arg" }, Env).DataDirectory);
        Assert.Null(LaunchOptions.Parse(Array.Empty<string>(), _ => "  ").DataDirectory);
    }

    [Fact]
    public void LaunchOptions_IgnoresUnknown()
    {
        var options = LaunchOptions.Parse(
            new[]
            {
                "-ToastActivated", "----AppNotificationActivated:", "--bogus", "--sidebar", "nowhere",
                "--view", "kanban", "--theme", "sepia", "--size", "wide", "--size", "0x500", "--size", "-5x500",
                "--sidebar", "5C8E9F2A-0000-0000-0000-000000000000", "--data-dir",
            },
            NoEnv);

        Assert.Equal(new LaunchOptions(null, null, null, null, false, false, null), options);
    }

    [Fact]
    public void LaunchOptions_ValidOptionsSurviveUnknownNeighbours()
    {
        var options = LaunchOptions.Parse(
            new[] { "-ToastActivated", "--settings", "stray", "--size", "1280X720", "--theme", "LIGHT" },
            NoEnv);

        Assert.True(options.ShowSettings);
        Assert.Equal((1280, 720), options.Size);
        Assert.Equal(AppearancePreference.Light, options.Theme);
    }
}
