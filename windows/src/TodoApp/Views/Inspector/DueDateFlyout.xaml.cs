using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TodoApp.State;
using TodoCore;

namespace TodoApp;

/// <summary>
/// The due date flyout, 270 wide: Today, Tomorrow and Next week chips, the <see cref="MiniMonthPicker"/>, a "Time"
/// switch with a time picker, and "Remove due date". It stays open while the user picks, like the Mac popover.
/// </summary>
public sealed partial class DueDateFlyout : UserControl
{
    private AppState? _state;
    private Guid? _taskId;
    private bool _rendering;

    public DueDateFlyout()
    {
        InitializeComponent();
        TodayGlyph.Text = Glyphs.For("sun.max");
        TomorrowGlyph.Text = Glyphs.For("sunrise");
        NextWeekGlyph.Text = Glyphs.For("arrow.right.to.line");

        TimeField.MinuteIncrement = 1;
        TimeField.ClockIdentifier = CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern.Contains('H', StringComparison.Ordinal)
            ? "24HourClock"
            : "12HourClock";

        TodayChip.Click += (_, _) => SetDay(0);
        TomorrowChip.Click += (_, _) => SetDay(1);
        NextWeekChip.Click += (_, _) =>
        {
            if (_state is { } state) Reschedule(state.DayFor(QuickDue.NextWeek));
        };
        Month.DaySelected += (_, day) => Reschedule(day);
        TimeSwitch.Toggled += OnTimeToggled;
        TimeField.SelectedTimeChanged += OnTimeChanged;
    }

    /// <summary>Called as the flyout opens: the calendar goes back to the due date's month, or today's.</summary>
    public void ResetMonth() => Month.ResetMonth();

    public void Render(AppState state, TaskItem task)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(task);
        _state = state;
        _taskId = task.Id;
        var calendar = state.Clock.Calendar;

        _rendering = true;
        try
        {
            Month.Render(task.DueDate, state.Clock.UtcNow, calendar);

            TimeSwitch.IsEnabled = task.DueDate is not null;
            TimeSwitch.IsOn = task.HasDueTime;
            TimeField.Visibility = task.HasDueTime ? Visibility.Visible : Visibility.Collapsed;
            if (task.HasDueTime && task.DueDate is { } due)
            {
                var local = calendar.ToLocal(due);
                var time = new TimeSpan(local.Hour, local.Minute, 0);
                if (TimeField.SelectedTime != time) TimeField.SelectedTime = time;
            }

            RemoveOption.Visibility = task.DueDate is null ? Visibility.Collapsed : Visibility.Visible;
            RemoveOption.Show([OptionItem.Choice("Remove due date", RemoveDueDate, icon: "xmark")]);
        }
        finally
        {
            _rendering = false;
        }
    }

    private void SetDay(int offset)
    {
        if (_state is not { } state) return;
        var calendar = state.Clock.Calendar;
        Reschedule(calendar.AddDays(calendar.StartOfDay(state.Clock.UtcNow), offset));
    }

    /// <summary>Moves the task to <paramref name="day"/>, keeping its time of day if it has one.</summary>
    private void Reschedule(DateTime day)
    {
        if (_state is not { } state || _taskId is not { } id) return;
        var calendar = state.Clock.Calendar;
        state.Store.UpdateTask(id, "Set Due Date", t => t.RescheduledTo(day, calendar));
    }

    private void RemoveDueDate()
    {
        if (_state is not { } state || _taskId is not { } id) return;
        state.Store.UpdateTask(id, "Remove Due Date", t => t.WithoutDueDate());
    }

    /// <summary>Turning the time on starts at 9:00 on the due day; turning it off goes back to the start of the day.</summary>
    private void OnTimeToggled(object sender, RoutedEventArgs e)
    {
        if (_rendering || _state is not { } state || _taskId is not { } id) return;
        var wantsTime = TimeSwitch.IsOn;
        if (state.Store.FindTask(id) is not { } task || task.HasDueTime == wantsTime) return;

        var calendar = state.Clock.Calendar;
        var now = state.Clock.UtcNow;
        state.Store.UpdateTask(id, wantsTime ? "Add Time" : "Remove Time", t =>
        {
            var day = calendar.StartOfDay(t.DueDate ?? now);
            return t with { HasDueTime = wantsTime, DueDate = wantsTime ? calendar.SettingTime(day, 9, 0) : day };
        });
    }

    private void OnTimeChanged(TimePicker sender, TimePickerSelectedValueChangedEventArgs args)
    {
        if (_rendering || _state is not { } state || _taskId is not { } id || args.NewTime is not { } time) return;
        var calendar = state.Clock.Calendar;
        var now = state.Clock.UtcNow;
        state.Store.UpdateTask(id, "Set Due Date", t =>
        {
            var picked = calendar.SettingTime(t.DueDate ?? calendar.StartOfDay(now), time.Hours, time.Minutes);
            return t with { DueDate = t.HasDueTime ? picked : calendar.StartOfDay(picked) };
        });
    }
}
