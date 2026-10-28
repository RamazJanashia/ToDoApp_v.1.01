using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TodoApp.State;

namespace TodoApp;

/// <summary>
/// A month grid of scheduled tasks. Click a day to select it, double-click to add a task, drag tasks between days to
/// reschedule. The 42 day cells are created once and updated in place.
/// </summary>
public sealed partial class CalendarMonthView : UserControl
{
    private const int Weeks = 6;
    private const int Columns = 7;

    private readonly TextBlock[] _weekdays = new TextBlock[Columns];
    private readonly CalendarDayCell[] _cells = new CalendarDayCell[Weeks * Columns];
    private AppState? _state;

    public CalendarMonthView()
    {
        InitializeComponent();
        PreviousButton.Content = Glyphs.For("chevron.left");
        NextButton.Content = Glyphs.For("chevron.right");

        for (var column = 0; column < Columns; column++)
        {
            var label = new TextBlock
            {
                FontSize = 10.5,
                FontWeight = Microsoft.UI.Text.FontWeights.Medium,
                CharacterSpacing = 80,
                Margin = new Thickness(8, 0, 0, 0),
                Style = (Style)Application.Current.Resources["AppTextBase"],
            };
            Grid.SetColumn(label, column);
            WeekdayRow.Children.Add(label);
            _weekdays[column] = label;
        }

        for (var row = 0; row < Weeks; row++) Cells.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        for (var column = 0; column < Columns; column++) Cells.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < _cells.Length; i++)
        {
            var cell = new CalendarDayCell();
            Grid.SetRow(cell, i / Columns);
            Grid.SetColumn(cell, i % Columns);
            Cells.Children.Add(cell);
            _cells[i] = cell;
        }

        ActualThemeChanged += (_, _) =>
        {
            if (_state is not null) Render(_state);
        };
        PreviousButton.Click += (_, _) => ShiftMonth(-1);
        NextButton.Click += (_, _) => ShiftMonth(1);
        TodayButton.Click += (_, _) =>
        {
            if (_state is not { } state) return;
            var now = state.Clock.UtcNow;
            state.CalendarMonth = now;
            state.CalendarSelectedDay = state.Clock.Calendar.StartOfDay(now);
        };
    }

    public void Render(AppState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _state = state;
        var model = CalendarModel.Build(state);

        MonthTitle.Text = model.MonthTitle;
        for (var column = 0; column < Columns && column < model.WeekdaySymbols.Count; column++)
        {
            _weekdays[column].Text = model.WeekdaySymbols[column];
            _weekdays[column].Foreground = DotBrushes.Resource("TertiaryTextBrush", ActualTheme);
        }

        for (var i = 0; i < _cells.Length && i < model.Days.Count; i++)
        {
            _cells[i].Render(state, model.Days[i], isLastColumn: i % Columns == Columns - 1);
        }
    }

    private void ShiftMonth(int delta)
    {
        if (_state is not { } state) return;
        var calendar = state.Clock.Calendar;
        state.CalendarMonth = calendar.AddMonths(calendar.StartOfMonth(state.CalendarMonth), delta);
    }
}
