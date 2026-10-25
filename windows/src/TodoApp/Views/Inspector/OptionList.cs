using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using TodoApp.State;
using TodoCore;

namespace TodoApp;

/// <summary>What a line of an <see cref="OptionList"/> is.</summary>
public enum OptionKind
{
    /// <summary>A choice the user can click (OptionRow).</summary>
    Choice,

    /// <summary>A hairline between groups.</summary>
    Separator,

    /// <summary>An explanation in caption type and secondary ink.</summary>
    Caption,

    /// <summary>A footnote in small type and tertiary ink.</summary>
    Footnote,
}

/// <summary>
/// One line of an <see cref="OptionList"/>. Icon names are SF Symbols; <see cref="IconToken"/> is "Secondary", "Tertiary",
/// "Red", "PriorityMedium", "PriorityLow" or "Project" (with <see cref="IconProjectColor"/>).
/// </summary>
public sealed class OptionItem
{
    private OptionItem(OptionKind kind, string text)
    {
        Kind = kind;
        Text = text;
    }

    public OptionKind Kind { get; }

    public string Text { get; }

    public string? Icon { get; private init; }

    public string IconToken { get; private init; } = "Secondary";

    public ProjectColor? IconProjectColor { get; private init; }

    /// <summary>The current choice: shows a tick.</summary>
    public bool IsSelected { get; private init; }

    /// <summary>A keyboard shortcut shown on the right in small tertiary type.</summary>
    public string? Shortcut { get; private init; }

    public Action? Invoke { get; private init; }

    public static OptionItem Choice(
        string title,
        Action invoke,
        string? icon = null,
        bool isSelected = false,
        string iconToken = "Secondary",
        ProjectColor? iconProjectColor = null,
        string? shortcut = null) =>
        new(OptionKind.Choice, title)
        {
            Invoke = invoke,
            Icon = icon,
            IsSelected = isSelected,
            IconToken = iconToken,
            IconProjectColor = iconProjectColor,
            Shortcut = shortcut,
        };

    public static OptionItem Separator() => new(OptionKind.Separator, string.Empty);

    public static OptionItem Caption(string text) => new(OptionKind.Caption, text);

    public static OptionItem Footnote(string text) => new(OptionKind.Footnote, text);
}

/// <summary>
/// The list on a property's flyout (PopoverSurface and OptionRow in Components.swift): 30 px rows with an optional 13 px
/// icon, the title, an optional shortcut and a tick on the current choice, a selection fill under the pointer, and
/// hairlines between groups; padding 6, rows 1 apart. A click runs the item's action.
/// When <see cref="Show"/> gets the same lines again, the rows are updated in place, so keyboard focus stays put.
/// Hosts set the brushes with ThemeResource; only lists with tinted icons need the red and priority brushes.
/// </summary>
public partial class OptionList : StackPanel
{
    public static readonly DependencyProperty PrimaryBrushProperty = RegisterBrush(nameof(PrimaryBrush));
    public static readonly DependencyProperty SecondaryBrushProperty = RegisterBrush(nameof(SecondaryBrush));
    public static readonly DependencyProperty TertiaryBrushProperty = RegisterBrush(nameof(TertiaryBrush));
    public static readonly DependencyProperty HoverBrushProperty = RegisterBrush(nameof(HoverBrush));
    public static readonly DependencyProperty SeparatorBrushProperty = RegisterBrush(nameof(SeparatorBrush));
    public static readonly DependencyProperty RedBrushProperty = RegisterBrush(nameof(RedBrush));
    public static readonly DependencyProperty PriorityMediumBrushProperty = RegisterBrush(nameof(PriorityMediumBrush));
    public static readonly DependencyProperty PriorityLowBrushProperty = RegisterBrush(nameof(PriorityLowBrush));

    private static readonly SolidColorBrush Clear = new(Colors.Transparent);

