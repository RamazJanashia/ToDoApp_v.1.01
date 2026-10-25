using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using TodoApp.State;
using TodoCore;

namespace TodoApp;

/// <summary>
/// The repeat flyout, 260 wide: "Doesn’t repeat", the presets and "Custom…". The custom editor (a frequency bar, "Every"
/// with a 1–99 number and the unit, round weekday toggles for weekly rules, and the summary) opens from "Custom…" and
/// is always shown for a rule no preset matches. Choosing a rule for an undated task dates it today, as it needs a day
/// to count from. It stays open while the user picks, like the Mac popover.
/// </summary>
public sealed partial class RepeatFlyout : UserControl
{
    private readonly SelectorBarItem[] _frequencyItems;
    private readonly List<(ToggleButton Button, int Weekday)> _weekdays = [];
    private int _weekdaysFirst;
    private AppState? _state;
    private TaskItem? _task;
    private bool _customizing;
    private bool _rendering;

    public RepeatFlyout()
    {
        InitializeComponent();
        // In RecurrenceFrequency order.
        _frequencyItems = [DailyItem, WeeklyItem, MonthlyItem, YearlyItem];
        FrequencyBar.SelectionChanged += OnFrequencyChanged;
        IntervalBox.ValueChanged += OnIntervalChanged;
    }

    /// <summary>Called as the flyout opens: the custom editor starts closed unless the rule is already custom.</summary>
    public void ResetCustomizing() => _customizing = false;

    public void Render(AppState state, TaskItem task)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(task);
        _state = state;
        _task = task;
        var rule = task.Recurrence;

        _rendering = true;
        try
        {
            var items = new List<OptionItem>
            {
                OptionItem.Choice("Doesn’t repeat", () => Apply(null), icon: "nosign", isSelected: rule is null),
                OptionItem.Separator(),
            };
            foreach (var (title, preset) in RecurrenceRule.Presets)
            {
                items.Add(OptionItem.Choice(title, () => Apply(preset), isSelected: rule == preset));
            }

            items.Add(OptionItem.Separator());
            items.Add(OptionItem.Choice("Custom…", ToggleCustomizing, icon: "slider.horizontal.3", isSelected: IsCustom(rule)));
            Options.Show(items);

            var showsEditor = _customizing || IsCustom(rule);
            Editor.Visibility = showsEditor ? Visibility.Visible : Visibility.Collapsed;
            if (showsEditor) ShowEditor(rule ?? RecurrenceRule.Weekly, state.Clock.Calendar.FirstWeekday);
        }
        finally
        {
            _rendering = false;
        }
    }

    private static bool IsCustom(RecurrenceRule? rule) =>
        rule is not null && !RecurrenceRule.Presets.Any(p => p.Rule == rule);

    private void ShowEditor(RecurrenceRule rule, int firstWeekday)
    {
        var frequencyItem = _frequencyItems[(int)rule.Frequency];
        if (FrequencyBar.SelectedItem != frequencyItem) FrequencyBar.SelectedItem = frequencyItem;
        if (IntervalBox.Value != rule.Interval) IntervalBox.Value = rule.Interval;
        UnitText.Text = rule.Frequency.Unit(rule.Interval != 1);

        var weekly = rule.Frequency == RecurrenceFrequency.Weekly;
        WeekdayRow.Visibility = weekly ? Visibility.Visible : Visibility.Collapsed;
        if (weekly)
        {
            if (_weekdays.Count == 0 || firstWeekday != _weekdaysFirst) BuildWeekdays(firstWeekday);
            foreach (var (button, weekday) in _weekdays) button.IsChecked = rule.Weekdays.Contains(weekday);
        }

        SummaryText.Text = rule.Summary;
    }

    private void BuildWeekdays(int firstWeekday)
    {
        _weekdaysFirst = firstWeekday;
        _weekdays.Clear();
        WeekdayRow.Children.Clear();
        var style = (Style)Resources["WeekdayToggleStyle"];
        var names = CultureInfo.CurrentCulture.DateTimeFormat.DayNames;
        for (var offset = 0; offset < 7; offset++)
        {
            var weekday = (firstWeekday - 1 + offset) % 7 + 1;
            var button = new ToggleButton { Style = style, Content = MiniMonthPicker.VeryShortWeekdaySymbol(weekday) };
            ToolTipService.SetToolTip(button, names[weekday - 1]);
            AutomationProperties.SetName(button, names[weekday - 1]);
            button.Click += (_, _) => ToggleWeekday(weekday);
            _weekdays.Add((button, weekday));
            WeekdayRow.Children.Add(button);
        }
    }

    /// <summary>The rule being edited, read fresh from the store.</summary>
    private RecurrenceRule CurrentRule() =>
        (_task is { } task ? _state?.Store.FindTask(task.Id) ?? task : null)?.Recurrence ?? RecurrenceRule.Weekly;

    private void ToggleCustomizing()
    {
        _customizing = !_customizing;
        if (_state is not { } state || _task is not { } task) return;
        var current = state.Store.FindTask(task.Id) ?? task;
        if (current.Recurrence is null) Apply(RecurrenceRule.Weekly);
        else Render(state, current);
    }

    private void OnFrequencyChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (_rendering || sender.SelectedItem is not { } selected) return;
        var index = Array.IndexOf(_frequencyItems, selected);
        if (index < 0) return;
        var frequency = (RecurrenceFrequency)index;
        var rule = CurrentRule();
        if (frequency == rule.Frequency) return;
        Apply(RecurrenceRule.Create(frequency, rule.Interval, frequency == RecurrenceFrequency.Weekly ? rule.Weekdays : null));
    }

    private void OnIntervalChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_rendering) return;
        var rule = CurrentRule();
        if (double.IsNaN(args.NewValue))
        {
            sender.Value = rule.Interval;
            return;
        }

        var interval = (int)Math.Clamp(Math.Round(args.NewValue), 1, 99);
        if (interval == rule.Interval) return;
        Apply(RecurrenceRule.Create(rule.Frequency, interval, rule.Weekdays));
    }

    private void ToggleWeekday(int weekday)
    {
        var rule = CurrentRule();
        var weekdays = rule.Weekdays.ToHashSet();
        if (!weekdays.Remove(weekday)) weekdays.Add(weekday);
        Apply(RecurrenceRule.Create(RecurrenceFrequency.Weekly, rule.Interval, weekdays));
    }

    private void Apply(RecurrenceRule? rule)
    {
        if (_state is not { } state || _task is not { } task) return;
        var calendar = state.Clock.Calendar;
        var now = state.Clock.UtcNow;
        state.Store.UpdateTask(task.Id, rule is null ? "Stop Repeating" : "Set Repeat", t => t with
        {
            Recurrence = rule,
            // A repeating task needs an anchor date to count from.
            DueDate = rule is not null && t.DueDate is null ? calendar.StartOfDay(now) : t.DueDate,
        });
    }
}
