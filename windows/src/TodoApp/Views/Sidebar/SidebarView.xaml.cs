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

        _noProjects = model.ShowsEmptyProjectsHint;
        EmptyHint.Visibility = _noProjects ? Visibility.Visible : Visibility.Collapsed;
        ProjectsRepeater.Visibility = _noProjects ? Visibility.Collapsed : Visibility.Visible;
        UpdatePlus();
    }

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