    private readonly List<Line> _lines = [];
    private IReadOnlyList<OptionItem> _items = [];
    private string? _shape;

    public OptionList()
    {
        Padding = new Thickness(6);
        Spacing = 1;
        ActualThemeChanged += (_, _) => Paint();
    }

    /// <summary>Titles and ticks (PrimaryText).</summary>
    public Brush? PrimaryBrush
    {
        get => (Brush?)GetValue(PrimaryBrushProperty);
        set => SetValue(PrimaryBrushProperty, value);
    }

    /// <summary>Default icons and captions (SecondaryText).</summary>
    public Brush? SecondaryBrush
    {
        get => (Brush?)GetValue(SecondaryBrushProperty);
        set => SetValue(SecondaryBrushProperty, value);
    }

    /// <summary>Shortcuts, footnotes and the "Tertiary" icon tint (TertiaryText).</summary>
    public Brush? TertiaryBrush
    {
        get => (Brush?)GetValue(TertiaryBrushProperty);
        set => SetValue(TertiaryBrushProperty, value);
    }

    /// <summary>The row under the pointer (Selection).</summary>
    public Brush? HoverBrush
    {
        get => (Brush?)GetValue(HoverBrushProperty);
        set => SetValue(HoverBrushProperty, value);
    }

    /// <summary>Hairlines between groups (Separator).</summary>
    public Brush? SeparatorBrush
    {
        get => (Brush?)GetValue(SeparatorBrushProperty);
        set => SetValue(SeparatorBrushProperty, value);
    }

    public Brush? RedBrush
    {
        get => (Brush?)GetValue(RedBrushProperty);
        set => SetValue(RedBrushProperty, value);
    }

    public Brush? PriorityMediumBrush
    {
        get => (Brush?)GetValue(PriorityMediumBrushProperty);
        set => SetValue(PriorityMediumBrushProperty, value);
    }

    public Brush? PriorityLowBrush
    {
        get => (Brush?)GetValue(PriorityLowBrushProperty);
        set => SetValue(PriorityLowBrushProperty, value);
    }

    public void Show(IReadOnlyList<OptionItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        _items = items;
        var shape = string.Join("\n", items.Select(i => $"{i.Kind}|{i.Icon}|{i.Text}|{i.Shortcut}"));
        if (shape != _shape)
        {
            _shape = shape;
            Build();
        }

        Paint();
    }

    private static DependencyProperty RegisterBrush(string name) => DependencyProperty.Register(
        name,
        typeof(Brush),
        typeof(OptionList),
        new PropertyMetadata(null, (d, _) => ((OptionList)d).Rebuild()));

    /// <summary>The hover fill is baked into each row when it's made, so new brushes (a theme change) mean new rows.</summary>
    private void Rebuild()
    {
        _shape = null;
        if (_items.Count > 0) Show(_items);
    }

    private void Build()
    {
        Children.Clear();
        _lines.Clear();
        for (var index = 0; index < _items.Count; index++)
        {
            var line = _items[index].Kind switch
            {
                OptionKind.Choice => ChoiceLine(_items[index], index),
                OptionKind.Separator => new Line(new Rectangle { Height = 1, Margin = new Thickness(0, 4, 0, 4) }),
                OptionKind.Caption => NoteLine(_items[index].Text, 12, new Thickness(10)),
                OptionKind.Footnote => NoteLine(_items[index].Text, 11, new Thickness(10, 8, 10, 8)),
                _ => throw new InvalidOperationException($"Unknown option kind {_items[index].Kind}"),
            };
            _lines.Add(line);
            Children.Add(line.Element);
        }
    }

