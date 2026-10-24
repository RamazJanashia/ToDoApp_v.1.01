using TodoCore;

namespace TodoApp.State.Tests;

/// <summary>A state over an empty store, for tests of the presentation models.</summary>
internal sealed class PresentationFixture : IDisposable
{
    private readonly TempDirectory _temp = new();

    public PresentationFixture(IClock? clock = null)
    {
        Clock = clock ?? new FakeClock();
        Store = new TaskStore(_temp.Path, Clock, new ManualDebouncer(), new FakeReminders());
        Store.DeleteTasks(Store.Tasks.Select(t => t.Id).ToHashSet());
        foreach (var project in Store.Projects.ToList()) Store.DeleteProject(project.Id);
        State = new AppState(Store, Clock, new AppSettings(), _ => { });
    }

    public IClock Clock { get; }

    public TaskStore Store { get; }

    public AppState State { get; }

    public TodoCalendar Calendar => Clock.Calendar;

    public DateTime Today => Calendar.StartOfDay(Clock.UtcNow);

    public DateTime Day(int offset) => Calendar.AddDays(Today, offset);

    public void Dispose() => _temp.Dispose();
}

/// <summary>Berlin, but weeks start on Sunday.</summary>
internal sealed class SundayClock : IClock
{
    private static readonly TodoCalendar SundayFirst = new(TestSupport.Berlin.TimeZone, DayOfWeek.Sunday);

    public DateTime UtcNow => TestSupport.Now;

    public TodoCalendar Calendar => SundayFirst;
}
