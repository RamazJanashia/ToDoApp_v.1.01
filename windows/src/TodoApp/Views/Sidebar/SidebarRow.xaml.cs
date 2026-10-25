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
    private AppState? _state;
    private SidebarRowModel? _model;
    private bool _hovering;

    public SidebarRow()
    {
        InitializeComponent();

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
        _projectMenu.Items.Add(edit);
        _projectMenu.Items.Add(new MenuFlyoutSeparator());
        _projectMenu.Items.Add(delete);

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

        var isProject = model.Item.Kind == SidebarKind.Project;
        Chrome.CanDrag = isProject;
        Chrome.ContextFlyout = isProject ? _projectMenu : null;
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
        // The Dashboard takes no drops, so it shows no outline and refuses.
        if (_model is null || _model.Item.Kind == SidebarKind.Dashboard || !e.DataView.Contains(StandardDataFormats.Text))
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
        if (state is null || model is null || model.Item.Kind == SidebarKind.Dashboard) return;
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
