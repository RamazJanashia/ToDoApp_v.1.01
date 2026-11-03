using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using TodoApp.State;
using TodoCore;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI.Text;
using TaskStatus = TodoCore.TaskStatus;

namespace TodoApp;

/// <summary>
/// A task on the board: check circle, title (three lines), the flag or an ⓘ on hover, and the meta line. A click
/// selects, a double-click opens the details, a drag moves it, and dropping another card here puts that card in front
/// (and in this card's column). Right-click opens <see cref="TaskContextMenu"/>.
/// </summary>
public sealed partial class BoardCard : UserControl
{
    private AppState? _state;
    private TaskRowModel? _model;
    private TaskStatus _status;
    private Func<IReadOnlyList<TaskItem>> _columnTasks = () => [];
    private bool _hovering;

    public BoardCard()
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
        DetailsButton.Tapped += (_, e) => e.Handled = true;
        DetailsButton.DoubleTapped += (_, e) => e.Handled = true;

        Root.Tapped += (_, _) =>
        {
            if (_state is not null && _model is not null) _state.HandleClick(_model.Id, isDoubleClick: false);
        };
        Root.DoubleTapped += (_, _) =>
        {
            if (_state is not null && _model is not null) _state.HandleClick(_model.Id, isDoubleClick: true);
        };
        Root.PointerEntered += (_, _) => SetHovering(true);
        Root.PointerExited += (_, _) => SetHovering(false);
        Root.DragStarting += OnDragStarting;
        Root.DragEnter += OnDragOver;
        Root.DragOver += OnDragOver;
        Root.DragLeave += (_, _) => DropLine.Opacity = 0;
        Root.Drop += OnDrop;
    }

    public Guid TaskId => _model?.Id ?? Guid.Empty;

    /// <param name="columnTasks">The tasks of this card's column in order, which a drop in front of this card needs.</param>
    public void Bind(AppState state, TaskRowModel model, TaskStatus status, Func<IReadOnlyList<TaskItem>> columnTasks)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(columnTasks);

        if (_model?.Id != model.Id || _state != state)
        {
            _hovering = false;
            Root.ContextFlyout = TaskContextMenu.Build(model.Task, state);
        }

        _state = state;
        _model = model;
        _status = status;
        _columnTasks = columnTasks;

        TitleText.Text = model.Title;
        TitleText.TextDecorations = model.IsCompleted ? TextDecorations.Strikethrough : TextDecorations.None;
        VisualStateManager.GoToState(this, model.IsCompleted || model.IsUntitled ? "TitleDimmed" : "TitleNormal", false);
        Check.IsDone = model.IsCompleted;
        Meta.Show(model.Meta);
        SubtaskProgress.Visibility = model.Progress is null ? Visibility.Collapsed : Visibility.Visible;
        SubtaskProgress.Fraction = model.Progress ?? 0;
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

    private void UpdateHighlight()
    {
        var selected = _model?.IsSelected == true;
        VisualStateManager.GoToState(this, selected ? "CardSelected" : _hovering ? "CardHover" : "CardNormal", false);
        DetailsButton.Visibility = _hovering ? Visibility.Visible : Visibility.Collapsed;
        Flag.Visibility = !_hovering && _model?.ShowsPriority != false ? Visibility.Visible : Visibility.Collapsed;
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
        // The card, not the column behind it, is the drop target.
        e.Handled = true;
        DropLine.Opacity = 1;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        DropLine.Opacity = 0;
        var state = _state;
        var model = _model;
        if (state is null || model is null || !e.DataView.Contains(StandardDataFormats.Text)) return;
        e.Handled = true;

        var deferral = e.GetDeferral();
        try
        {
            var text = await e.DataView.GetTextAsync();
            var items = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            state.HandleDropOnStatus(items, _status, model.Id, _columnTasks());
        }
        catch (Exception ex)
        {
            AppLog.Error("Couldn't read what was dropped on a card", ex);
        }
        finally
        {
            deferral.Complete();
        }
    }
}
