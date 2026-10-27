using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TodoApp.State;

namespace TodoApp;

/// <summary>
/// Keeps a text box and one of the task's texts in step. The box reports line breaks as "\r" (the model keeps the
/// Mac's "\n") and may not keep everything it's given, so what it showed right after the last push is remembered:
/// a TextChanged that still shows exactly that, while the task still has the pushed text, is the push echoing back,
/// not the user typing, and nothing is saved.
/// </summary>
internal sealed class EditableText(TextBox box)
{
    private string? _pushed;
    private string? _shown;

    /// <summary>
    /// Shows the task's text unless the user is typing in the box (<see cref="TextSync"/>) or it already shows it.
    /// <paramref name="force"/> (another task) always replaces it.
    /// </summary>
    public void Push(string text, bool force)
    {
        if (!force)
        {
            if (IsEcho(text)) return;
            var hasFocus = box.FocusState != FocusState.Unfocused;
            if (!TextSync.ShouldPush(Normalize(box.Text), Normalize(text), hasFocus)) return;
        }

        box.Text = text;
        _pushed = text;
        _shown = box.Text;
    }

    /// <summary>The box's text, with "\n" line breaks, when the user has changed it; otherwise null.</summary>
    public string? Edited(string current)
    {
        if (IsEcho(current)) return null;
        var text = Normalize(box.Text);
        return text == Normalize(current) ? null : text;
    }

    private static string Normalize(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    private bool IsEcho(string current) =>
        string.Equals(box.Text, _shown, StringComparison.Ordinal) && string.Equals(current, _pushed, StringComparison.Ordinal);
}
