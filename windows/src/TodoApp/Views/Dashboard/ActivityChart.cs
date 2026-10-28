using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using TodoCore;
using Windows.Foundation;

namespace TodoApp;

/// <summary>
/// Completions per day (ActivityChart in DashboardView.swift): one quiet series, drawn from rectangles. The y-domain is
/// 0…max(peak + 2, 4) with about three gridlines and labels on the left; the x labels give the day number every second
/// day. Bars use half of their slot, today's is brighter and the hovered one brighter still, with a card above it
/// ("N completed", "Fri, Oct 23").
/// </summary>
public sealed partial class ActivityChart : Grid
{
    private const double AxisWidth = 28;
    private const double LabelsHeight = 22;

    private readonly Canvas _axis = new();
    private readonly Canvas _plot = new() { Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
    private readonly Canvas _labels = new();
    private IReadOnlyList<DayCount> _days = [];
    private TodoCalendar? _calendar;
    private DateTime _now;
    private int? _hovered;

    public ActivityChart()
    {
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(AxisWidth) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(LabelsHeight) });
        SetColumn(_plot, 1);
        SetColumn(_labels, 1);
        SetRow(_labels, 1);
        Children.Add(_axis);
        Children.Add(_plot);
        Children.Add(_labels);
        AutomationProperties.SetName(this, "Tasks completed per day over the last 14 days");

        _plot.SizeChanged += (_, _) => Draw();
        _plot.PointerMoved += OnPointerMoved;
        _plot.PointerExited += (_, _) => SetHovered(null);
        ActualThemeChanged += (_, _) => Draw();
    }

    public void Render(IReadOnlyList<DayCount> days, TodoCalendar calendar, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(days);
        ArgumentNullException.ThrowIfNull(calendar);
        _days = days;
        _calendar = calendar;
        _now = now;
        if (_hovered >= days.Count) _hovered = null;
        Draw();
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_days.Count == 0 || _plot.ActualWidth <= 0) return;
        var x = e.GetCurrentPoint(_plot).Position.X;
        SetHovered(Math.Clamp((int)(x / (_plot.ActualWidth / _days.Count)), 0, _days.Count - 1));
    }

    private void SetHovered(int? index)
    {
        if (_hovered == index) return;
        _hovered = index;
        Draw();
    }

    private Brush Ink(string key) => DotBrushes.Resource(key, ActualTheme);

    private void Draw()
    {
        _axis.Children.Clear();
        _plot.Children.Clear();
        _labels.Children.Clear();
        var width = _plot.ActualWidth;
        var height = _plot.ActualHeight;
        if (_calendar is not { } calendar || _days.Count == 0 || width <= 0 || height <= 0) return;

        var culture = CultureInfo.CurrentCulture;
        var peak = _days.Max(d => d.Count);
        var domain = Math.Max(peak + 2, 4);

        // About three gridlines: 0 and every "nice" step up to the top.
        var step = NiceStep(domain / 2.0);
        for (var value = 0; value <= domain; value += step)
        {
            var y = height - value * height / domain;
            var line = new Rectangle { Width = width, Height = 1, Fill = Ink("SeparatorBrush") };
            Canvas.SetTop(line, Math.Min(y, height - 1));
            _plot.Children.Add(line);

            var label = new TextBlock
            {
                Text = value.ToString(culture),
                FontSize = 10.5,
                Width = AxisWidth - 6,
                TextAlignment = TextAlignment.Right,
                Foreground = Ink("TertiaryTextBrush"),
            };
            Canvas.SetTop(label, y - 8);
            _axis.Children.Add(label);
        }

        var slot = width / _days.Count;
        var barWidth = slot * 0.5;
        for (var i = 0; i < _days.Count; i++)
        {
            var day = _days[i];
            var barHeight = day.Count * height / domain;
            var brush = _hovered == i ? Ink("ChartHoverBrush")
                : calendar.IsSameDay(day.Day, _now) ? Ink("ChartTodayBrush")
                : Ink("ChartBarBrush");
            if (barHeight > 0)
            {
                var bar = new Rectangle { Width = barWidth, Height = barHeight, RadiusX = 4, RadiusY = 4, Fill = brush };
                Canvas.SetLeft(bar, i * slot + (slot - barWidth) / 2);
                Canvas.SetTop(bar, height - barHeight);
                _plot.Children.Add(bar);
            }

            if (i % 2 == 0)
            {
                var number = new TextBlock
                {
                    Text = calendar.ToLocal(day.Day).Day.ToString(culture),
                    FontSize = 10.5,
                    Width = slot,
                    TextAlignment = TextAlignment.Center,
                    Foreground = Ink("TertiaryTextBrush"),
                };
                Canvas.SetLeft(number, i * slot);
                Canvas.SetTop(number, 4);
                _labels.Children.Add(number);
            }
        }

        if (_hovered is { } hovered) DrawCard(calendar, hovered, slot, height, domain, width, culture);
    }

    private void DrawCard(TodoCalendar calendar, int index, double slot, double height, int domain, double width, CultureInfo culture)
    {
        var day = _days[index];
        var card = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 5, 8, 5),
            BorderThickness = new Thickness(1),
            BorderBrush = Ink("SeparatorBrush"),
            Background = Ink("RaisedBrush"),
            IsHitTestVisible = false,
            Child = new StackPanel
            {
                Spacing = 2,
                Children =
                {
                    new TextBlock
                    {
                        Text = day.Count.ToString(culture) + " completed",
                        FontSize = 11,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        Foreground = Ink("PrimaryTextBrush"),
                    },
                    new TextBlock
                    {
                        Text = calendar.ToLocal(day.Day).ToString("ddd, MMM d", culture),
                        FontSize = 10.5,
                        Foreground = Ink("SecondaryTextBrush"),
                    },
                },
            },
        };
        card.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = card.DesiredSize;
        var barTop = height - day.Count * height / domain;
        Canvas.SetLeft(card, Math.Clamp(index * slot + slot / 2 - size.Width / 2, 0, Math.Max(width - size.Width, 0)));
        Canvas.SetTop(card, barTop - 6 - size.Height);
        _plot.Children.Add(card);
    }

    /// <summary>The smallest of 1, 2, 5, 10, 20, 50… that is at least <paramref name="raw"/>.</summary>
    private static int NiceStep(double raw)
    {
        var magnitude = 1;
        while (magnitude * 10 <= raw) magnitude *= 10;
        foreach (var factor in new[] { 1, 2, 5, 10 })
        {
            if (factor * magnitude >= raw) return factor * magnitude;
        }

        return 10 * magnitude;
    }
}
