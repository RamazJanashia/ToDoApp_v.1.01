using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TodoApp.State;
using TodoCore;

namespace TodoApp;

/// <summary>
/// The 13 px flag of a task with a priority (PriorityFlag in Components.swift); nothing at all for No Priority.
/// The tint follows <see cref="PriorityStyle.Token"/>, or tertiary ink when dimmed (a done task). Hosts set the brushes
/// with ThemeResource so they follow the theme.
/// </summary>
public sealed partial class PriorityFlag : Grid
{
    public static readonly DependencyProperty RedBrushProperty = RegisterBrush(nameof(RedBrush));
    public static readonly DependencyProperty PriorityMediumBrushProperty = RegisterBrush(nameof(PriorityMediumBrush));
    public static readonly DependencyProperty PriorityLowBrushProperty = RegisterBrush(nameof(PriorityLowBrush));
    public static readonly DependencyProperty TertiaryBrushProperty = RegisterBrush(nameof(TertiaryBrush));

    private readonly FontIcon _icon = new() { FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
    private Priority _priority = Priority.None;
    private Priority? _labelled;
    private bool _dimmed;

    public PriorityFlag()
    {
        _icon.Glyph = Glyphs.For("flag");
        Children.Add(_icon);
        Show(Priority.None, false);
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

    public Brush? TertiaryBrush
    {
        get => (Brush?)GetValue(TertiaryBrushProperty);
        set => SetValue(TertiaryBrushProperty, value);
    }

    public void Show(Priority priority, bool dimmed)
    {
        _priority = priority;
        _dimmed = dimmed;
        Visibility = priority == Priority.None ? Visibility.Collapsed : Visibility.Visible;
        if (_labelled != priority)
        {
            _labelled = priority;
            var label = $"{priority.Title()} priority";
            ToolTipService.SetToolTip(this, label);
            AutomationProperties.SetName(this, label);
        }

        Paint();
    }

    private static DependencyProperty RegisterBrush(string name) => DependencyProperty.Register(
        name,
        typeof(Brush),
        typeof(PriorityFlag),
        new PropertyMetadata(null, (d, _) => ((PriorityFlag)d).Paint()));

    private void Paint() => _icon.Foreground = _dimmed
        ? TertiaryBrush
        : PriorityStyle.Token(_priority) switch
        {
            "Red" => RedBrush,
            "PriorityMedium" => PriorityMediumBrush,
            "PriorityLow" => PriorityLowBrush,
            _ => TertiaryBrush,
        };
}
