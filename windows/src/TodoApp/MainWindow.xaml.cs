using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using TodoApp.State;
using Windows.Graphics;
using Windows.UI;
using CoreVirtualKeyStates = Windows.UI.Core.CoreVirtualKeyStates;
using InputKeyboardSource = Microsoft.UI.Input.InputKeyboardSource;
using VirtualKey = Windows.System.VirtualKey;

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
    private readonly SidebarView _sidebar;
    private readonly CenterView _center;
    private readonly InspectorView _inspector;
    private bool _renderQueued;
    private double _sidebarWidth;
    private double _inspectorWidth;

    /// <param name="size">The <c>--size</c> launch option; null opens at 1320×820.</param>
    public MainWindow(AppState state, (int Width, int Height)? size)
    {
        ArgumentNullException.ThrowIfNull(state);
        State = state;
        InitializeComponent();
        WindowsShell.WindowHandle = Handle;

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarArea);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));

        SearchGlyph.Glyph = Glyphs.For("magnifyingglass");
        SearchClear.Content = Glyphs.For("xmark.circle.fill");
        InspectorButton.Content = Glyphs.For("sidebar.right");
        MoreButton.Content = Glyphs.For("ellipsis");
        InstallTitleBarControls();

        SidebarResizer.Range = ColumnLayout.SidebarRange;
        SidebarResizer.CurrentWidth = () => _sidebarWidth;
        SidebarResizer.WidthDragged += (_, width) => State.SidebarWidth = width;
        InspectorResizer.Range = ColumnLayout.InspectorRange;
        InspectorResizer.CurrentWidth = () => _inspectorWidth;
        InspectorResizer.WidthDragged += (_, width) => State.InspectorWidth = width;

        _sidebar = new SidebarView();
        SidebarHost.Content = _sidebar;
        _center = new CenterView();
        CenterHost.Content = _center;
        _inspector = new InspectorView();
        InspectorHost.Content = _inspector;
        Root.PreviewKeyDown += OnPreviewKeyDown;
        Accelerators.Install(Root, this);

        State.Changed += (_, _) => RenderSoon();
        State.TitleFocusRequested += (_, _) => _inspector.FocusTitle();
        State.ProjectEditorRequested += async (_, projectId) => await ShowDialogAsync(root => ProjectEditorDialog.ShowAsync(root, State, projectId));
        State.ProjectDeletionRequested += async (_, project) => await ShowDialogAsync(root => Dialogs.ConfirmProjectDeletionAsync(root, State, project));
        Columns.SizeChanged += (_, _) => RenderColumns();
        Root.ActualThemeChanged += (_, _) => ColorCaptionButtons();
        Root.Loaded += (_, _) =>
        {
            ColorCaptionButtons();
            InsetTitleBar();
            Root.XamlRoot.Changed += (_, _) => InsetTitleBar();
            // The search box is the first thing that can take focus; the keys belong to the list until it is clicked.
            CenterHost.Focus(FocusState.Pointer);
            // Once, after the first render: tasks that couldn't be read were set aside, and the user should know.
            if (State.Store.LoadIssue is { } issue) _ = ShowDialogAsync(root => ShowLoadIssueAsync(root, issue));
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

    /// <summary>Opens Settings (Ctrl+, and the menu); at launch (<c>--settings</c>) it waits for the window to be up.</summary>
    public void ShowSettings()
    {
        if (Root.IsLoaded)
        {
            _ = ShowDialogAsync(root => SettingsDialog.ShowAsync(root, State));
            return;
        }

        RoutedEventHandler? opened = null;
        opened = (_, _) =>
        {
            Root.Loaded -= opened;
            _ = ShowDialogAsync(root => SettingsDialog.ShowAsync(root, State));
        };
        Root.Loaded += opened;
    }

    /// <summary>The search box, and the appearance, inspector and "More" buttons of the title bar.</summary>
    private void InstallTitleBarControls()
    {
        SearchBox.TextChanged += (_, _) =>
        {
            SearchClear.Visibility = SearchBox.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (SearchBox.Text != State.SearchText) State.SearchText = SearchBox.Text;
        };
        SearchBox.GotFocus += (_, _) => PaintSearchBorder();
        SearchBox.LostFocus += (_, _) => PaintSearchBorder();
        // Esc clears the search and gives the keys back to the list.
        SearchBox.KeyDown += (_, e) =>
        {
            if (e.Key != VirtualKey.Escape) return;
            e.Handled = true;
            SearchBox.Text = "";
            State.SearchText = "";
            CenterHost.Focus(FocusState.Pointer);
        };
        SearchClear.Click += (_, _) =>
        {
            SearchBox.Text = "";
            State.SearchText = "";
            SearchBox.Focus(FocusState.Programmatic);
        };
        State.SearchFocusRequested += (_, _) =>
        {
            SearchBox.Focus(FocusState.Programmatic);
            SearchBox.SelectAll();
        };

        var appearance = new MenuFlyout();
        appearance.Opening += (_, _) =>
        {
            appearance.Items.Clear();
            AppMenu.FillAppearance(appearance.Items, State, withIcons: true);
        };
        AppearanceButton.Flyout = appearance;

        InspectorButton.Click += (_, _) => State.IsInspectorVisible = !State.IsInspectorVisible;
        MoreButton.Flyout = AppMenu.Build(this);
    }

    /// <summary>The search box shows the focus ring while it has focus.</summary>
    private void PaintSearchBorder()
    {
        var key = SearchBox.FocusState != FocusState.Unfocused ? "FocusRingBrush" : "SeparatorBrush";
        SearchBorder.BorderBrush = new SolidColorBrush(ThemeColor(key, Root.ActualTheme));
    }

    /// <summary>"Couldn’t open your tasks" with what went wrong; the file was set aside and not overwritten.</summary>
    private static Task ShowLoadIssueAsync(XamlRoot xamlRoot, string issue) => Dialogs.ShowAsync(
        new ContentDialog
        {
            Title = "Couldn’t open your tasks",
            Content = new TextBlock
            {
                Style = (Style)Application.Current.Resources["BodyText"],
                Text = issue,
                TextWrapping = TextWrapping.Wrap,
            },
            CloseButtonText = "OK",
            DefaultButton = ContentDialogButton.Close,
        },
        xamlRoot);

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
        AppearanceButton.Content = Glyphs.For(AppMenu.AppearanceIcon(State.Appearance));
        var inspectorTip = (State.IsInspectorVisible ? "Hide Inspector" : "Show Inspector") + " (Ctrl+Shift+I)";
        ToolTipService.SetToolTip(InspectorButton, inspectorTip);
        if (TextSync.ShouldPush(SearchBox.Text, State.SearchText, SearchBox.FocusState != FocusState.Unfocused)) SearchBox.Text = State.SearchText;
        RenderColumns();
        _sidebar.Render(State);
        _center.Render(State);
        if (State.IsInspectorVisible) _inspector.Render(State);

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            // The presenter limits the outer window; the frame is added so the content keeps its minimum.
            var frameWidth = AppWindow.Size.Width - AppWindow.ClientSize.Width;
            var frameHeight = AppWindow.Size.Height - AppWindow.ClientSize.Height;
            presenter.PreferredMinimumWidth = Pixels(ColumnLayout.MinimumWidth(State.IsSidebarVisible, State.IsInspectorVisible)) + frameWidth;
            presenter.PreferredMinimumHeight = Pixels(MinimumHeight) + frameHeight;
        }
    }

    /// <summary>True when keystrokes are typing: a text field, search box, number box, password or rich text has focus.</summary>
    internal static bool IsTextInputFocused(XamlRoot? xamlRoot) =>
        xamlRoot is not null
        && FocusManager.GetFocusedElement(xamlRoot) is TextBox or AutoSuggestBox or NumberBox or PasswordBox or RichEditBox;

    /// <summary>
    /// The bare keys (arrows, Space, Enter, Delete, Backspace, Esc) act on tasks, unless a text input has focus, a
    /// modifier is held, a dialog is open or the Dashboard is showing (AppState.HandleKey decides).
    /// </summary>
    private void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        KeyCommand? command = e.Key switch
        {
            VirtualKey.Down => KeyCommand.Down,
            VirtualKey.Up => KeyCommand.Up,
            VirtualKey.Space => KeyCommand.Space,
            VirtualKey.Enter => KeyCommand.Enter,
            VirtualKey.Delete or VirtualKey.Back => KeyCommand.Delete,
            VirtualKey.Escape => KeyCommand.Escape,
            _ => null,
        };
        if (command is not { } key) return;

        // Shift doesn't count, as on the Mac.
        var hasModifiers = IsKeyDown(VirtualKey.Control)
            || IsKeyDown(VirtualKey.Menu)
            || IsKeyDown(VirtualKey.LeftWindows)
            || IsKeyDown(VirtualKey.RightWindows);
        if (State.HandleKey(key, IsTextInputFocused(Root.XamlRoot), hasModifiers, Dialogs.IsOpen)) e.Handled = true;
    }

    private static bool IsKeyDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    /// <summary>Runs a dialog on the window's content; a failure is logged rather than ending the app.</summary>
    private async Task ShowDialogAsync(Func<XamlRoot, Task> show)
    {
        if (Root.XamlRoot is not { } xamlRoot) return;
        try
        {
            await show(xamlRoot);
        }
        catch (Exception ex)
        {
            AppLog.Error("A dialog failed", ex);
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
