namespace TodoApp.State;

/// <summary>Responsive widths of the sidebar and inspector columns (RootView.swift).</summary>
public static class ColumnLayout
{
    public const double CenterMinWidth = 500;

    public static readonly (double Min, double Max) SidebarRange = (200, 340);

    public static readonly (double Min, double Max) InspectorRange = (290, 480);

    /// <summary>
    /// On small windows the side columns give way (down to their minimums) so the task list keeps room to
    /// breathe; on larger windows they use the widths the user dragged them to.
    /// </summary>
    public static (double Sidebar, double Inspector) Widths(double total, double sidebarPreferred, double inspectorPreferred)
    {
        var sidebar = Math.Min(sidebarPreferred, Math.Max(SidebarRange.Min, total * 0.19));
        var inspector = Math.Min(inspectorPreferred, Math.Max(InspectorRange.Min, total * 0.25));
        return (sidebar, inspector);
    }

    /// <summary>The window can't shrink below what the visible columns need at their narrowest (each plus a 1px divider).</summary>
    public static double MinimumWidth(bool sidebarVisible, bool inspectorVisible) =>
        (sidebarVisible ? SidebarRange.Min + 1 : 0)
        + CenterMinWidth
        + (inspectorVisible ? InspectorRange.Min + 1 : 0);
}
