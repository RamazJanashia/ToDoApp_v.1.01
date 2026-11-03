using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using TodoApp.State;
using TodoCore;
using Windows.System;

namespace TodoApp;

/// <summary>
/// Name, colour and icon for a new or existing project (ProjectEditorView.swift). Enter saves and Esc cancels;
/// Create/Save is disabled while the trimmed name is empty. "Delete Project…" closes this and asks to confirm.
/// </summary>
public sealed partial class ProjectEditorDialog : ContentDialog
{
    private const int IconColumns = 8;

    private readonly AppState _state;
    private readonly Guid? _projectId;
    private readonly ElementTheme _theme;
    private readonly Dictionary<ProjectColor, ToggleButton> _swatches = [];
    private readonly Dictionary<string, ToggleButton> _icons = [];
    private readonly List<Guid?> _parentChoices = [];
    private string _icon = "folder";
    private ProjectColor _color = ProjectColor.Blue;
    private bool _deleteRequested;

    private ProjectEditorDialog(AppState state, ProjectEditorRequest request, ElementTheme theme)
    {
        _state = state;
        _projectId = request.ProjectId;
        _theme = theme;
        InitializeComponent();

        var isNew = _projectId is null;
        TitleText.Text = isNew ? (request.ParentId is null ? "New Project" : "New Sub-project") : "Edit Project";
        SaveButton.Content = isNew ? "Create" : "Save";
        DeleteButton.Visibility = isNew ? Visibility.Collapsed : Visibility.Visible;

        Guid? parentId = null;
        if (state.Store.FindProject(_projectId) is { } project)
        {
            NameBox.Text = project.Name;
            _icon = project.Icon;
            _color = project.Color;
            parentId = project.ParentId;
        }
        else if (state.Store.FindProject(request.ParentId) is { } parent)
        {
            // A new sub-project starts in its parent's colour.
            _color = parent.Color;
            parentId = parent.Id;
        }

        BuildParentChoices(parentId);
        BuildSwatches();
        BuildIcons();
        Refresh();
        Opened += (_, _) => NameBox.Focus(FocusState.Programmatic);
    }

    /// <summary>
    /// Shows the editor for a new project (<paramref name="projectId"/> null) or an existing one. Creating selects the
    /// new project. Returns once the dialog is closed.
    /// </summary>
    public static async Task ShowAsync(XamlRoot xamlRoot, AppState state, ProjectEditorRequest request)
    {
        ArgumentNullException.ThrowIfNull(xamlRoot);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(request);
        var projectId = request.ProjectId;

        var theme = xamlRoot.Content is FrameworkElement root ? root.ActualTheme : ElementTheme.Default;
        var dialog = new ProjectEditorDialog(state, request, theme);
        await Dialogs.ShowAsync(dialog, xamlRoot);

        // Asked for after this dialog has closed, as only one can be open at a time.
        if (dialog._deleteRequested && projectId is { } id) state.RequestProjectDeletion(id);
    }

    private string TrimmedName => NameBox.Text.Trim();

    private void BuildSwatches()
    {
        foreach (var color in Enum.GetValues<ProjectColor>())
        {
            var swatch = new ToggleButton
            {
                Style = (Style)Resources["SwatchStyle"],
                Foreground = ProjectColors.Brush(color, _theme),
            };
            ToolTipService.SetToolTip(swatch, color.ToString());
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(swatch, color.ToString());
            swatch.Click += (_, _) =>
            {
                _color = color;
                Refresh();
            };
            _swatches[color] = swatch;
            Swatches.Children.Add(swatch);
        }
    }

    private void BuildIcons()
    {
        for (var column = 0; column < IconColumns; column++) IconGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        var rows = (Project.IconChoices.Count + IconColumns - 1) / IconColumns;
        for (var row = 0; row < rows; row++) IconGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });

        for (var i = 0; i < Project.IconChoices.Count; i++)
        {
            var symbol = Project.IconChoices[i];
            var cell = new ToggleButton
            {
                Style = (Style)Resources["IconChoiceStyle"],
                Content = Glyphs.For(symbol),
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(cell, symbol);
            cell.Click += (_, _) =>
            {
                _icon = symbol;
                Refresh();
            };
            Grid.SetColumn(cell, i % IconColumns);
            Grid.SetRow(cell, i / IconColumns);
            _icons[symbol] = cell;
            IconGrid.Children.Add(cell);
        }
    }

    /// <summary>
    /// "Nothing (top level)", then every project it may sit inside (not itself or anything under it), indented four
    /// spaces per level.
    /// </summary>
    private void BuildParentChoices(Guid? selected)
    {
        _parentChoices.Clear();
        _parentChoices.Add(null);
        ParentBox.Items.Add("Nothing (top level)");
        foreach (var row in _state.Store.Projects.ParentChoices(_projectId))
        {
            _parentChoices.Add(row.Id);
            ParentBox.Items.Add(new string(' ', 4 * row.Depth) + row.Project.Name);
        }

        ParentBox.SelectedIndex = Math.Max(0, _parentChoices.IndexOf(selected));
    }

    private Guid? SelectedParent => ParentBox.SelectedIndex is var index and >= 0 ? _parentChoices[index] : null;

    /// <summary>Redraws the preview, the swatch and icon selection and the Create/Save state.</summary>
    private void Refresh()
    {
        var tint = ProjectColors.Brush(_color, _theme);
        PreviewGlyph.Glyph = Glyphs.For(_icon);
        PreviewGlyph.Foreground = tint;
        foreach (var (color, swatch) in _swatches) swatch.IsChecked = color == _color;
        foreach (var (symbol, cell) in _icons)
        {
            cell.IsChecked = symbol == _icon;
            cell.Foreground = tint;
        }

        SaveButton.IsEnabled = TrimmedName.Length > 0;
    }

    private void OnNameChanged(object sender, TextChangedEventArgs e) => SaveButton.IsEnabled = TrimmedName.Length > 0;

    private void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        // Enter saves from anywhere but a focused button, where it presses that button.
        if (e.Key == VirtualKey.Enter && e.OriginalSource is not ButtonBase)
        {
            e.Handled = true;
            Save();
        }
        else if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            Hide();
        }
    }

    private void OnSave(object sender, RoutedEventArgs e) => Save();

    private void OnCancel(object sender, RoutedEventArgs e) => Hide();

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        _deleteRequested = true;
        Hide();
    }

    private void Save()
    {
        var name = TrimmedName;
        if (name.Length == 0) return;

        var parentId = SelectedParent;
        if (_projectId is { } id)
        {
            var icon = _icon;
            var color = _color;
            _state.Store.UpdateProject(id, project => project with { Name = name, Icon = icon, Color = color, ParentId = parentId });
        }
        else
        {
            var project = _state.Store.CreateProject(name, _icon, _color, parentId);
            _state.Select(SidebarItem.Project(project.Id));
        }

        Hide();
    }
}
