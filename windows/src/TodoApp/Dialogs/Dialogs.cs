using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TodoApp.State;
using TodoCore;

namespace TodoApp;

/// <summary>
/// Shows dialogs one at a time (a XamlRoot allows a single open ContentDialog) in the window's theme, and holds the
/// delete confirmation from RootView.swift.
/// </summary>
public static class Dialogs
{
    /// <summary>True while a dialog is up; keyboard shortcuts for tasks stay quiet then.</summary>
    public static bool IsOpen { get; private set; }

    /// <summary>Shows <paramref name="dialog"/>; returns None without showing it when another dialog is open.</summary>
    public static async Task<ContentDialogResult> ShowAsync(ContentDialog dialog, XamlRoot xamlRoot)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        ArgumentNullException.ThrowIfNull(xamlRoot);
        if (IsOpen) return ContentDialogResult.None;

        IsOpen = true;
        try
        {
            // The dialog lives in the popup layer, outside the window's content, so it doesn't inherit a forced theme.
            if (xamlRoot.Content is FrameworkElement root) dialog.RequestedTheme = root.ActualTheme;
            dialog.XamlRoot = xamlRoot;
            return await dialog.ShowAsync();
        }
        finally
        {
            IsOpen = false;
        }
    }

    /// <summary>
    /// "Delete “Name”?" with Delete Project and Cancel; confirming deletes it. What's under it moves up a level, as the
    /// message says.
    /// </summary>
    public static async Task ConfirmProjectDeletionAsync(XamlRoot xamlRoot, AppState state, Project project)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(project);

        var dialog = new ContentDialog
        {
            Title = $"Delete “{project.Name}”?",
            Content = new TextBlock
            {
                Style = (Style)Application.Current.Resources["BodyText"],
                Text = state.ProjectDeletionMessage(project),
                TextWrapping = TextWrapping.Wrap,
            },
            PrimaryButtonText = "Delete Project",
            CloseButtonText = "Cancel",
            // Enter cancels: a destructive action shouldn't be one stray keypress away.
            DefaultButton = ContentDialogButton.Close,
        };
        if (await ShowAsync(dialog, xamlRoot) == ContentDialogResult.Primary) state.DeleteProjectConfirmed(project.Id);
    }
}
