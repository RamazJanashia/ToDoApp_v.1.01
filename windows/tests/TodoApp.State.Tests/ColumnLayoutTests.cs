namespace TodoApp.State.Tests;

public sealed class ColumnLayoutTests
{
    [Fact]
    public void ColumnLayout_GivesWayOnSmallWindows()
    {
        var (sidebar, inspector) = ColumnLayout.Widths(1000, 250, 340);

        Assert.Equal(200, sidebar);
        Assert.Equal(290, inspector);
    }

    [Fact]
    public void ColumnLayout_UsesPreferredWidthsOnWideWindows()
    {
        var (sidebar, inspector) = ColumnLayout.Widths(1600, 250, 340);

        Assert.Equal(250, sidebar);
        Assert.Equal(340, inspector);
    }

    [Fact]
    public void ColumnLayout_MiddleSizesShrinkProportionally()
    {
        // 19% of 1200 is 228 and 25% is 300, both between the minimum and the preferred width.
        var (sidebar, inspector) = ColumnLayout.Widths(1200, 250, 340);

        Assert.Equal(228, sidebar, 6);
        Assert.Equal(300, inspector, 6);
    }

    [Fact]
    public void ColumnLayout_MinimumWidth()
    {
        Assert.Equal(992, ColumnLayout.MinimumWidth(sidebarVisible: true, inspectorVisible: true));
        Assert.Equal(701, ColumnLayout.MinimumWidth(sidebarVisible: true, inspectorVisible: false));
        Assert.Equal(791, ColumnLayout.MinimumWidth(sidebarVisible: false, inspectorVisible: true));
        Assert.Equal(500, ColumnLayout.MinimumWidth(sidebarVisible: false, inspectorVisible: false));
    }
}
