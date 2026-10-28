using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using TodoApp.State;

namespace TodoApp;

public partial class App : Application
{
    private readonly AppState _state;
    private readonly (int Width, int Height)? _size;
    private readonly bool _showSettings;
    private readonly DispatcherQueue _queue;
    private MainWindow? _window;

    /// <param name="size">The <c>--size</c> launch option; null opens at the default size.</param>
    /// <param name="showSettings">The <c>--settings</c> launch option: open Settings once the window is up.</param>
    public App(AppState state, (int Width, int Height)? size, bool showSettings = false)
    {
        ArgumentNullException.ThrowIfNull(state);
        _state = state;
        _size = size;
        _showSettings = showSettings;
        _queue = DispatcherQueue.GetForCurrentThread();
        InitializeComponent();
        UnhandledException += (_, e) => AppLog.Error("Unhandled exception", e.Exception);
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow(_state, _size);
        _window.Activate();
        if (_showSettings) _window.ShowSettings();
    }

    /// <summary>A second launch was handed to this copy: show the window. Callable from any thread.</summary>
    public void BringToFront() => _queue.TryEnqueue(() => _window?.BringToFront());
}
