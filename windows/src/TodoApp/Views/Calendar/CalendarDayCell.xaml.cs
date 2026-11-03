using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using TodoApp.State;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;

namespace TodoApp;

/// <summary>
/// A day cell. A click selects the day, a double-click adds a task on it, and dropping a task reschedules it here.
/// Wide cells list chips (as many as fit, then "+N more"); cells under 100 wide show up to three dots and a count,
/// with the titles in the tooltip, instead of titles that would truncate to "…".
/// </summary>
public sealed partial class CalendarDayCell : UserControl
{
    private const double CompactBelow = 100;

    private AppState? _state;
    private CalendarDayModel? _model;
    private bool _hovering;
    private bool _dropTarget;

    public CalendarDayCell()
    {
        InitializeComponent();

        Root.SizeChanged += (_, e) =>
        {
            // Anything beyond the cell is cut off, like the Mac's clipped().
            Root.Clip = new RectangleGeometry { Rect = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height) };
            Layout();
        };
        ActualThemeChanged += (_, _) => Layout();
        Root.PointerEntered += (_, _) => SetHovering(true);
        Root.PointerExited += (_, _) => SetHovering(false);
        Root.Tapped += (_, _) =>
        {
            if (_state is not null && _model is not null) _state.CalendarSelectedDay = _model.Day;
        };
        Root.DoubleTapped += (_, _) =>
        {
            if (_state is null || _model is null) return;
            _state.CalendarSelectedDay = _model.Day;
            _state.CreateDraftTask(dueDate: _model.Day);
        };
        Root.DragEnter += OnDragOver;
        Root.DragOver += OnDragOver;
        Root.DragLeave += (_, _) => SetDropTarget(false);
        Root.Drop += OnDrop;
    }

    public void Render(AppState state, CalendarDayModel model, bool isLastColumn)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(model);
        _state = state;
        _model = model;

        NumberText.Text = model.DayNumber;
        VisualStateManager.GoToState(this, model.IsToday ? "DayToday" : model.IsInMonth ? "DayInMonth" : "DayOutside", false);
        // The Mac dims the number a little: 0.85 in the month, 0.7 outside it; today is full.
        NumberText.Opacity = model.IsToday ? 1 : model.IsInMonth ? 0.85 : 0.7;
        RightLine.Visibility = isLastColumn ? Visibility.Collapsed : Visibility.Visible;
        UpdateFill();
        Layout();
    }

    /// <summary>Draws the chips, or the dots, for the cell's size. Chips are built new each time.</summary>
    private void Layout()
    {
        Chips.Children.Clear();
        Dots.Children.Clear();
        MoreText.Visibility = Visibility.Collapsed;
        Dots.Visibility = Visibility.Collapsed;
        if (_state is not { } state || _model is not { } model || Root.ActualWidth <= 0) return;

        var compact = Root.ActualWidth < CompactBelow;
        ToolTipService.SetToolTip(Root, compact ? model.CompactTooltip : model.Tooltip);
        // The full date, which the tooltip starts with.
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(Root, model.Tooltip);

        if (compact)
        {
            foreach (var chip in model.Chips.Take(3))
            {
                Dots.Children.Add(new Ellipse { Width = 6, Height = 6, Fill = DotBrushes.For(chip, ActualTheme) });
            }

            if (model.Chips.Count > 3)
            {
                Dots.Children.Add(new TextBlock
                {
                    Text = "+" + (model.Chips.Count - 3).ToString(CultureInfo.CurrentCulture),
                    FontSize = 10,
                    Foreground = DotBrushes.Resource("TertiaryTextBrush", ActualTheme),
                    VerticalAlignment = VerticalAlignment.Center,
                });
            }

            Dots.Visibility = model.Chips.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            return;
        }

        var visible = Math.Min(CalendarModel.MaxVisible(Root.ActualHeight, model.Chips.Count), model.Chips.Count);
        foreach (var chip in model.Chips.Take(visible)) Chips.Children.Add(new CalendarChip(state, chip));
        if (model.Chips.Count > visible)
        {
            MoreText.Text = "+" + (model.Chips.Count - visible).ToString(CultureInfo.CurrentCulture) + " more";
            MoreText.Visibility = Visibility.Visible;
        }
    }

    private void SetHovering(bool hovering)
    {
        _hovering = hovering;
        UpdateFill();
    }

    private void SetDropTarget(bool target)
    {
        _dropTarget = target;
        UpdateFill();
    }

    /// <summary>The drop tint wins over the selection, which wins over hover.</summary>
    private void UpdateFill()
    {
        var selected = _model?.IsSelected == true;
        DropFill.Opacity = _dropTarget ? 0.12 : 0;
        SelectedFill.Opacity = !_dropTarget && selected ? 0.6 : 0;
        HoverFill.Opacity = !_dropTarget && !selected && _hovering ? 0.6 : 0;
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.Text)) return;
        e.AcceptedOperation = DataPackageOperation.Move;
        e.DragUIOverride.IsCaptionVisible = false;
        e.DragUIOverride.IsGlyphVisible = false;
        e.Handled = true;
        SetDropTarget(true);
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        SetDropTarget(false);
        var state = _state;
        var model = _model;
        if (state is null || model is null || !e.DataView.Contains(StandardDataFormats.Text)) return;
        e.Handled = true;

        var deferral = e.GetDeferral();
        try
        {
            var text = await e.DataView.GetTextAsync();
            var items = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            state.HandleDropOnDay(items, model.Day);
        }
        catch (Exception ex)
        {
            AppLog.Error("Couldn't read what was dropped on a day", ex);
        }
        finally
        {
            deferral.Complete();
        }
    }
}
