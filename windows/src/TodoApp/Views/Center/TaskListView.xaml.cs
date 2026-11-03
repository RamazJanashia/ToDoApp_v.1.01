using System.Collections.ObjectModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TodoApp.State;

namespace TodoApp;

/// <summary>
/// The list (TaskListView.swift): section headers and task rows in one repeater, the empty state, and the "New task"
/// composer, capped at the reading width. A newly selected task, or a newly opened composer, is scrolled into view.
/// </summary>
public sealed partial class TaskListView : UserControl
{
    /// <summary>Section headers and <see cref="TaskRowModel"/>s, in display order.</summary>
    private readonly ObservableCollection<object> _items = [];

    /// <summary>The same for a project's finished tasks, shown below the composer.</summary>
    private readonly ObservableCollection<object> _history = [];
    private AppState? _state;
    private Guid? _shownSelection;
    private bool _wasComposing;

    public TaskListView()
    {
        InitializeComponent();
        var templates = new ItemTemplates((DataTemplate)Resources["RowTemplate"], (DataTemplate)Resources["HeaderTemplate"]);
        Repeater.ItemTemplate = templates;
        Repeater.ItemsSource = _items;
        Repeater.ElementPrepared += (_, e) => Prepare(_items, e);
        HistoryRepeater.ItemTemplate = templates;
        HistoryRepeater.ItemsSource = _history;
        HistoryRepeater.ElementPrepared += (_, e) => Prepare(_history, e);

        // Clicking a row ends any text editing, so the arrow keys and Space go to the list (Mac: selectTask endEditing).
        Repeater.Tapped += (_, _) => EndEditing();
        HistoryRepeater.Tapped += (_, _) => EndEditing();
    }

    public void Render(AppState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _state = state;
        var model = ListModel.Build(state);

        CollectionSync.Sync(_items, Flatten(model.Sections), Key);
        CollectionSync.Sync(_history, Flatten(model.History), Key);

        Empty.Visibility = model.Empty is null ? Visibility.Collapsed : Visibility.Visible;
        if (model.Empty is { } empty) Empty.Show(empty);
        Composer.Visibility = model.CanCompose ? Visibility.Visible : Visibility.Collapsed;
        Composer.Render(state);

        if (state.SelectedTaskId != _shownSelection)
        {
            _shownSelection = state.SelectedTaskId;
            if (_shownSelection is { } id) BringRowIntoView(id);
        }

        if (state.IsComposing && !_wasComposing) BringComposerIntoView();
        _wasComposing = state.IsComposing;
    }

    private static string Key(object item) => item switch
    {
        TaskRowModel row => row.Id.ToString("D"),
        SectionHeaderItem header => "section:" + header.Id,
        _ => throw new ArgumentException($"Unexpected list item {item.GetType()}", nameof(item)),
    };

    private static List<object> Flatten(IReadOnlyList<ListSectionModel> sections)
    {
        var items = new List<object>();
        foreach (var section in sections)
        {
            if (section.Title is { } title) items.Add(new SectionHeaderItem(section.Id, title, section.Detail, section.IsWarning));
            items.AddRange(section.Rows);
        }

        return items;
    }

    private void EndEditing()
    {
        if (MainWindow.IsTextInputFocused(XamlRoot)) Scroller.Focus(FocusState.Pointer);
    }

    private void Prepare(ObservableCollection<object> items, ItemsRepeaterElementPreparedEventArgs e)
    {
        if (_state is null || e.Index < 0 || e.Index >= items.Count) return;
        switch (items[e.Index], e.Element)
        {
            case (TaskRowModel model, TaskRow row):
                row.Bind(_state, model);
                break;
            case (SectionHeaderItem header, StackPanel panel):
                ShowHeader(panel, header);
                break;
        }
    }

    private static void ShowHeader(StackPanel panel, SectionHeaderItem header)
    {
        var title = (TextBlock)panel.Children[0];
        var warning = (TextBlock)panel.Children[1];
        var detail = (TextBlock)panel.Children[2];
        title.Text = warning.Text = header.Title;
        title.Visibility = header.IsWarning ? Visibility.Collapsed : Visibility.Visible;
        warning.Visibility = header.IsWarning ? Visibility.Visible : Visibility.Collapsed;
        detail.Text = header.Detail ?? string.Empty;
        detail.Visibility = header.Detail is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void BringRowIntoView(Guid id)
    {
        // After the repeater has taken in this render's changes.
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (!Repeater.IsLoaded || Visibility != Visibility.Visible) return;
            var (repeater, index) = Find(_items, Repeater, id) is { Index: >= 0 } open ? open : Find(_history, HistoryRepeater, id);
            if (index < 0) return;
            var element = repeater.GetOrCreateElement(index);
            element.UpdateLayout();
            element.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = true });
        });
    }

    private static (ItemsRepeater Repeater, int Index) Find(ObservableCollection<object> items, ItemsRepeater repeater, Guid id)
    {
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i] is TaskRowModel row && row.Id == id) return (repeater, i);
        }

        return (repeater, -1);
    }

    private void BringComposerIntoView()
    {
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (!Composer.IsLoaded || Composer.Visibility != Visibility.Visible) return;
            Composer.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = true, VerticalAlignmentRatio = 1 });
        });
    }

    /// <summary>A section's title line; sections without a title have none.</summary>
    private sealed record SectionHeaderItem(string Id, string Title, string? Detail, bool IsWarning);

    /// <summary>Rows and section headers come from their own templates, and are recycled separately.</summary>
    private sealed partial class ItemTemplates(DataTemplate row, DataTemplate header) : DataTemplateSelector
    {
        protected override DataTemplate SelectTemplateCore(object item) => item is TaskRowModel ? row : header;

        protected override DataTemplate SelectTemplateCore(object item, DependencyObject container) => SelectTemplateCore(item);
    }
}
