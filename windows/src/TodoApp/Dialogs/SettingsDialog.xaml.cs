using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TodoApp.State;

namespace TodoApp;

/// <summary>Settings: the appearance (System, Light or Dark). A choice applies at once and is saved with the other settings.</summary>
public sealed partial class SettingsDialog : ContentDialog
{
    private readonly AppState _state;

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
    }

    /// <summary>Shows the dialog; returns once it is closed.</summary>
    public static async Task ShowAsync(XamlRoot xamlRoot, AppState state)
    {
        ArgumentNullException.ThrowIfNull(xamlRoot);
        ArgumentNullException.ThrowIfNull(state);
        await Dialogs.ShowAsync(new SettingsDialog(state), xamlRoot);
    }
}
