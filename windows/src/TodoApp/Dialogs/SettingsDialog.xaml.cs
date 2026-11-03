using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TodoApp.State;

namespace TodoApp;

/// <summary>
/// Settings: the appearance (System, Light or Dark), what the app shows, and the data folder. Every choice applies at
/// once and is saved with the other settings.
/// </summary>
public sealed partial class SettingsDialog : ContentDialog
{
    private readonly AppState _state;
    private readonly Dictionary<WorkspaceElement, CheckBox> _switches = [];
    private bool _refreshing;

    private SettingsDialog(AppState state)
    {
        _state = state;
        InitializeComponent();
        AppearanceBar.SelectedItem = AppearanceBar.Items[(int)state.Appearance];
        AppearanceBar.SelectionChanged += (sender, _) =>
        {
            var index = sender.Items.IndexOf(sender.SelectedItem);
            if (index >= 0) _state.Appearance = (AppearancePreference)index;
        };

        BuildGroups();
        ShowEverythingButton.Click += (_, _) =>
        {
            _state.ShowEverything();
            Refresh();
        };
        DataPath.Text = state.Store.DataDirectory;
        ShowDataFolderButton.Click += (_, _) => WindowsShell.OpenFolder(_state.Store.DataDirectory);
        Refresh();
    }

    /// <summary>A heading per group, then its switches in two columns.</summary>
    private void BuildGroups()
    {
        foreach (var group in Enum.GetValues<WorkspaceGroup>())
        {
            var panel = new StackPanel { Spacing = 8 };
            panel.Children.Add(new TextBlock
            {
                Style = (Style)Application.Current.Resources["BodyText"],
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["PrimaryTextBrush"],
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Text = group.Title(),
            });

            var grid = new Grid { ColumnSpacing = 12, RowSpacing = 2 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var elements = group.Elements();
            for (var i = 0; i < elements.Count; i++)
            {
                if (i % 2 == 0) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var element = elements[i];
                var box = new CheckBox { Content = element.Title(), MinWidth = 0 };
                box.Checked += (_, _) => Toggle(element, true);
                box.Unchecked += (_, _) => Toggle(element, false);
                Grid.SetRow(box, i / 2);
                Grid.SetColumn(box, i % 2);
                grid.Children.Add(box);
                _switches[element] = box;
            }

            panel.Children.Add(grid);
            Groups.Children.Add(panel);
        }
    }

    private void Toggle(WorkspaceElement element, bool visible)
    {
        if (_refreshing) return;
        _state.SetVisible(visible, element);
        Refresh();
    }

    /// <summary>The switches follow the state: the last view can't be switched off.</summary>
    private void Refresh()
    {
        _refreshing = true;
        foreach (var (element, box) in _switches)
        {
            box.IsChecked = _state.Shows(element);
            box.IsEnabled = _state.CanHide(element);
        }

        ShowEverythingButton.IsEnabled = _state.HiddenElements.Count > 0;
        _refreshing = false;
    }

    /// <summary>Shows the dialog; returns once it is closed.</summary>
    public static async Task ShowAsync(XamlRoot xamlRoot, AppState state)
    {
        ArgumentNullException.ThrowIfNull(xamlRoot);
        ArgumentNullException.ThrowIfNull(state);
        await Dialogs.ShowAsync(new SettingsDialog(state), xamlRoot);
    }
}