    private Line ChoiceLine(OptionItem item, int index)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        FontIcon? icon = null;
        if (item.Icon is { } symbol)
        {
            icon = new FontIcon
            {
                Glyph = Glyphs.For(symbol),
                FontSize = 13,
                Width = 18,
                Margin = new Thickness(0, 0, 10, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            grid.Children.Add(icon);
        }

        var title = Text(item.Text, 13);
        title.MaxLines = 1;
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        Grid.SetColumn(title, 1);
        grid.Children.Add(title);

        // HStack spacing 10 around a Spacer of at least 12 before the shortcut and the tick.
        var trailing = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Margin = new Thickness(32, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        TextBlock? shortcut = null;
        if (item.Shortcut is { } keys)
        {
            shortcut = Text(keys, 11);
            trailing.Children.Add(shortcut);
        }

        var check = new FontIcon { Glyph = Glyphs.For("checkmark"), FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
        trailing.Children.Add(check);
        Grid.SetColumn(trailing, 2);
        grid.Children.Add(trailing);

        var button = new Button
        {
            Content = grid,
            Height = 30,
            Padding = new Thickness(10, 0, 10, 0),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(0),
            Background = Clear,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Center,
        };

        // Lightweight styling: the default template takes its pointer-over and pressed fills from these keys.
        var hover = HoverBrush ?? Clear;
        button.Resources["ButtonBackgroundPointerOver"] = hover;
        button.Resources["ButtonBackgroundPressed"] = hover;
        button.Resources["ButtonBorderBrushPointerOver"] = Clear;
        button.Resources["ButtonBorderBrushPressed"] = Clear;
        AutomationProperties.SetName(button, item.Text);
        button.Click += (_, _) =>
        {
            if (index < _items.Count) _items[index].Invoke?.Invoke();
        };

        return new Line(button) { Icon = icon, Text = title, Shortcut = shortcut, Check = check };
    }

    private static Line NoteLine(string text, double size, Thickness margin)
    {
        var note = Text(text, size);
        note.Margin = margin;
        note.TextWrapping = TextWrapping.Wrap;
        return new Line(note) { Text = note };
    }

    private static TextBlock Text(string text, double size) => new()
    {
        Text = text,
        Style = (Style)Application.Current.Resources["AppTextBase"],
        FontSize = size,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private void Paint()
    {
        for (var index = 0; index < _lines.Count && index < _items.Count; index++)
        {
            var item = _items[index];
            var line = _lines[index];
            switch (item.Kind)
            {
                case OptionKind.Choice:
                    if (line.Icon is { } icon) icon.Foreground = Tint(item);
                    if (line.Text is { } title) title.Foreground = PrimaryBrush;
                    if (line.Shortcut is { } shortcut) shortcut.Foreground = TertiaryBrush;
                    if (line.Check is { } check)
                    {
                        check.Foreground = PrimaryBrush;
                        check.Visibility = item.IsSelected ? Visibility.Visible : Visibility.Collapsed;
                    }

                    break;
                case OptionKind.Separator:
                    ((Rectangle)line.Element).Fill = SeparatorBrush;
                    break;
                case OptionKind.Caption:
                    if (line.Text is { } caption) caption.Foreground = SecondaryBrush;
                    break;
                case OptionKind.Footnote:
                    if (line.Text is { } footnote) footnote.Foreground = TertiaryBrush;
                    break;
            }
        }
    }

    private Brush? Tint(OptionItem item) => item.IconToken switch
    {
        "Project" when item.IconProjectColor is { } color => ProjectColors.Brush(color, ActualTheme),
        "Red" => RedBrush,
        "PriorityMedium" => PriorityMediumBrush,
        "PriorityLow" => PriorityLowBrush,
        "Tertiary" => TertiaryBrush,
        _ => SecondaryBrush,
    };

    /// <summary>The element shown for an item, and the parts that are recoloured.</summary>
    private sealed class Line(UIElement element)
    {
        public UIElement Element { get; } = element;

        public FontIcon? Icon { get; init; }

        public TextBlock? Text { get; init; }

        public TextBlock? Shortcut { get; init; }

        public FontIcon? Check { get; init; }
    }
}
