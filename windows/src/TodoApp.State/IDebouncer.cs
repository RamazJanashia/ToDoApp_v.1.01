namespace TodoApp.State;

public interface IDebouncer
{
    /// <summary>Runs <paramref name="action"/> after a quiet period. Replaces any action still pending.</summary>
    void Schedule(Action action);

    /// <summary>Drops the pending action, if any.</summary>
    void Cancel();
}
