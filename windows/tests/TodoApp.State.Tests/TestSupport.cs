using System.Globalization;
using System.Runtime.CompilerServices;
using TodoCore;

namespace TodoApp.State.Tests;

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
}

internal sealed class FakeClock : IClock
{
    public DateTime UtcNow { get; set; } = TestSupport.Now;

    public TodoCalendar Calendar => TestSupport.Berlin;
}

internal sealed class ManualDebouncer : IDebouncer
{
    private Action? _pending;

    public bool HasPending => _pending is not null;

    public void Schedule(Action action) => _pending = action;

    public void Cancel() => _pending = null;

    /// <summary>Runs the pending action, if any, as the timer would.</summary>
    public void Flush()
    {
        var action = _pending;
        _pending = null;
        action?.Invoke();
    }
}

internal sealed class FakeReminders : IReminderScheduler
{
    public List<string> Calls { get; } = new();

    public List<(IReadOnlyList<TaskItem> Tasks, IReadOnlyList<Project> Projects)> Syncs { get; } = new();

    public bool AreNotificationsBlocked { get; set; }

    public event EventHandler<Guid>? OpenTaskRequested
    {
        add { }
        remove { }
    }

    public void Activate() => Calls.Add("Activate");

    public void Sync(IReadOnlyList<TaskItem> tasks, IReadOnlyList<Project> projects)
    {
        Calls.Add("Sync");
        Syncs.Add((tasks, projects));
    }
}

/// <summary>A fresh directory that is deleted, read-only parts included, when disposed.</summary>
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TodoApp.State.Tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        if (!Directory.Exists(Path)) return;
        foreach (var entry in new DirectoryInfo(Path).EnumerateFileSystemInfos("*", SearchOption.AllDirectories))
        {
            entry.Attributes = FileAttributes.Normal;
        }

        Directory.Delete(Path, recursive: true);
    }
}
