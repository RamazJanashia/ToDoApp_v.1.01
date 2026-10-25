using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TodoApp.State;
using Windows.Foundation;

namespace TodoApp;

/// <summary>
/// Project · due date · repeat · reminder · subtasks · attachments · notes, in quiet grey (TaskMetaLine in
/// TaskRowView.swift). When the line is too narrow it drops the project name first, then the small icons, and never
/// truncates the due text, like the Mac's ViewThatFits. Collapsed when there is nothing to show. Hosts set the brushes
/// with ThemeResource.
/// </summary>
public sealed partial class MetaLine : Panel
{
    public static readonly DependencyProperty TextBrushProperty = RegisterBrush(nameof(TextBrush));
    public static readonly DependencyProperty OverdueBrushProperty = RegisterBrush(nameof(OverdueBrush));

    private const double Spacing = 10;
    private const double LabelSpacing = 4;
    private const double IconSize = 10.5;

    private readonly List<UIElement> _icons = [];
    private UIElement? _project;
    private UIElement? _due;
    private bool _dueIsOverdue;
    private TaskMetaInfo? _meta;
    private Fit _fit;

    public MetaLine()
    {
        Visibility = Visibility.Collapsed;
    }

    /// <summary>What can be left out, tried in this order until the line fits.</summary>
    private enum Fit { Everything, WithoutProject, DueOnly }

    /// <summary>The text and icons (SecondaryText).</summary>
    public Brush? TextBrush
    {
        get => (Brush?)GetValue(TextBrushProperty);
        set => SetValue(TextBrushProperty, value);
    }

    /// <summary>An overdue due date, drawn at 0.9 opacity (Red).</summary>
    public Brush? OverdueBrush
    {
        get => (Brush?)GetValue(OverdueBrushProperty);
        set => SetValue(OverdueBrushProperty, value);
    }

    public void Show(TaskMetaInfo meta)
    {
        ArgumentNullException.ThrowIfNull(meta);
        if (meta == _meta) return;
        _meta = meta;

        Children.Clear();
        _icons.Clear();
        _project = null;
        _due = null;
        _dueIsOverdue = meta.DueIsOverdue;

        if (meta.ProjectName is { } project)
        {
            _project = Text(project);
            Children.Add(_project);
        }

        if (meta.DueText is { } due)
        {
            _due = Label("calendar", due);
            if (meta.DueIsOverdue) _due.Opacity = 0.9;
            Children.Add(_due);
        }

        if (meta.RepeatSummary is { } summary)
        {
            var repeat = Icon("repeat");
            ToolTipService.SetToolTip(repeat, summary);
            _icons.Add(repeat);
        }

        if (meta.HasReminder) _icons.Add(Icon("bell"));
        if (meta.SubtaskProgress is { } progress) _icons.Add(Label("checklist", progress));
        if (meta.AttachmentCount > 0)
        {
            _icons.Add(Label("paperclip", meta.AttachmentCount.ToString(CultureInfo.CurrentCulture)));
        }

        if (meta.ShowsNotesIcon) _icons.Add(Icon("text.alignleft"));
        foreach (var icon in _icons) Children.Add(icon);

        Paint();
        Visibility = meta.HasAnything ? Visibility.Visible : Visibility.Collapsed;
        InvalidateMeasure();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var unlimited = new Size(double.PositiveInfinity, double.PositiveInfinity);
        var height = 0.0;
        foreach (var child in Children)
        {
            child.Measure(unlimited);
            height = Math.Max(height, child.DesiredSize.Height);
        }

        // The first arrangement that fits, or the shortest one when none does.
        _fit = Fit.DueOnly;
        foreach (var fit in new[] { Fit.Everything, Fit.WithoutProject })
        {
            if (WidthOf(fit) <= availableSize.Width)
            {
                _fit = fit;
                break;
            }
        }

        return new Size(Math.Min(WidthOf(_fit), availableSize.Width), height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var x = 0.0;
        foreach (var child in Children)
        {
            if (!IsShown(child, _fit))
            {
                // A zero slot clips the element away without changing its visibility, which would re-trigger layout.
                child.Arrange(new Rect(0, 0, 0, 0));
                continue;
            }

            var size = child.DesiredSize;
            var width = Math.Max(0, Math.Min(size.Width, finalSize.Width - x));
            child.Arrange(new Rect(x, (finalSize.Height - size.Height) / 2, width, size.Height));
            x += size.Width + Spacing;
        }

        return finalSize;
    }

    private static DependencyProperty RegisterBrush(string name) => DependencyProperty.Register(
        name,
        typeof(Brush),
        typeof(MetaLine),
        new PropertyMetadata(null, (d, _) => ((MetaLine)d).Paint()));

    private bool IsShown(UIElement child, Fit fit)
    {
        if (child == _project) return fit == Fit.Everything;
        if (child == _due) return true;

        // The small icons stay when there is no due date to make room for.
        return fit != Fit.DueOnly || _due is null;
    }

    private double WidthOf(Fit fit)
    {
        var width = 0.0;
        var count = 0;
        foreach (var child in Children)
        {
            if (!IsShown(child, fit)) continue;
            width += child.DesiredSize.Width;
            count++;
        }

        return count == 0 ? 0 : width + Spacing * (count - 1);
    }

    private void Paint()
    {
        foreach (var child in Children)
        {
            Paint(child, child == _due && _dueIsOverdue ? OverdueBrush : TextBrush);
        }
    }

    private static void Paint(UIElement element, Brush? brush)
    {
        switch (element)
        {
            case TextBlock text:
                text.Foreground = brush;
                break;
            case FontIcon icon:
                icon.Foreground = brush;
                break;
            case StackPanel panel:
                foreach (var child in panel.Children) Paint(child, brush);
                break;
        }
    }

    private static TextBlock Text(string text) => new()
    {
        Text = text,
        Style = (Style)Application.Current.Resources["CaptionText"],
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static FontIcon Icon(string symbol) => new()
    {
        Glyph = Glyphs.For(symbol),
        FontSize = IconSize,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>An icon and its text, 4 apart (CompactLabelStyle).</summary>
    private static StackPanel Label(string symbol, string text)
    {
        var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = LabelSpacing };
        label.Children.Add(Icon(symbol));
        label.Children.Add(Text(text));
        return label;
    }
}
