using TodoCore;
using static TodoCore.Tests.TestSupport;

namespace TodoCore.Tests;

public class RecurrenceTests
{
    private static DateTime Next(RecurrenceRule rule, DateTime after) => rule.NextDate(after, null, Berlin);

    [Fact]
    public void DailyAddsOneDay() =>
        Assert.Equal(D(2026, 10, 24), Next(RecurrenceRule.Daily, D(2026, 10, 23)));

    [Fact]
    public void EveryThreeDays() =>
        Assert.Equal(D(2026, 10, 26), Next(RecurrenceRule.Create(RecurrenceFrequency.Daily, 3), D(2026, 10, 23)));

    [Fact]
    public void WeeklyAddsSevenDays() =>
        Assert.Equal(D(2026, 10, 30), Next(RecurrenceRule.Weekly, D(2026, 10, 23)));

    [Fact]
    public void WeekdaysSkipTheWeekend() =>
        Assert.Equal(D(2026, 10, 26), Next(RecurrenceRule.EveryWeekday, D(2026, 10, 23)));

    [Fact]
    public void WeekdaysMidWeek() =>
        Assert.Equal(D(2026, 10, 21), Next(RecurrenceRule.EveryWeekday, D(2026, 10, 20)));

    [Fact]
    public void MonWedFri_FromMonday() =>
        Assert.Equal(D(2026, 10, 21), Next(RecurrenceRule.Create(RecurrenceFrequency.Weekly, 1, new[] { 2, 4, 6 }), D(2026, 10, 19)));

    [Fact]
    public void MonWedFri_FromFriday() =>
        Assert.Equal(D(2026, 10, 26), Next(RecurrenceRule.Create(RecurrenceFrequency.Weekly, 1, new[] { 2, 4, 6 }), D(2026, 10, 23)));

    [Fact]
    public void EveryTwoWeeksOnMonday_SkipsAWeek() =>
        Assert.Equal(D(2026, 11, 2), Next(RecurrenceRule.Create(RecurrenceFrequency.Weekly, 2, new[] { 2 }), D(2026, 10, 19)));

    [Fact]
    public void MonthlyClampsToMonthEnd() =>
        Assert.Equal(D(2026, 2, 28), Next(RecurrenceRule.Monthly, D(2026, 1, 31)));

    [Fact]
    public void Yearly() =>
        Assert.Equal(D(2027, 10, 23), Next(RecurrenceRule.Yearly, D(2026, 10, 23)));

    [Fact]
    public void WeekdaySummary() =>
        Assert.Equal("Every weekday", RecurrenceRule.EveryWeekday.Summary);

    [Fact]
    public void IntervalSummary() =>
        Assert.Equal("Every 3 months", RecurrenceRule.Create(RecurrenceFrequency.Monthly, 3).Summary);

    [Fact]
    public void IntervalFloorIsOne() =>
        Assert.Equal(1, RecurrenceRule.Create(RecurrenceFrequency.Daily, 0).Interval);

    [Fact]
    public void Weekly_AcrossDst_KeepsWallClock() =>
        Assert.Equal(D(2026, 10, 26, 9), Next(RecurrenceRule.Weekly, D(2026, 10, 19, 9)));

    [Fact]
    public void Create_DedupesSortsAndFiltersWeekdays()
    {
        var rule = RecurrenceRule.Create(RecurrenceFrequency.Weekly, 1, new[] { 6, 2, 2, 0, 8, 4 });
        Assert.Equal(new[] { 2, 4, 6 }, rule.Weekdays.ToArray());
    }

    [Fact]
    public void Presets_HaveMacTitles() =>
        Assert.Equal(
            new[] { "Every Day", "Every Weekday", "Every Week", "Every Month", "Every Year" },
            RecurrenceRule.Presets.Select(p => p.Title).ToArray());

    [Fact]
    public void Summary_WeeklyWithDaysUsesAbbreviatedNames() =>
        Assert.Equal("Every 2 weeks on Mon, Wed", RecurrenceRule.Create(RecurrenceFrequency.Weekly, 2, new[] { 4, 2 }).Summary);
}
