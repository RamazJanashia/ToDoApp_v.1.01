using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using TodoApp.State;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI.Text;

namespace TodoApp;

/// <summary>
/// One task in the list (TaskRowView.swift): check circle, title, meta line, an ⓘ on hover and the priority flag over a
/// hairline. A click selects, a double-click opens the details. The row is a drag source, and dropping a task on it
/// puts that task in front. Right-click opens <see cref="TaskContextMenu"/>.
/// </summary>
public sealed partial class TaskRow : UserControl
{
    private AppState? _state;
    private TaskRowModel? _model;
    private bool _hovering;

    public TaskRow()
    {
        InitializeComponent();
        DetailsButton.Content = Glyphs.For("info.circle");

        Check.Toggled += (_, _) =>
        {
            if (_state is not null && _model is not null) _state.ToggleCompleted(_model.Id);
        };
        DetailsButton.Click += (_, _) =>
        {
            if (_state is not null && _model is not null) _state.RevealInInspector(_model.Id, focusTitle: false);
        };
        // The button's own clicks aren't row clicks.
        DetailsButton.Tapped += (_, e) => e.Handled = true;
        DetailsButton.DoubleTapped += (_, e) => e.Handled = true;

        Chrome.Tapped += (_, _) =>
        {
            if (_state is not null && _model is not null) _state.HandleClick(_model.Id, isDoubleClick: false);
        };
        Chrome.DoubleTapped += (_, _) =>
        {
            if (_state is not null && _model is not null) _state.HandleClick(_model.Id, isDoubleClick: true);
        };
        Chrome.PointerEntered += (_, _) => SetHovering(true);
        Chrome.PointerExited += (_, _) => SetHovering(false);
        Chrome.DragStarting += OnDragStarting;
        Chrome.DragEnter += OnDragOver;
        Chrome.DragOver += OnDragOver;
        Chrome.DragLeave += (_, _) => DropLine.Opacity = 0;
        Chrome.Drop += OnDrop;
    }

    /// <summary>Shows the model; called each time the repeater prepares this row.</summary>
    public void Bind(AppState state, TaskRowModel model)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(model);

        // A recycled row showing a different task starts without hover, and gets that task's menu.
        if (_model?.Id != model.Id || _state != state)
        {
            _hovering = false;
            Chrome.ContextFlyout = TaskContextMenu.Build(model.Task, state);
        }

        _state = state;
        _model = model;

        TitleText.Text = model.Title;
        TitleText.TextDecorations = model.IsCompleted ? TextDecorations.Strikethrough : TextDecorations.None;
        VisualStateManager.GoToState(this, model.IsCompleted || model.IsUntitled ? "TitleDimmed" : "TitleNormal", false);
        Check.IsDone = model.IsCompleted;
        Meta.Show(model.Meta);
        Flag.Show(model.Priority, dimmed: model.IsCompleted);
        AutomationProperties.SetName(this, model.Title);
        DropLine.Opacity = 0;
        UpdateHighlight();
    }

    private void SetHovering(bool hovering)
    {
        _hovering = hovering;
        UpdateHighlight();
    }

    /// <summary>Selected rows get the selection fill at 0.75; otherwise hover gets the hover fill. The ⓘ shows on hover.</summary>
    private void UpdateHighlight()
    {
        var selected = _model?.IsSelected == true;
        SelectionFill.Opacity = selected ? 0.75 : 0;
        HoverFill.Opacity = !selected && _hovering ? 1 : 0;
        DetailsButton.Opacity = _hovering ? 1 : 0;
        DetailsButton.IsHitTestVisible = _hovering;
    }

    private void OnDragStarting(UIElement sender, DragStartingEventArgs args)
    {
        if (_model is null)
        {
            args.Cancel = true;
            return;
        }

        args.Data.SetText(_model.Id.ToString("D").ToUpperInvariant());
        args.AllowedOperations = DataPackageOperation.Move | DataPackageOperation.Copy;
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (_model is null || !e.DataView.Contains(StandardDataFormats.Text))
        {
            DropLine.Opacity = 0;
            return;
        }

        e.AcceptedOperation = DataPackageOperation.Move;
        e.DragUIOverride.IsCaptionVisible = false;
        e.DragUIOverride.IsGlyphVisible = false;
        DropLine.Opacity = 1;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        DropLine.Opacity = 0;
        var state = _state;
        var model = _model;
        if (state is null || model is null || !e.DataView.Contains(StandardDataFormats.Text)) return;

        var deferral = e.GetDeferral();
        try
        {
            var text = await e.DataView.GetTextAsync();
            var items = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            state.HandleDropBefore(items, model.Id, state.VisibleTasks);
        }
        catch (Exception ex)
        {
            AppLog.Error("Couldn't read what was dropped on a task", ex);
        }
        finally
        {
            deferral.Complete();
        }
    }
}
