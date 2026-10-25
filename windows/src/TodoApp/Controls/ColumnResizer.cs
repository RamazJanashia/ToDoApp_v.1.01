using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace TodoApp;

/// <summary>
/// An invisible, wide grab area on a column edge that resizes the neighbouring column (WindowChrome.swift).
/// It sits in a 1 px grid column and reaches 4 px into each neighbour, so the hit area is 9 px wide.
/// </summary>
public sealed partial class ColumnResizer : Grid
{
    private const double HitWidth = 9;

    public static readonly DependencyProperty LineBrushProperty = DependencyProperty.Register(
        nameof(LineBrush),
        typeof(Brush),
        typeof(ColumnResizer),
        new PropertyMetadata(null, (d, e) => ((ColumnResizer)d)._line.Fill = (Brush?)e.NewValue));

    private readonly Rectangle _line = new() { Width = 1, HorizontalAlignment = HorizontalAlignment.Center };
    private double _verticalInset;
    private double? _dragStartWidth;
    private double _dragStartX;

    public ColumnResizer()
    {
        Width = HitWidth;
        // Transparent rather than null, so the whole area takes the pointer.
        Background = new SolidColorBrush(Colors.Transparent);
        Children.Add(_line);
        UpdateMargin();
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);

        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerCaptureLost += (_, _) => _dragStartWidth = null;
    }

    /// <summary>The 1 px divider line; null draws none (the sidebar edge).</summary>
    public Brush? LineBrush
    {
        get => (Brush?)GetValue(LineBrushProperty);
        set => SetValue(LineBrushProperty, value);
    }

    /// <summary>+1 when dragging right widens the column (sidebar), -1 when it narrows it (inspector).</summary>
    public double Direction { get; set; } = 1;

    /// <summary>Space kept clear above and below the line and the grab area.</summary>
    public double VerticalInset
    {
        get => _verticalInset;
        set
        {
            _verticalInset = value;
            UpdateMargin();
        }
    }

    public (double Min, double Max) Range { get; set; } = (0, double.MaxValue);

    /// <summary>The column's width as shown, read when a drag starts.</summary>
    public Func<double>? CurrentWidth { get; set; }

    /// <summary>Raised while dragging, with the new width already clamped to <see cref="Range"/>.</summary>
    public event EventHandler<double>? WidthDragged;

    private void UpdateMargin()
    {
        var reach = (HitWidth - 1) / 2;
        Margin = new Thickness(-reach, _verticalInset, -reach, _verticalInset);
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (CurrentWidth is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (!CapturePointer(e.Pointer)) return;
        _dragStartWidth = CurrentWidth();
        // Window coordinates, so the reference point doesn't move with the edge.
        _dragStartX = e.GetCurrentPoint(null).Position.X;
        e.Handled = true;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_dragStartWidth is not { } start) return;
        var point = e.GetCurrentPoint(null);
        // The button came up where we didn't see it (say, over another window): the drag is over.
        if (!point.Properties.IsLeftButtonPressed)
        {
            _dragStartWidth = null;
            ReleasePointerCapture(e.Pointer);
            return;
        }

        var proposed = start + (point.Position.X - _dragStartX) * Direction;
        WidthDragged?.Invoke(this, Math.Clamp(proposed, Range.Min, Range.Max));
        e.Handled = true;
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_dragStartWidth is null) return;
        _dragStartWidth = null;
        ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }
}
