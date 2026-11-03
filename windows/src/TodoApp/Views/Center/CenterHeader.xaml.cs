using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TodoApp.State;
using TodoCore;

namespace TodoApp;

/// <summary>
/// Large title, the view switcher, the sort and filter menu and "+", then the subtitle and the progress line
/// (CenterHeader, ViewModeSwitcher and SortFilterMenu in CenterView.swift).
/// </summary>
public sealed partial class CenterHeader : UserControl
{
    private const double ReadingWidth = 860;

    private AppState? _state;

    public CenterHeader()
    {
        InitializeComponent();
        ListButton.Content = Glyphs.For("list.bullet");
        BoardButton.Content = Glyphs.For("rectangle.split.3x1");
        CalendarButton.Content = Glyphs.For("calendar");
        FilterButton.Content = Glyphs.For("line.3.horizontal.decrease");
        NewTaskButton.Content = Glyphs.For("plus");
    }

    public void Render(AppState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _state = state;
        // Counted without a project's finished tasks from earlier days.
        var model = HeaderModel.Build(state, state.CurrentTasks);

        TitleText.Text = model.Title;
        SubtitleText.Text = model.Subtitle;
        Progress.Fraction = model.Progress;
        Progress.Visibility = model.ShowsProgress ? Visibility.Visible : Visibility.Collapsed;
        Root.Padding = new Thickness(32, 26, 32, model.ShowsProgress ? 10 : 4);
        Body.MaxWidth = state.ViewMode == ViewMode.List ? ReadingWidth : double.PositiveInfinity;

        var mode = state.ViewMode switch
        {
            ViewMode.Board => "BoardMode",
            ViewMode.Calendar => "CalendarMode",
            _ => "ListMode",
        };
        VisualStateManager.GoToState(this, mode, false);
        VisualStateManager.GoToState(this, IsFiltering(state) ? "Filtering" : "NotFiltering", false);
        // Only the views left on in Settings.
        ListButton.Visibility = state.Shows(WorkspaceElement.ListView) ? Visibility.Visible : Visibility.Collapsed;
        BoardButton.Visibility = state.Shows(WorkspaceElement.BoardView) ? Visibility.Visible : Visibility.Collapsed;
        CalendarButton.Visibility = state.Shows(WorkspaceElement.CalendarView) ? Visibility.Visible : Visibility.Collapsed;

        // Never empty, so the button always has a menu to open; Opening keeps it current.
        if (FilterMenu.Items.Count == 0) FillFilterMenu();
    }

    private static bool IsFiltering(AppState state) =>
        state.PriorityFilter.Count > 0 || !state.ShowCompleted || state.Sort != TaskSort.Manual;

    private void OnList(object sender, RoutedEventArgs e) => SetViewMode(ViewMode.List);

    private void OnBoard(object sender, RoutedEventArgs e) => SetViewMode(ViewMode.Board);

    private void OnCalendar(object sender, RoutedEventArgs e) => SetViewMode(ViewMode.Calendar);

    private void SetViewMode(ViewMode mode)
    {
        if (_state is not null) _state.ViewMode = mode;
    }

    private void OnNewTask(object sender, RoutedEventArgs e) => _state?.BeginNewTask();

    /// <summary>Rebuilt on every open, so the checks are current.</summary>
    private void OnFilterMenuOpening(object? sender, object e) => FillFilterMenu();

    private void FillFilterMenu()
    {
        var items = FilterMenu.Items;
        items.Clear();
        if (_state is not { } state) return;

        items.Add(Heading("Sort By"));
        foreach (var sort in Enum.GetValues<TaskSort>())
        {
            var item = new RadioMenuFlyoutItem { Text = sort.Title(), GroupName = "Sort", IsChecked = state.Sort == sort };
            item.Click += (_, _) => state.Sort = sort;
            items.Add(item);
        }

        items.Add(new MenuFlyoutSeparator());
        items.Add(Heading("Priority"));
        foreach (var priority in Priorities.Descending)
        {
            var item = new ToggleMenuFlyoutItem { Text = priority.Title(), IsChecked = state.PriorityFilter.Contains(priority) };
            item.Click += (_, _) =>
            {
                var filter = state.PriorityFilter.ToHashSet();
                if (!filter.Remove(priority)) filter.Add(priority);
                state.PriorityFilter = filter;
            };
            items.Add(item);
        }

        items.Add(new MenuFlyoutSeparator());
        var showCompleted = new ToggleMenuFlyoutItem { Text = "Show Completed", IsChecked = state.ShowCompleted };
        showCompleted.Click += (_, _) => state.ShowCompleted = !state.ShowCompleted;
        items.Add(showCompleted);

        if (IsFiltering(state))
        {
            items.Add(new MenuFlyoutSeparator());
            var reset = new MenuFlyoutItem { Text = "Reset" };
            reset.Click += (_, _) =>
            {
                state.PriorityFilter = new HashSet<Priority>();
                state.ShowCompleted = true;
                state.Sort = TaskSort.Manual;
            };
            items.Add(reset);
        }
    }

    /// <summary>A section title inside the menu ("Sort By", "Priority"), as the Mac's inline picker and section show.</summary>
    private static MenuFlyoutItem Heading(string text) => new() { Text = text, IsEnabled = false };
}
