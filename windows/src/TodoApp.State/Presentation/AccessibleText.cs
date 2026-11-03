namespace TodoApp.State;

public static class AccessibleText
{
    /// <summary>A tooltip without its shortcut, for a screen reader: "New Task" from "New Task (Ctrl+N)".</summary>
    public static string FromHelp(string help)
    {
        ArgumentNullException.ThrowIfNull(help);
        var shortcut = help.IndexOf(" (", StringComparison.Ordinal);
        return shortcut < 0 ? help : help[..shortcut];
    }
}
