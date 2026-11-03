namespace TodoApp.State.Tests;

public sealed class AlertPresenterTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    /// <summary>A store whose launch raised "Couldn’t open your tasks".</summary>
    private TaskStore StoreWithAlert()
    {
        File.WriteAllText(Path.Combine(_temp.Path, "tasks.json"), "not json");
        return new TaskStore(_temp.Path, new FakeClock(), new ManualDebouncer(), new FakeReminders());
    }

    [Fact]
    public async Task WhileADialogIsOpen_TheAlertWaitsAndIsKept()
    {
        var store = StoreWithAlert();
        var shown = new List<StoreAlert>();
        var dialogOpen = true;
        var presenter = new AlertPresenter(store, () => dialogOpen, alert =>
        {
            shown.Add(alert);
            return Task.CompletedTask;
        });

        await presenter.ShowPendingAsync();
        Assert.Empty(shown);
        Assert.NotNull(store.Alert);

        dialogOpen = false;
        await presenter.ShowPendingAsync();
        Assert.Single(shown);
        Assert.Null(store.Alert);
    }

    [Fact]
    public async Task NothingPending_ShowsNothing()
    {
        var store = new TaskStore(_temp.Path, new FakeClock(), new ManualDebouncer(), new FakeReminders());
        var shown = 0;
        var presenter = new AlertPresenter(store, () => false, _ =>
        {
            shown++;
            return Task.CompletedTask;
        });

        await presenter.ShowPendingAsync();

        Assert.Equal(0, shown);
    }

    [Fact]
    public async Task WhileShowing_ASecondCallDoesNotShowItAgain()
    {
        var store = StoreWithAlert();
        var closed = new TaskCompletionSource();
        var shown = 0;
        var presenter = new AlertPresenter(store, () => false, _ =>
        {
            shown++;
            return closed.Task;
        });

        var first = presenter.ShowPendingAsync();
        await presenter.ShowPendingAsync();
        Assert.Equal(1, shown);
        Assert.NotNull(store.Alert);

        closed.SetResult();
        await first;
        Assert.Null(store.Alert);
    }
}
