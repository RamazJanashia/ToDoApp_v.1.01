using TodoCore;
using static TodoCore.Tests.TestSupport;

namespace TodoCore.Tests;

public class MonthGridTests
{
    [Fact]
    public void SixWeeksStartingMonday28Sep()
    {
        var grid = MonthGrid.Days(D(2026, 10, 15), Berlin);
        Assert.Equal(42, grid.Count);
        Assert.Equal(D(2026, 9, 28), grid[0]);
        Assert.Equal(D(2026, 11, 8), grid[^1]);
    }

    [Fact]
    public void SundayFirst_StartsOnSunday()
    {
        var sundayFirst = new TodoCalendar(Berlin.TimeZone, DayOfWeek.Sunday);
        var grid = MonthGrid.Days(D(2026, 10, 15), sundayFirst);
        Assert.Equal(D(2026, 9, 27), grid[0]);
        Assert.Equal(D(2026, 11, 7), grid[^1]);
    }
}
