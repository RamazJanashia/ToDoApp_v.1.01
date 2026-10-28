using Microsoft.UI.Xaml.Controls;
using TodoApp.State;
using TodoCore;

namespace TodoApp;

/// <summary>
/// The "More" (…) menu: the Mac's menu bar in one flyout (AppCommands.swift). It is filled again each time it opens,
/// so the labels, the checks and the project list are current. The shortcuts shown are the ones
/// <see cref="Accelerators"/> installs.
/// </summary>
public static class AppMenu
{
    public static MenuFlyout Build(MainWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var flyout = new MenuFlyout();
        flyout.Opening += (_, _) => Fill(flyout.Items, window);
        Fill(flyout.Items, window);
        return flyout;
    }

    /// <summary>System, Light and Dark as radio items; the title bar's appearance button uses it with icons.</summary>
    public static void FillAppearance(IList<MenuFlyoutItemBase> items, AppState state, bool withIcons)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(state);
        foreach (var option in Enum.GetValues<AppearancePreference>())
        {
            var item = new RadioMenuFlyoutItem { Text = option.ToString(), GroupName = "Appearance", IsChecked = state.Appearance == option };
            if (withIcons) item.Icon = new FontIcon { Glyph = Glyphs.For(AppearanceIcon(option)) };
            item.Click += (_, _) => state.Appearance = option;
            items.Add(item);
        }
    }

    /// <summary>The icon the title bar shows for the current choice, and in its menu.</summary>
    public static string AppearanceIcon(AppearancePreference option) => option switch
    {
        AppearancePreference.Light => "sun.max",
        AppearancePreference.Dark => "moon",
        _ => "circle.lefthalf.filled",
    };

    private static void Fill(IList<MenuFlyoutItemBase> items, MainWindow window)
    {
        var state = window.State;
        var selected = state.SelectedTask;
        items.Clear();

        items.Add(Item("New Task", "Ctrl+N", state.BeginNewTask));
        items.Add(Item("New Project…", "Ctrl+Shift+N", () => state.RequestProjectEditor(null)));
        items.Add(new MenuFlyoutSeparator());

        var undo = state.Store.Undo;
        items.Add(Item(undo.UndoActionName is { } undoName ? "Undo " + undoName : "Undo", "Ctrl+Z", () => Accelerators.Undo(window), undo.CanUndo));
        items.Add(Item(undo.RedoActionName is { } redoName ? "Redo " + redoName : "Redo", "Ctrl+Y", () => Accelerators.Redo(window), undo.CanRedo));
        items.Add(new MenuFlyoutSeparator());

        items.Add(TaskMenu(state, selected));
        items.Add(ViewMenu(state));
        items.Add(GoMenu(state));
        items.Add(new MenuFlyoutSeparator());

        items.Add(Item("Search Tasks", "Ctrl+F", state.FocusSearch));
        items.Add(Item("Settings…", "Ctrl+,", window.ShowSettings));
    }

    private static MenuFlyoutSubItem TaskMenu(AppState state, TaskItem? selected)
    {
        var menu = new MenuFlyoutSubItem { Text = "Task", IsEnabled = selected is not null };
        var id = selected?.Id ?? Guid.Empty;
        var ids = new[] { id };

        menu.Items.Add(Item(selected?.IsCompleted == true ? "Mark as Incomplete" : "Complete", "Ctrl+Enter", state.ToggleSelectedCompleted));
        menu.Items.Add(Item("Edit Title", "Ctrl+E", () => state.RevealInInspector(id, focusTitle: true)));
        menu.Items.Add(Item("Duplicate", "Ctrl+D", state.DuplicateSelected));
        menu.Items.Add(new MenuFlyoutSeparator());

        var priority = new MenuFlyoutSubItem { Text = "Priority" };
        foreach (var level in Priorities.Descending)
        {
            var item = new ToggleMenuFlyoutItem
            {
                Text = level.Title(),
                IsChecked = selected?.Priority == level,
                KeyboardAcceleratorTextOverride = "Ctrl+Shift+" + (int)level,
            };
            item.Click += (_, _) => state.SetPriority(level, ids);
            priority.Items.Add(item);
        }

        menu.Items.Add(priority);

        var due = new MenuFlyoutSubItem { Text = "Due Date" };
        due.Items.Add(Item("Today", "Ctrl+T", () => state.SetDueOffset(0, id)));
        due.Items.Add(Item("Tomorrow", "Ctrl+Shift+T", () => state.SetDueOffset(1, id)));
        due.Items.Add(Item("Next Week", null, () => state.SetDueOffset(7, id)));
        due.Items.Add(new MenuFlyoutSeparator());
        due.Items.Add(Item("Remove Due Date", null, () => state.SetDueDay(null, ids)));
        menu.Items.Add(due);

        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(Item("Delete", "Delete", state.DeleteSelected));
        return menu;
    }

    private static MenuFlyoutSubItem ViewMenu(AppState state)
    {
        var menu = new MenuFlyoutSubItem { Text = "View" };
        var number = 1;
        foreach (var mode in Enum.GetValues<ViewMode>())
        {
            var item = new RadioMenuFlyoutItem
            {
                Text = "as " + mode,
                GroupName = "ViewMode",
                IsChecked = state.ViewMode == mode,
                KeyboardAcceleratorTextOverride = "Ctrl+" + number++,
            };
            item.Click += (_, _) => state.ViewMode = mode;
            menu.Items.Add(item);
        }

        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(Item(state.IsSidebarVisible ? "Hide Sidebar" : "Show Sidebar", "Ctrl+Shift+S", () => state.IsSidebarVisible = !state.IsSidebarVisible));
        menu.Items.Add(Item(state.IsInspectorVisible ? "Hide Inspector" : "Show Inspector", "Ctrl+Shift+I", () => state.IsInspectorVisible = !state.IsInspectorVisible));
        menu.Items.Add(new MenuFlyoutSeparator());

        var appearance = new MenuFlyoutSubItem { Text = "Appearance" };
        FillAppearance(appearance.Items, state, withIcons: false);
        menu.Items.Add(appearance);
        return menu;
    }

    private static MenuFlyoutSubItem GoMenu(AppState state)
    {
        var menu = new MenuFlyoutSubItem { Text = "Go" };
        (string Title, SidebarItem Item)[] places =
        [
            ("Inbox", SidebarItem.Inbox),
            ("Today", SidebarItem.Today),
            ("Upcoming", SidebarItem.Upcoming),
            ("Completed", SidebarItem.Completed),
            ("Dashboard", SidebarItem.Dashboard),
        ];
        for (var i = 0; i < places.Length; i++)
        {
            var place = places[i].Item;
            menu.Items.Add(Item(places[i].Title, "Alt+" + (i + 1), () => state.Select(place)));
        }

        if (state.Store.Projects.Count > 0) menu.Items.Add(new MenuFlyoutSeparator());
        foreach (var project in state.Store.Projects)
        {
            var id = project.Id;
            menu.Items.Add(Item(project.Name, null, () => state.Select(SidebarItem.Project(id))));
        }

        return menu;
    }

    private static MenuFlyoutItem Item(string text, string? shortcut, Action action, bool enabled = true)
    {
        var item = new MenuFlyoutItem { Text = text, IsEnabled = enabled };
        if (shortcut is not null) item.KeyboardAcceleratorTextOverride = shortcut;
        item.Click += (_, _) => action();
        return item;
    }
}
