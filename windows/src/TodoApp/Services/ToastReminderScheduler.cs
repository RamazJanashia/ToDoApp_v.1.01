using System.Runtime.InteropServices;
using Microsoft.Toolkit.Uwp.Notifications;
using Microsoft.UI.Dispatching;
using TodoApp.State;
using TodoCore;
using Windows.UI.Notifications;

namespace TodoApp;

/// <summary>
/// Reminders as Windows notifications (ReminderScheduler.swift): each one is a scheduled toast that Windows shows at its
/// time whether or not the app is running. A toast carries its task as <c>taskID</c>; a click starts the app, or reaches
/// the running copy, and raises <see cref="OpenTaskRequested"/> on the UI thread. Toasts are tagged
/// <c>task-reminder-{GUID}</c> so this app's can be told from anything else.
/// </summary>
public sealed class ToastReminderScheduler : IReminderScheduler
{
    private const string TagPrefix = "task-reminder-";
    private const string TaskArgument = "taskID";

    private readonly IClock _clock;
    private readonly DispatcherQueue _queue;
    private string? _fingerprint;

    public ToastReminderScheduler(IClock clock, DispatcherQueue queue)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(queue);
        _clock = clock;
        _queue = queue;
        // Must be set before the app has finished starting: a click that launched the app is delivered to it.
        ToastNotificationManagerCompat.OnActivated += OnActivated;
    }

    public event EventHandler<Guid>? OpenTaskRequested;

    /// <summary>Notifications turned off for the app or for the user, or by policy. A failure to ask counts as allowed.</summary>
    public bool AreNotificationsBlocked
    {
        get
        {
            try
            {
                return ToastNotificationManagerCompat.CreateToastNotifier().Setting != NotificationSetting.Enabled;
            }
            catch (Exception ex) when (ex is COMException or InvalidOperationException)
            {
                AppLog.Error("Couldn't read the notification setting", ex);
                return false;
            }
        }
    }

    /// <summary>Clears reminders left from a previous run; <see cref="Sync"/> schedules the current ones.</summary>
    public void Activate()
    {
        _fingerprint = null;
        try
        {
            var notifier = ToastNotificationManagerCompat.CreateToastNotifier();
            foreach (var scheduled in Scheduled(notifier)) notifier.RemoveFromSchedule(scheduled);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            AppLog.Error("Couldn't clear old reminders", ex);
        }
    }

    public void Sync(IReadOnlyList<TaskItem> tasks, IReadOnlyList<Project> projects)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(projects);
        var plan = ReminderPlanner.Plan(tasks, projects, _clock.UtcNow, _clock.Calendar);

        // Most saves change nothing a reminder shows: leave Windows alone then.
        var fingerprint = string.Join('\n', plan.Select(p => $"{p.TaskId:N}|{p.FireAt:O}|{p.Title}|{p.Body}"));
        if (fingerprint == _fingerprint) return;

        try
        {
            var notifier = ToastNotificationManagerCompat.CreateToastNotifier();
            foreach (var stale in Scheduled(notifier)) notifier.RemoveFromSchedule(stale);
            foreach (var reminder in plan) Schedule(notifier, reminder);
            _fingerprint = fingerprint;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            // Try again at the next change; the app carries on without reminders meanwhile.
            AppLog.Error("Couldn't schedule reminders", ex);
        }
    }

    private static IEnumerable<ScheduledToastNotification> Scheduled(ToastNotifierCompat notifier) =>
        notifier.GetScheduledToastNotifications().Where(n => n.Tag is { } tag && tag.StartsWith(TagPrefix, StringComparison.Ordinal));

    private static void Schedule(ToastNotifierCompat notifier, PlannedReminder reminder)
    {
        var builder = new ToastContentBuilder()
            .AddArgument(TaskArgument, reminder.TaskId.ToString("D"))
            .AddText(reminder.Title);
        if (reminder.Body.Length > 0) builder.AddText(reminder.Body);

        var toast = new ScheduledToastNotification(builder.GetToastContent().GetXml(), new DateTimeOffset(reminder.FireAt))
        {
            Tag = TagPrefix + reminder.TaskId.ToString("D"),
        };
        try
        {
            notifier.AddToSchedule(toast);
        }
        catch (ArgumentException ex)
        {
            // The moment passed while scheduling; nothing to remind of any more.
            AppLog.Error("A reminder was already due when it was scheduled", ex);
        }
    }

    /// <summary>A click arrives on a background thread; the UI hears about it on its own.</summary>
    private void OnActivated(ToastNotificationActivatedEventArgsCompat args)
    {
        try
        {
            var parsed = ToastArguments.Parse(args.Argument);
            if (!parsed.TryGetValue(TaskArgument, out var text) || !Guid.TryParse(text, out var id)) return;
            _queue.TryEnqueue(() => OpenTaskRequested?.Invoke(this, id));
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException)
        {
            AppLog.Error("Couldn't read what a reminder was clicked for", ex);
        }
    }
}
