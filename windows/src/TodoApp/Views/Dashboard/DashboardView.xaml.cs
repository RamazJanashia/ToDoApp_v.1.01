using System.Globalization;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using TodoApp.State;
using TodoCore;
using Typography = Microsoft.UI.Xaml.Documents.Typography;

namespace TodoApp;

/// <summary>
/// The dashboard: header and completion line, four metric tiles, the activity chart, projects, up next and open by
/// priority. The page is rebuilt from <see cref="DashboardModel"/> on every render, as fresh elements, with the brushes
/// of the current theme; nothing on it takes text input. The width it gets (the window's, less the margins, up to 960)
/// picks the layout: tiles in one row from 600, projects and up next side by side from 680.
/// </summary>
public sealed partial class DashboardView : UserControl
{
    private const double MaximumWidth = 960;
    private const double PageMargin = 32;
    private const double OneRowFrom = 600;
    private const double SideBySideFrom = 680;
    private const double PriorityMaximumWidth = 520;

    private AppState? _state;
    private DashboardModel? _model;
    private double _width;

    public DashboardView()
    {
        InitializeComponent();
        Scroller.SizeChanged += (_, e) =>
        {
            var width = Math.Max(Math.Min(e.NewSize.Width - PageMargin * 2, MaximumWidth), 0);
            if (Math.Abs(width - _width) < 0.5) return;
            _width = width;
            Page.Width = width;
            Build();
        };
        ActualThemeChanged += (_, _) => Build();
    }

    public void Render(AppState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _state = state;
        _model = DashboardModel.Build(state);
        Build();
    }

    private Brush Ink(string key) => DotBrushes.Resource(key, ActualTheme);

    /// <summary>A token from the models ("Primary", "Red", "PriorityMedium"…) as a brush.</summary>
    private Brush Token(string token) => Ink(token switch
    {
        "Primary" => "PrimaryTextBrush",
        "Secondary" => "SecondaryTextBrush",
        "Tertiary" => "TertiaryTextBrush",
        _ => token + "Brush",
    });

    private TextBlock Text(string text, string style, string brush) => new()
    {
        Text = text,
        Style = (Style)Application.Current.Resources[style],
        Foreground = Ink(brush),
    };

    private void Build()
    {
        if (_state is not { } state || _model is not { } model || _width <= 0) return;
        var stats = model.Stats;

        Subtitle.Text = model.Subtitle;
        CompletionText.Text = model.CompletionText;
        Progress.Fraction = stats.CompletionRate;

        BuildMetrics(state, model);
        Chart.Render(stats.Activity, state.Clock.Calendar, state.Clock.UtcNow);
        BuildProjects(state, stats);
        BuildUpNext(state, model);
        BuildPriorities(model);
        PrioritySection.Width = Math.Min(_width, PriorityMaximumWidth);
    }

    // Metrics

