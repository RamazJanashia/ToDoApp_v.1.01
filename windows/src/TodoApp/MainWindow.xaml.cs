using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using TodoApp.State;
using Windows.Graphics;
using Windows.UI;

namespace TodoApp;

/// <summary>
/// The window: a seamless title bar over sidebar · centre · inspector (RootView.swift, WindowChrome.swift).
/// Everything on screen is drawn from <see cref="State"/>; any change re-renders once per dispatcher turn.
/// </summary>
public sealed partial class MainWindow : Window
{
    private const int DefaultWidth = 1320;
    private const int DefaultHeight = 820;
    private const int MinimumHeight = 600;

    private readonly DispatcherQueueTimer _minuteTimer;
    private bool _renderQueued;
    private double _sidebarWidth;
    private double _inspectorWidth;

    /// <param name="size">The <c>--size</c> launch option; null opens at 1320×820.</param>
    public MainWindow(AppState state, (int Width, int Height)? size)
    {
        ArgumentNullException.ThrowIfNull(state);
        State = state;
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarArea);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));

        SearchGlyph.Glyph = Glyphs.For("magnifyingglass");
        InspectorButton.Content = Glyphs.For("sidebar.right");

        SidebarResizer.Range = ColumnLayout.SidebarRange;
        SidebarResizer.CurrentWidth = () => _sidebarWidth;
        SidebarResizer.WidthDragged += (_, width) => State.SidebarWidth = width;
        InspectorResizer.Range = ColumnLayout.InspectorRange;
        InspectorResizer.CurrentWidth = () => _inspectorWidth;
        InspectorResizer.WidthDragged += (_, width) => State.InspectorWidth = width;

        State.Changed += (_, _) => RenderSoon();
        Columns.SizeChanged += (_, _) => RenderColumns();
        Root.ActualThemeChanged += (_, _) => ColorCaptionButtons();
        Root.Loaded += (_, _) =>
        {
            ColorCaptionButtons();
            InsetTitleBar();
            Root.XamlRoot.Changed += (_, _) => InsetTitleBar();
        };
        AppWindow.Closing += (_, _) => State.Store.SaveNow();
        Activated += (_, args) =>
        {
            if (args.WindowActivationState != WindowActivationState.Deactivated) RefreshClock();
        };

        _minuteTimer = DispatcherQueue.CreateTimer();
        _minuteTimer.Interval = TimeSpan.FromSeconds(60);
        _minuteTimer.Tick += (_, _) => RefreshClock();
        _minuteTimer.Start();

        // After the first render has set the minimum size, so the initial size respects it.
        Render();
        Place(size ?? (DefaultWidth, DefaultHeight));
    }

    public AppState State { get; }

    private IntPtr Handle => Win32Interop.GetWindowFromWindowId(AppWindow.Id);

    /// <summary>Physical pixels per effective pixel on the window's current monitor.</summary>
    private double Scale => GetDpiForWindow(Handle) / 96.0;

    /// <summary>Queues one <see cref="Render"/> for the next dispatcher turn, however many changes arrive first.</summary>
    public void RenderSoon()
    {
        if (_renderQueued) return;
        _renderQueued = DispatcherQueue.TryEnqueue(() =>
        {
            _renderQueued = false;
            Render();
        });
    }

    /// <summary>Restores and activates the window when another launch was handed to this copy.</summary>
    public void BringToFront()
    {
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter) presenter.Restore();
        Activate();
        SetForegroundWindow(Handle);
    }

    private void Render()
    {
        Title = State.WindowTitle;
        TitleText.Text = State.WindowTitle;
        Root.RequestedTheme = State.Appearance switch
        {
            AppearancePreference.Light => ElementTheme.Light,
            AppearancePreference.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
        AppearanceButton.Content = Glyphs.For(State.Appearance switch
        {
            AppearancePreference.Light => "sun.max",
            AppearancePreference.Dark => "moon",
            _ => "circle.lefthalf.filled",
        });
        RenderColumns();

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            // The presenter limits the outer window; the frame is added so the content keeps its minimum.
            var frameWidth = AppWindow.Size.Width - AppWindow.ClientSize.Width;
            var frameHeight = AppWindow.Size.Height - AppWindow.ClientSize.Height;
            presenter.PreferredMinimumWidth = Pixels(ColumnLayout.MinimumWidth(State.IsSidebarVisible, State.IsInspectorVisible)) + frameWidth;
            presenter.PreferredMinimumHeight = Pixels(MinimumHeight) + frameHeight;
        }
    }

    /// <summary>On small windows the side columns give way, down to their minimums (ColumnLayout).</summary>
    private void RenderColumns()
    {
        (_sidebarWidth, _inspectorWidth) = ColumnLayout.Widths(Columns.ActualWidth, State.SidebarWidth, State.InspectorWidth);

        var sidebar = State.IsSidebarVisible;
        SidebarColumn.Width = new GridLength(sidebar ? _sidebarWidth : 0);
        SidebarEdgeColumn.Width = new GridLength(sidebar ? 1 : 0);
        SidebarHost.Visibility = SidebarResizer.Visibility = sidebar ? Visibility.Visible : Visibility.Collapsed;

        var inspector = State.IsInspectorVisible;
        InspectorColumn.Width = new GridLength(inspector ? _inspectorWidth : 0);
        InspectorEdgeColumn.Width = new GridLength(inspector ? 1 : 0);
        InspectorHost.Visibility = InspectorResizer.Visibility = inspector ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>"Today" and overdue colours are worked out when views draw: pick up a new day, time zone or wake.</summary>
    private void RefreshClock()
    {
        TimeZoneInfo.ClearCachedData();
        State.Tick();
    }

    /// <summary>Starts at the requested window size, centred in the work area and no larger than it.</summary>
    private void Place((int Width, int Height) size)
    {
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var width = Math.Min(Pixels(size.Width), area.Width);
        var height = Math.Min(Pixels(size.Height), area.Height);
        AppWindow.MoveAndResize(new RectInt32(area.X + (area.Width - width) / 2, area.Y + (area.Height - height) / 2, width, height));
    }

    /// <summary>Keeps the title bar controls clear of the minimise, maximise and close buttons.</summary>
    private void InsetTitleBar()
    {
        var scale = Root.XamlRoot?.RasterizationScale ?? 1;
        TitleBarTrailing.Margin = new Thickness(0, 0, AppWindow.TitleBar.RightInset / scale + 8, 0);
    }

    /// <summary>
    /// Minimise, maximise and close follow the theme. The system ignores alpha in these colours (except the
    /// transparent backgrounds), so the ink tokens are flattened onto the window background.
    /// </summary>
    private void ColorCaptionButtons()
    {
        var theme = Root.ActualTheme;
        var background = ThemeColor("BackgroundBrush", theme);
        Color Ink(string key) => Flatten(ThemeColor(key, theme), background);

        var bar = AppWindow.TitleBar;
        bar.ButtonBackgroundColor = Colors.Transparent;
        bar.ButtonInactiveBackgroundColor = Colors.Transparent;
        bar.ButtonForegroundColor = Ink("PrimaryTextBrush");
        bar.ButtonInactiveForegroundColor = Ink("TertiaryTextBrush");
        bar.ButtonHoverForegroundColor = Ink("PrimaryTextBrush");
        bar.ButtonHoverBackgroundColor = Ink("SelectionBrush");
        bar.ButtonPressedForegroundColor = Ink("PrimaryTextBrush");
        bar.ButtonPressedBackgroundColor = Ink("ChipSelectedBrush");
    }

    /// <summary>A brush colour from Theme/Colors.xaml for the given theme ("Default" holds the dark values).</summary>
    private static Color ThemeColor(string key, ElementTheme theme)
    {
        var themeKey = theme == ElementTheme.Light ? "Light" : "Default";
        foreach (var dictionary in Application.Current.Resources.MergedDictionaries)
        {
            if (dictionary.ThemeDictionaries.TryGetValue(themeKey, out var themed)
                && themed is ResourceDictionary resources
                && resources.TryGetValue(key, out var value)
                && value is SolidColorBrush brush)
            {
                return brush.Color;
            }
        }

        throw new KeyNotFoundException($"No {themeKey} brush named {key}");
    }

    private static Color Flatten(Color top, Color bottom)
    {
        var alpha = top.A / 255.0;
        byte Mix(byte over, byte under) => (byte)Math.Round(over * alpha + under * (1 - alpha));
        return ColorHelper.FromArgb(0xFF, Mix(top.R, bottom.R), Mix(top.G, bottom.G), Mix(top.B, bottom.B));
    }

    private int Pixels(double effectivePixels) => (int)Math.Round(effectivePixels * Scale);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);
}
