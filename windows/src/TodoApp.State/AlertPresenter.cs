namespace TodoApp.State;

/// <summary>
/// Puts the store's alert on screen ("Couldn’t save your tasks"…) once nothing else is: only one dialog can be open at
/// a time, so an alert raised while another is up waits for it to close. The alert is dismissed only after it has
/// been shown, so none is lost. Main-thread only.
/// </summary>
/// <param name="isDialogOpen">Whether a dialog is on screen now.</param>
/// <param name="show">Shows the alert; completes when it is closed.</param>
public sealed class AlertPresenter(TaskStore store, Func<bool> isDialogOpen, Func<StoreAlert, Task> show)
{
    private bool _showing;

    /// <summary>Call when an alert is raised and whenever a dialog closes.</summary>
    public async Task ShowPendingAsync()
    {
        if (_showing || isDialogOpen() || store.Alert is not { } alert) return;
        _showing = true;
        try
        {
            await show(alert);
            store.DismissAlert();
        }
        finally
        {
            _showing = false;
        }
    }
}