    /// <summary>Four tiles in a row when there is room, otherwise 2 × 2: never squeezed labels.</summary>
    private void BuildMetrics(AppState state, DashboardModel model)
    {
        MetricsHost.Children.Clear();
        MetricsHost.RowDefinitions.Clear();
        MetricsHost.ColumnDefinitions.Clear();
        var tiles = model.Metrics.Select(m => Tile(state, m)).ToList();

        if (_width >= OneRowFrom)
        {
            MetricsFrame.Height = 96;
            for (var i = 0; i < 4; i++)
            {
                if (i > 0) MetricsHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
                MetricsHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }

            for (var i = 0; i < 4; i++)
            {
                Grid.SetColumn(tiles[i], i * 2);
                MetricsHost.Children.Add(tiles[i]);
                if (i > 0) MetricsHost.Children.Add(VerticalLine(i * 2 - 1, 0, new Thickness(0, 18, 0, 18)));
            }

            return;
        }

        MetricsFrame.Height = 177;
        MetricsHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        MetricsHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
        MetricsHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        MetricsHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(88) });
        MetricsHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1) });
        MetricsHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(88) });
        for (var i = 0; i < 4; i++)
        {
            Grid.SetRow(tiles[i], i / 2 * 2);
            Grid.SetColumn(tiles[i], i % 2 * 2);
            MetricsHost.Children.Add(tiles[i]);
        }

        MetricsHost.Children.Add(VerticalLine(1, 0, new Thickness(0, 16, 0, 16)));
        MetricsHost.Children.Add(VerticalLine(1, 2, new Thickness(0, 16, 0, 16)));
        var horizontal = new Rectangle { Height = 1, Fill = Ink("SeparatorBrush"), IsHitTestVisible = false };
        Grid.SetRow(horizontal, 1);
        Grid.SetColumnSpan(horizontal, 3);
        MetricsHost.Children.Add(horizontal);
    }

    private Rectangle VerticalLine(int column, int row, Thickness margin)
    {
        var line = new Rectangle { Width = 1, Fill = Ink("SeparatorBrush"), Margin = margin, IsHitTestVisible = false };
        Grid.SetColumn(line, column);
        Grid.SetRow(line, row);
        return line;
    }

    private Grid Tile(AppState state, MetricModel metric)
    {
        var value = new TextBlock
        {
            Text = metric.Value.ToString(CultureInfo.CurrentCulture),
            FontSize = 30,
            FontWeight = FontWeights.Light,
            Foreground = Token(metric.ValueToken),
        };
        Typography.SetNumeralAlignment(value, FontNumeralAlignment.Tabular);
        var label = Text(metric.Label, "CaptionText", "SecondaryTextBrush");
        label.MaxLines = 1;

        // Top-aligned so the numbers line up even if a label is longer.
        var tile = new Grid
        {
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            Padding = new Thickness(18, 18, 0, 0),
            Children = { new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Top, Children = { value, label } } },
        };
        tile.PointerEntered += (_, _) => label.Foreground = Ink("PrimaryTextBrush");
        tile.PointerExited += (_, _) => label.Foreground = Ink("SecondaryTextBrush");
        tile.Tapped += (_, _) => state.Select(metric.Target);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(tile, $"{metric.Label}: {metric.Value}");
        return tile;
    }

    // Projects and up next

    private void BuildProjects(AppState state, DashboardStats stats)
    {
        ProjectRows.Children.Clear();
        if (stats.Projects.Count == 0)
        {
            var empty = Text("No projects yet.", "CaptionText", "TertiaryTextBrush");
            empty.Margin = new Thickness(0, 12, 0, 12);
            ProjectRows.Children.Add(empty);
        }

        foreach (var entry in stats.Projects) ProjectRows.Children.Add(ProjectRow(state, entry));
        ApplySplit();
    }

    private void BuildUpNext(AppState state, DashboardModel model)
    {
        UpNextRows.Children.Clear();
        if (model.UpNext.Count == 0)
        {
            var empty = Text("Nothing scheduled.", "CaptionText", "TertiaryTextBrush");
            empty.Margin = new Thickness(0, 12, 0, 12);
            UpNextRows.Children.Add(empty);
        }

        foreach (var row in model.UpNext) UpNextRows.Children.Add(UpNextRow(state, row));
    }

    /// <summary>Side by side, 44 apart, from 680; stacked below that.</summary>
    private void ApplySplit()
    {
        SplitGrid.ColumnDefinitions.Clear();
        SplitGrid.RowDefinitions.Clear();
        if (_width >= SideBySideFrom)
        {
            SplitGrid.ColumnSpacing = 44;
            SplitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            SplitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(ProjectsSection, 0);
            Grid.SetColumn(ProjectsSection, 0);
            Grid.SetRow(UpNextSection, 0);
            Grid.SetColumn(UpNextSection, 1);
            return;
        }

        SplitGrid.ColumnSpacing = 0;
        SplitGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        SplitGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetColumn(ProjectsSection, 0);
        Grid.SetRow(ProjectsSection, 0);
        Grid.SetColumn(UpNextSection, 0);
        Grid.SetRow(UpNextSection, 1);
    }

    /// <summary>A clickable row with the hover fill reaching 8 px past the content on each side.</summary>
    private Border HoverRow(UIElement content, double verticalPadding, Action click)
    {
        var row = new Border
        {
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, verticalPadding, 8, verticalPadding),
            Margin = new Thickness(-8, 0, -8, 0),
            Child = content,
        };
        row.PointerEntered += (_, _) => row.Background = Ink("HoverBrush");
        row.PointerExited += (_, _) => row.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        row.Tapped += (_, _) => click();
        return row;
    }

    private Border ProjectRow(AppState state, ProjectProgress entry)
    {
        var color = ProjectColors.Brush(entry.Project.Color, ActualTheme);
        var count = Text($"{entry.Done} of {entry.Total}", "CaptionText", "SecondaryTextBrush");
        Typography.SetNumeralAlignment(count, FontNumeralAlignment.Tabular);

        var header = new Grid { ColumnSpacing = 10 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = new FontIcon { Glyph = Glyphs.For(entry.Project.Icon), FontSize = 13, Foreground = color, HorizontalAlignment = HorizontalAlignment.Center };
        var name = Text(entry.Project.Name, "BodyText", "PrimaryTextBrush");
        Grid.SetColumn(name, 1);
        Grid.SetColumn(count, 2);
        header.Children.Add(icon);
        header.Children.Add(name);
        header.Children.Add(count);

        var bar = Bar(3, Ink("TrackBrush"), color, entry.Fraction, entry.Done > 0, 0.85);
        bar.Margin = new Thickness(28, 0, 0, 0);
        var content = new StackPanel { Spacing = 8, Children = { header, bar } };
        return HoverRow(content, 11, () => _state?.Select(SidebarItem.Project(entry.Project.Id)));
    }

    private Border UpNextRow(AppState state, UpNextRowModel row)
    {
        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var dot = new Ellipse { Width = 9, Height = 9, Fill = Ink("CheckboxFillBrush"), VerticalAlignment = VerticalAlignment.Center };
        var title = Text(row.Title, "BodyText", "PrimaryTextBrush");
        title.MaxLines = 1;
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        var lines = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center, Children = { title } };
        if (row.ProjectName is { } project) lines.Children.Add(Text(project, "CaptionText", "SecondaryTextBrush"));
        Grid.SetColumn(lines, 1);
        grid.Children.Add(dot);
        grid.Children.Add(lines);

        if (row.DueText is { } due)
        {
            var dueText = Text(due, "CaptionText", row.DueIsOverdue ? "RedBrush" : "SecondaryTextBrush");
            dueText.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(dueText, 2);
            grid.Children.Add(dueText);
        }

        var flag = new PriorityFlag
        {
            RedBrush = Ink("RedBrush"),
            PriorityMediumBrush = Ink("PriorityMediumBrush"),
            PriorityLowBrush = Ink("PriorityLowBrush"),
            TertiaryBrush = Ink("TertiaryTextBrush"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        flag.Show(row.Priority, dimmed: false);
        Grid.SetColumn(flag, 3);
        grid.Children.Add(flag);

        return HoverRow(grid, 9, () => _state?.Reveal(row.Id));
    }

    // Open by priority

    private void BuildPriorities(DashboardModel model)
    {
        PriorityRows.Children.Clear();
        foreach (var bar in model.Priorities)
        {
            var tint = Token(bar.IconToken);
            var grid = new Grid { Height = 30, ColumnSpacing = 12 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });

            var flag = new FontIcon { Glyph = Glyphs.For("flag"), FontSize = 12, Foreground = tint, VerticalAlignment = VerticalAlignment.Center };
            var name = Text(bar.Title, "BodyText", "PrimaryTextBrush");
            name.VerticalAlignment = VerticalAlignment.Center;
            var track = Bar(4, new SolidColorBrush(Microsoft.UI.Colors.Transparent), tint, bar.Fraction, bar.Count > 0, 1);
            track.VerticalAlignment = VerticalAlignment.Center;
            var count = Text(bar.Count.ToString(CultureInfo.CurrentCulture), "CaptionText", "SecondaryTextBrush");
            count.TextAlignment = TextAlignment.Right;
            count.VerticalAlignment = VerticalAlignment.Center;
            Typography.SetNumeralAlignment(count, FontNumeralAlignment.Tabular);
            Grid.SetColumn(name, 1);
            Grid.SetColumn(track, 2);
            Grid.SetColumn(count, 3);
            grid.Children.Add(flag);
            grid.Children.Add(name);
            grid.Children.Add(track);
            grid.Children.Add(count);
            PriorityRows.Children.Add(grid);
        }
    }

    /// <summary>
    /// A capsule bar: a track with a fill <paramref name="fraction"/> of its width, at least 4 px once there is
    /// anything to show.
    /// </summary>
    private static Grid Bar(double height, Brush track, Brush fill, double fraction, bool hasValue, double fillOpacity)
    {
        var radius = new CornerRadius(height / 2);
        var fillBar = new Border { CornerRadius = radius, Background = fill, Opacity = fillOpacity, HorizontalAlignment = HorizontalAlignment.Left };
        var grid = new Grid
        {
            Height = height,
            Children = { new Border { CornerRadius = radius, Background = track }, fillBar },
        };
        grid.SizeChanged += (_, e) => fillBar.Width = Math.Max(hasValue ? 4 : 0, e.NewSize.Width * fraction);
        return grid;
    }
}
