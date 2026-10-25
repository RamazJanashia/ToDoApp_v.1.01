using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Shapes;
using TodoApp.State;
using TodoCore;

namespace TodoApp;

/// <summary>
/// The month header with ‹ ›, the weekday initials from the first weekday, and a 7×6 grid of days: today in red, the
/// selection as a filled circle, days of other months in tertiary ink, and nothing selected for an undated task.
/// Clicking a day raises <see cref="DaySelected"/>.
/// </summary>
public sealed partial class MiniMonthPicker : UserControl
{
    private const int Columns = 7;
    private const int Rows = 6;

    private readonly List<TextBlock> _weekdayLabels = [];
    private readonly List<DayCell> _cells = [];
    private DateTime? _month;
    private DateTime? _selection;
    private DateTime _now;
    private TodoCalendar? _calendar;

    public MiniMonthPicker()
    {
        InitializeComponent();
        PreviousButton.Content = Glyphs.For("chevron.left");
        NextButton.Content = Glyphs.For("chevron.right");
        PreviousButton.Click += (_, _) => Shift(-1);
        NextButton.Click += (_, _) => Shift(1);
        BuildGrid();
    }

    /// <summary>A day (local midnight, as a UTC instant) was clicked.</summary>
    public event EventHandler<DateTime>? DaySelected;

    /// <summary>The next render shows the selection's month, or today's, as when the Mac popover reopens.</summary>
    public void ResetMonth() => _month = null;

    public void Render(DateTime? selection, DateTime now, TodoCalendar calendar)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        _selection = selection;
        _now = now;
        _calendar = calendar;
        _month ??= selection ?? now;
        Draw();
    }

    /// <summary>The Mac's <c>veryShortWeekdaySymbols</c>: the first letter of the culture's shortest day name (1 = Sunday).</summary>
    internal static string VeryShortWeekdaySymbol(int weekday)
    {
        var name = CultureInfo.CurrentCulture.DateTimeFormat.ShortestDayNames[weekday - 1];
        return name.Length == 0 ? name : StringInfo.GetNextTextElement(name);
    }

    private void BuildGrid()
    {
        var weekdayStyle = (Style)Resources["WeekdayTextStyle"];
        var dayStyle = (Style)Resources["DayButtonStyle"];
        var discStyle = (Style)Resources["SelectedDiscStyle"];

        for (var column = 0; column < Columns; column++)
        {
            WeekdayRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            DaysGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var label = new TextBlock { Style = weekdayStyle };
            Grid.SetColumn(label, column);
            WeekdayRow.Children.Add(label);
            _weekdayLabels.Add(label);
        }

        for (var row = 0; row < Rows; row++)
        {
            DaysGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        for (var index = 0; index < Columns * Rows; index++)
        {
            var disc = new Ellipse { Style = discStyle, Visibility = Visibility.Collapsed };
            var number = new TextBlock();
            Microsoft.UI.Xaml.Documents.Typography.SetNumeralAlignment(number, FontNumeralAlignment.Tabular);
            var content = new Grid();
            content.Children.Add(disc);
            content.Children.Add(number);

            var button = new Button { Style = dayStyle, Content = content };
            var cell = new DayCell(button, disc, number);
            button.Click += (_, _) => DaySelected?.Invoke(this, cell.Day);
            Grid.SetRow(button, index / Columns);
            Grid.SetColumn(button, index % Columns);
            DaysGrid.Children.Add(button);
            _cells.Add(cell);
        }
    }

    private void Shift(int months)
    {
        if (_calendar is not { } calendar || _month is not { } month) return;
        _month = calendar.AddMonths(calendar.StartOfMonth(month), months);
        Draw();
    }

    private void Draw()
    {
        if (_calendar is not { } calendar || _month is not { } month) return;
        var culture = CultureInfo.CurrentCulture;
        MonthTitle.Text = calendar.ToLocal(calendar.StartOfMonth(month)).ToString(culture.DateTimeFormat.YearMonthPattern, culture);

        for (var column = 0; column < Columns; column++)
        {
            _weekdayLabels[column].Text = VeryShortWeekdaySymbol((calendar.FirstWeekday - 1 + column) % 7 + 1);
        }

        var days = MonthGrid.Days(month, calendar);
        for (var index = 0; index < _cells.Count && index < days.Count; index++)
        {
            var day = days[index];
            var cell = _cells[index];
            var local = calendar.ToLocal(day);
            var isSelected = _selection is { } selection && calendar.IsSameDay(selection, day);
            var isToday = calendar.IsSameDay(day, _now);
            var isInMonth = calendar.IsSameMonth(day, month);

            cell.Day = day;
            cell.Number.Text = local.Day.ToString(culture);
            var style = (Style)Resources[
                isSelected ? "SelectedDayTextStyle"
                : isToday ? "TodayTextStyle"
                : isInMonth ? "DayTextStyle"
                : "OutsideDayTextStyle"];
            if (cell.Number.Style != style) cell.Number.Style = style;
            cell.Disc.Visibility = isSelected ? Visibility.Visible : Visibility.Collapsed;

            var label = local.ToString("D", culture);
            ToolTipService.SetToolTip(cell.Button, label);
            AutomationProperties.SetName(cell.Button, label);
        }
    }

    private sealed class DayCell(Button button, Ellipse disc, TextBlock number)
    {
        public Button Button { get; } = button;

        public Ellipse Disc { get; } = disc;

        public TextBlock Number { get; } = number;

        public DateTime Day { get; set; }
    }
}
