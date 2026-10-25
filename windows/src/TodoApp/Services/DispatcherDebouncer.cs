using Microsoft.UI.Dispatching;
using TodoApp.State;

namespace TodoApp;

/// <summary>
/// Runs the latest scheduled action on the UI thread once things have been quiet for <c>delay</c>.
/// The action may call <see cref="Schedule"/> or <see cref="Cancel"/> itself (TaskStore.SaveNow cancels).
/// </summary>
public sealed class DispatcherDebouncer : IDebouncer
{
    private readonly DispatcherQueueTimer _timer;
    private Action? _pending;

    public DispatcherDebouncer(DispatcherQueue queue, TimeSpan delay)
    {
        ArgumentNullException.ThrowIfNull(queue);
        _timer = queue.CreateTimer();
        _timer.Interval = delay;
        _timer.IsRepeating = false;
        _timer.Tick += OnTick;
    }

    public void Schedule(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _pending = action;
        _timer.Stop();
        _timer.Start();
    }

    public void Cancel()
    {
        _pending = null;
        _timer.Stop();
    }

    private void OnTick(DispatcherQueueTimer sender, object args)
    {
        sender.Stop();
        // Taken before running, so the action is free to schedule a new one or cancel.
        var action = _pending;
        _pending = null;
        action?.Invoke();
    }
}
