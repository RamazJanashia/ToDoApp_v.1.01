using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TodoApp.State;

namespace TodoApp;

/// <summary>
/// The centre column (CenterView.swift): the dashboard, or the header over the list, board or calendar. Only the
/// view on screen is rendered.
/// </summary>
public sealed partial class CenterView : UserControl
{
    public CenterView()
    {
        InitializeComponent();
    }

    public void Render(AppState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var dashboard = state.IsDashboard;
        var mode = state.ViewMode;
        Show(DashboardHost, dashboard);
        Show(Header, !dashboard);
        Show(TaskList, !dashboard && mode == ViewMode.List);
        Show(Board, !dashboard && mode == ViewMode.Board);
        Show(CalendarHost, !dashboard && mode == ViewMode.Calendar);
        if (dashboard) return;

        Header.Render(state);
        if (mode == ViewMode.List) TaskList.Render(state);
        else if (mode == ViewMode.Board) Board.Render(state);
    }

    private static void Show(UIElement element, bool visible) =>
        element.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
}
