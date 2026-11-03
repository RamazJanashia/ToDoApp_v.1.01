namespace TodoApp.State;

/// <summary>Responsive widths of the sidebar and inspector columns (RootView.swift).</summary>
public static class ColumnLayout
{
    public const double CenterMinWidth = 500;

    public static readonly (double Min, double Max) SidebarRange = (200, 340);

    public static readonly (double Min, double Max) InspectorRange = (290, 480);

    /// <summary>
    /// The columns use the widths the user dragged them to. When that would leave the task list too little room,
    /// they give way down to their minimums, the inspector first. A hidden column takes no room.
    /// </summary>
    public static (double Sidebar, double Inspector) Widths(
        double total, double sidebarPreferred, double inspectorPreferred, bool sidebarVisible, bool inspectorVisible)
    {
        var sidebar = Math.Clamp(sidebarPreferred, SidebarRange.Min, SidebarRange.Max);
        var inspector = Math.Clamp(inspectorPreferred, InspectorRange.Min, InspectorRange.Max);
        var excess = CenterMinWidth - total
            + (sidebarVisible ? sidebar + 1 : 0)
            + (inspectorVisible ? inspector + 1 : 0);
        if (excess > 0 && inspectorVisible)
        {
            var cut = Math.Min(excess, inspector - InspectorRange.Min);
            inspector -= cut;
            excess -= cut;
        }

        if (excess > 0 && sidebarVisible) sidebar -= Math.Min(excess, sidebar - SidebarRange.Min);
        return (sidebar, inspector);
    }

    /// <summary>The window can't shrink below what the visible columns need at their narrowest (each plus a 1px divider).</summary>
    public static double MinimumWidth(bool sidebarVisible, bool inspectorVisible) =>
        (sidebarVisible ? SidebarRange.Min + 1 : 0)
        + CenterMinWidth
        + (inspectorVisible ? InspectorRange.Min + 1 : 0);
}
