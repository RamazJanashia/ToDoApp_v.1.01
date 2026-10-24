using System.Globalization;
using System.Runtime.CompilerServices;
using TodoCore;

namespace TodoCore.Tests;

internal static class TestSupport
{
    // Tests must not depend on the machine's culture or time zone.
    [ModuleInitializer]
    internal static void SetCulture()
    {
        CultureInfo.DefaultThreadCurrentCulture = new CultureInfo("en-US");
        CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo("en-US");
    }

    public static readonly TodoCalendar Berlin =
        new(TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin"), DayOfWeek.Monday);

    /// <summary>A Berlin wall-clock moment as a UTC instant.</summary>
    public static DateTime D(int y, int m, int d, int h = 0, int min = 0) => Berlin.Make(y, m, d, h, min);

    // Friday, October 23, 2026, mid-afternoon.
    public static readonly DateTime Now = D(2026, 10, 23, 15, 30);
    public static readonly DateTime Today = Berlin.StartOfDay(Now);
}
