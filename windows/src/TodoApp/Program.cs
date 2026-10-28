using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using TodoApp.State;
using TodoCore;

namespace TodoApp;

/// <summary>
/// Starts the app: one running copy per data folder, then the store, the state and the window.
/// It replaces the XAML-generated Main so the single-instance check runs before XAML starts.
/// </summary>
public static class Program
{
    private static readonly TimeSpan SaveDelay = TimeSpan.FromSeconds(0.4);

    private static App? _app;

    [STAThread]
    private static int Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        var options = LaunchOptions.Parse(args, Environment.GetEnvironmentVariable);
        var directory = Path.GetFullPath(options.DataDirectory ?? DataFileStore.DefaultDirectory);
        AppLog.Configure(directory);

        // Two copies on one data folder would both write tasks.json, so a second launch hands over to the first.
        // The key is per folder: a run on a sandbox folder never redirects to the copy showing real data.
        var instance = AppInstance.FindOrRegisterForKey(InstanceKey(directory));
        if (!instance.IsCurrent)
        {
            RedirectTo(instance);
            return 0;
        }

        instance.Activated += (_, _) => Volatile.Read(ref _app)?.BringToFront();

        SettingsFile? settings = null;
        Application.Start(_ =>
        {
            var queue = DispatcherQueue.GetForCurrentThread();
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(queue));

            var clock = new SystemClock();
            var store = new TaskStore(directory, clock, new DispatcherDebouncer(queue, SaveDelay), new NullReminderScheduler());
            settings = new SettingsFile(Path.Combine(directory, "settings.json"), options, new DispatcherDebouncer(queue, SaveDelay));
            var state = new AppState(store, clock, settings.Initial, settings.Save);
            if (options.Sidebar is { } item) state.Select(item);
            if (options.SelectFirst) state.SelectTask(state.NavigationOrder.FirstOrDefault()?.Id);

            Volatile.Write(ref _app, new App(state, options.Size, options.ShowSettings));
        });

        // The window has closed; write a settings change that is still waiting out its delay.
        settings?.Flush();
        return 0;
    }

    /// <summary>"TodoApp-" plus a hash of the folder's full path, compared case-insensitively like Windows paths.</summary>
    private static string InstanceKey(string directory)
    {
        var path = Path.TrimEndingDirectorySeparator(directory).ToUpperInvariant();
        return "TodoApp-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path)), 0, 16);
    }

    /// <summary>
    /// Hands this launch to the running copy, which brings its window forward. The main thread is a COM
    /// single-threaded apartment, so it waits with CoWaitForMultipleObjects, which keeps COM calls flowing.
    /// </summary>
    private static void RedirectTo(AppInstance instance)
    {
        var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
        // This process has the foreground (the user just started it), so it can pass that on.
        AllowSetForegroundWindow(instance.ProcessId);

        using var done = new ManualResetEvent(false);
        _ = Task.Run(() =>
        {
            try
            {
                instance.RedirectActivationToAsync(activation).AsTask().Wait();
            }
            catch (Exception ex)
            {
                AppLog.Error("Couldn't hand the launch to the running copy", ex);
            }
            finally
            {
                done.Set();
            }
        });
        _ = CoWaitForMultipleObjects(0, uint.MaxValue, 1, [done.SafeWaitHandle.DangerousGetHandle()], out _);
    }

    [DllImport("ole32.dll")]
    private static extern int CoWaitForMultipleObjects(uint flags, uint timeout, uint count, IntPtr[] handles, out uint index);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint processId);

    /// <summary>
    /// settings.json, written a moment after the last change, since dragging a column edge changes it on every move.
    /// <c>--view</c> and <c>--theme</c> open the window that way without becoming the saved preference: until the
    /// user picks another value, the stored one is what gets written.
    /// </summary>
    private sealed class SettingsFile(string path, LaunchOptions options, IDebouncer debouncer)
    {
        private readonly AppSettings _stored = AppSettings.Load(path);
        private bool _viewChosen = options.View is null;
        private bool _themeChosen = options.Theme is null;
        private AppSettings? _unsaved;

        public AppSettings Initial => _stored with
        {
            ViewMode = options.View ?? _stored.ViewMode,
            Appearance = options.Theme ?? _stored.Appearance,
        };

        public void Save(AppSettings settings)
        {
            _viewChosen |= settings.ViewMode != options.View;
            _themeChosen |= settings.Appearance != options.Theme;
            _unsaved = settings with
            {
                ViewMode = _viewChosen ? settings.ViewMode : _stored.ViewMode,
                Appearance = _themeChosen ? settings.Appearance : _stored.Appearance,
            };
            debouncer.Schedule(Flush);
        }

        public void Flush()
        {
            if (_unsaved is not { } settings) return;
            _unsaved = null;
            settings.Save(path);
        }
    }
}
