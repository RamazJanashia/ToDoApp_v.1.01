using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TodoApp.State;

namespace TodoApp;

/// <summary>An empty list's icon, title and optional message, from an <see cref="EmptyStateModel"/>.</summary>
public sealed partial class EmptyState : UserControl
{
    public EmptyState()
    {
        InitializeComponent();
    }

    public void Show(EmptyStateModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        IconGlyph.Glyph = Glyphs.For(model.Icon);
        TitleText.Text = model.Title;
        MessageText.Text = model.Message ?? string.Empty;
        MessageText.Visibility = model.Message is null ? Visibility.Collapsed : Visibility.Visible;
    }
}
