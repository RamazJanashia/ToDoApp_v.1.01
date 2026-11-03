namespace TodoApp.State.Tests;

public sealed class ColumnLayoutTests
{
    // The Mac's columnWidths (RootView.swift): dragged widths stand until the list would get less than 500 px;
    // then the inspector gives way first, then the sidebar, each down to its minimum. A divider is 1 px.

    [Fact]
    public void Widths_WideWindowKeepsDraggedWidths() =>
        Assert.Equal((300.0, 400.0), ColumnLayout.Widths(1600, 300, 400, true, true));

    [Fact]
    public void Widths_MiddleWindowKeepsDraggedWidths() =>
        Assert.Equal((250.0, 340.0), ColumnLayout.Widths(1200, 250, 340, true, true));

    [Fact]
    public void Widths_InspectorGivesWayFirst() =>
        // 500 − 1100 + 301 + 401 = 102 too many: all of it from the inspector.
        Assert.Equal((300.0, 298.0), ColumnLayout.Widths(1100, 300, 400, true, true));

    [Fact]
    public void Widths_ThenSidebar() =>
        // 302 too many: the inspector gives 110 (to 290), the sidebar 100 (to 200).
        Assert.Equal((200.0, 290.0), ColumnLayout.Widths(900, 300, 400, true, true));

    [Fact]
    public void Widths_HiddenColumnsDontCount()
    {
        var (sidebar, _) = ColumnLayout.Widths(800, 300, 400, true, false);
        Assert.Equal(299, sidebar);
    }

    [Fact]
    public void Widths_ClampedToRanges() =>
        Assert.Equal((200.0, 480.0), ColumnLayout.Widths(3000, 50, 900, true, true));

    [Fact]
    public void ColumnLayout_MinimumWidth()
    {
        Assert.Equal(992, ColumnLayout.MinimumWidth(sidebarVisible: true, inspectorVisible: true));
        Assert.Equal(701, ColumnLayout.MinimumWidth(sidebarVisible: true, inspectorVisible: false));
        Assert.Equal(791, ColumnLayout.MinimumWidth(sidebarVisible: false, inspectorVisible: true));
        Assert.Equal(500, ColumnLayout.MinimumWidth(sidebarVisible: false, inspectorVisible: false));
    }
}
