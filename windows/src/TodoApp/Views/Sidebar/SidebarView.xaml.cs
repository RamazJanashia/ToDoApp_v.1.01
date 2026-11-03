using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TodoApp.State;

namespace TodoApp;

/// <summary>
/// The left column (SidebarView.swift): the workspace rows, then the projects with a "+" that shows while the
/// pointer is over the column, or always when there are none. Public surface is <see cref="Render"/>.
/// </summary>
public sealed partial class SidebarView : UserControl
{
    private readonly ObservableCollection<SidebarRowModel> _workspace = [];
    private readonly ObservableCollection<SidebarRowModel> _projects = [];
    private AppState? _state;
    private bool _hovering;
    private bool _noProjects = true;

    public SidebarView()
    {
        InitializeComponent();
        NewProjectButton.Content = Glyphs.For("plus");
        WorkspaceRepeater.ItemsSource = _workspace;
        ProjectsRepeater.ItemsSource = _projects;
        WorkspaceRepeater.ElementPrepared += (_, e) => Prepare(_workspace, e);
        ProjectsRepeater.ElementPrepared += (_, e) => Prepare(_projects, e);
        CustomizeGlyph.Glyph = Glyphs.For("slider.horizontal.3");
        WorkspaceHeader.ContextFlyout = HeaderMenu(("Customize…", s => s.OpenSettings()));
        ProjectsHeader.ContextFlyout = HeaderMenu(
            ("Hide Projects", s => s.SetVisible(false, WorkspaceElement.Projects)),
            ("Customize…", s => s.OpenSettings()));
        Root.PointerEntered += (_, _) => SetHovering(true);
        Root.PointerExited += (_, _) => SetHovering(false);
    }

    public void Render(AppState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _state = state;
        var model = SidebarModel.Build(state);
        CollectionSync.Sync(_workspace, model.Workspace, row => row.Item);
        CollectionSync.Sync(_projects, model.Projects, row => row.Item);

        WorkspaceHeader.Visibility = model.ShowsWorkspaceHeader ? Visibility.Visible : Visibility.Collapsed;
        ProjectsHeader.Visibility = model.ShowsProjects ? Visibility.Visible : Visibility.Collapsed;
        ProjectsHeader.Margin = new Thickness(10, model.ShowsWorkspaceHeader ? 26 : 0, 2, 4);
        _noProjects = model.ShowsEmptyProjectsHint;
        EmptyHint.Visibility = _noProjects ? Visibility.Visible : Visibility.Collapsed;
        ProjectsRepeater.Visibility = model.ShowsProjects && !_noProjects ? Visibility.Visible : Visibility.Collapsed;
        UpdatePlus();
    }

    private MenuFlyout HeaderMenu(params (string Text, Action<AppState> Action)[] items)
    {
        var menu = new MenuFlyout();
        foreach (var (text, action) in items)
        {
            var item = new MenuFlyoutItem { Text = text };
            item.Click += (_, _) =>
            {
                if (_state is { } state) action(state);
            };
            menu.Items.Add(item);
        }

        return menu;
    }

    private void OnCustomize(object sender, RoutedEventArgs e) => _state?.OpenSettings();

    private void Prepare(ObservableCollection<SidebarRowModel> rows, ItemsRepeaterElementPreparedEventArgs e)
    {
        if (_state is not null && e.Element is SidebarRow row) row.Bind(_state, rows[e.Index]);
    }

    private void SetHovering(bool hovering)
    {
        _hovering = hovering;
        UpdatePlus();
    }

    private void UpdatePlus() => NewProjectButton.Opacity = _hovering || _noProjects ? 1 : 0;

    private void OnNewProject(object sender, RoutedEventArgs e) => _state?.RequestProjectEditor(null);
}
