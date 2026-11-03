using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using TodoApp.State;
using Windows.ApplicationModel.DataTransfer;

namespace TodoApp;

/// <summary>
/// One sidebar line (SidebarRow in SidebarView.swift): icon, title and open-task count. Clicking selects it,
/// dropping tasks on it files them there, and a project row can be dragged to reorder and has a context menu.
/// </summary>
public sealed partial class SidebarRow : UserControl
{
    private const string ProjectPrefix = "project:";

    private readonly MenuFlyout _projectMenu = new();
    private readonly MenuFlyout _placeMenu = new();
    private AppState? _state;
    private SidebarRowModel? _model;
    private bool _hovering;

    public SidebarRow()
    {
        InitializeComponent();

        var newSubproject = new MenuFlyoutItem { Text = "New Sub-project…" };
        newSubproject.Click += (_, _) =>
        {
            if (_state is not null && _model?.Item.ProjectId is { } id) _state.RequestProjectEditor(null, id);
        };
        var edit = new MenuFlyoutItem { Text = "Edit Project…" };
        edit.Click += (_, _) =>
        {
            if (_state is not null && _model?.Item.ProjectId is { } id) _state.RequestProjectEditor(id);
        };
        var delete = new MenuFlyoutItem { Text = "Delete Project…" };
        delete.Click += (_, _) =>
        {
            if (_state is not null && _model?.Item.ProjectId is { } id) _state.RequestProjectDeletion(id);
        };
        _projectMenu.Items.Add(newSubproject);
        _projectMenu.Items.Add(edit);
        _projectMenu.Items.Add(new MenuFlyoutSeparator());
        _projectMenu.Items.Add(delete);

        // "Hide Today" and "Customize…", filled when it opens so the title is current.
        _placeMenu.Opening += (_, _) =>
        {
            _placeMenu.Items.Clear();
            if (_state is not { } state || _model is not { } model) return;
            var hide = new MenuFlyoutItem { Text = "Hide " + model.Title };
            hide.Click += (_, _) => state.SetVisible(false, model.Item.Element());
            var customize = new MenuFlyoutItem { Text = "Customize…" };
            customize.Click += (_, _) => state.OpenSettings();
            _placeMenu.Items.Add(hide);
            _placeMenu.Items.Add(customize);
        };

        ChevronButton.Click += (_, _) =>
        {
            if (_state is not null && _model?.Item.ProjectId is { } id) _state.ToggleCollapsed(id);
        };
        // The chevron folds the project away; it doesn't open it.
        ChevronButton.Tapped += (_, e) => e.Handled = true;

        Chrome.Tapped += (_, _) =>
        {
            if (_state is not null && _model is not null) _state.Select(_model.Item);
        };
        Chrome.PointerEntered += (_, _) => SetHovering(true);
        Chrome.PointerExited += (_, _) => SetHovering(false);
        Chrome.DragStarting += OnDragStarting;
        Chrome.DragEnter += OnDragOver;
        Chrome.DragOver += OnDragOver;
        Chrome.DragLeave += (_, _) => VisualStateManager.GoToState(this, "NotTargeted", true);
        Chrome.Drop += OnDrop;
        ActualThemeChanged += (_, _) => ApplyTint();
    }

    /// <summary>Shows the model; called each time the repeater prepares this row.</summary>
    public void Bind(AppState state, SidebarRowModel model)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(model);
        _state = state;
        _model = model;

        IconGlyph.Glyph = TintGlyph.Glyph = Glyphs.For(model.Icon);
        TitleText.Text = model.Title;
        CountText.Text = model.Count?.ToString(System.Globalization.CultureInfo.CurrentCulture) ?? string.Empty;
        CountText.Visibility = model.Count is null ? Visibility.Collapsed : Visibility.Visible;
        ApplyTint();

        Body.Padding = new Thickness(10 + 16 * model.Depth, 0, 10, 0);
        ChevronButton.Visibility = model.IsCollapsed is null ? Visibility.Collapsed : Visibility.Visible;
        if (model.IsCollapsed is { } collapsed)
        {
            ChevronButton.Content = Glyphs.For(collapsed ? "chevron.right" : "chevron.down");
            var tip = collapsed ? "Show Sub-projects" : "Hide Sub-projects";
            ToolTipService.SetToolTip(ChevronButton, tip);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(ChevronButton, tip);
        }

        var isProject = model.Item.Kind == SidebarKind.Project;
        Chrome.CanDrag = isProject;
        Chrome.ContextFlyout = isProject ? _projectMenu : _placeMenu;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
            this,
            model.Count is { } count ? $"{model.Title}, {count}" : model.Title);
        UpdateHighlight();
    }

    private void ApplyTint()
    {
        if (_model is null) return;
        var tint = _model.Tint;
        IconGlyph.Visibility = tint is null ? Visibility.Visible : Visibility.Collapsed;
        TintGlyph.Visibility = tint is null ? Visibility.Collapsed : Visibility.Visible;
        if (tint is { } color) TintGlyph.Foreground = ProjectColors.Brush(color, ActualTheme);
    }

    private void SetHovering(bool hovering)
    {
        _hovering = hovering;
        UpdateHighlight();
    }

    private void UpdateHighlight() =>
        VisualStateManager.GoToState(this, _model is { IsSelected: true } ? "Selected" : _hovering ? "PointerOver" : "Normal", true);

    private void OnDragStarting(UIElement sender, DragStartingEventArgs args)
    {
        if (_model?.Item.ProjectId is { } id) args.Data.SetText(ProjectPrefix + id.ToString("D").ToUpperInvariant());
        else args.Cancel = true;
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        // All Tasks and the Dashboard take no drops, so they show no outline and refuse.
        if (_model is not { AcceptsDrops: true } || !e.DataView.Contains(StandardDataFormats.Text))
        {
            VisualStateManager.GoToState(this, "NotTargeted", true);
            return;
        }

        e.AcceptedOperation = DataPackageOperation.Move;
        e.DragUIOverride.IsCaptionVisible = false;
        e.DragUIOverride.IsGlyphVisible = false;
        VisualStateManager.GoToState(this, "DropTarget", true);
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        VisualStateManager.GoToState(this, "NotTargeted", true);
        var state = _state;
        var model = _model;
        if (state is null || model is not { AcceptsDrops: true }) return;
        if (!e.DataView.Contains(StandardDataFormats.Text)) return;

        var deferral = e.GetDeferral();
        try
        {
            var text = await e.DataView.GetTextAsync();
            var items = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            state.HandleDropOnSidebar(items, model.Item);
        }
        catch (Exception ex)
        {
            AppLog.Error("Couldn't read what was dropped on the sidebar", ex);
        }
        finally
        {
            deferral.Complete();
        }
    }
}
