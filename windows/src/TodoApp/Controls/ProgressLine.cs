using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace TodoApp;

/// <summary>
/// The thin completion bar under a page title (ProgressLine in Components.swift): a 4 px capsule track with a green
/// fill at 0.85 opacity, at least 4 px long once anything is done. Hosts set the brushes with ThemeResource.
/// </summary>
public sealed partial class ProgressLine : Grid
{
    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush),
        typeof(Brush),
        typeof(ProgressLine),
        new PropertyMetadata(null, (d, e) => ((ProgressLine)d)._track.Background = (Brush?)e.NewValue));

    public static readonly DependencyProperty FillBrushProperty = DependencyProperty.Register(
        nameof(FillBrush),
        typeof(Brush),
        typeof(ProgressLine),
        new PropertyMetadata(null, (d, e) => ((ProgressLine)d)._fill.Background = (Brush?)e.NewValue));

    private const double BarHeight = 4;

    private readonly Border _track = new() { CornerRadius = new CornerRadius(BarHeight / 2) };
    private readonly Border _fill = new()
    {
        CornerRadius = new CornerRadius(BarHeight / 2),
        HorizontalAlignment = HorizontalAlignment.Left,
        Opacity = 0.85,
    };

    private double _fraction;

    public ProgressLine()
    {
        Height = BarHeight;
        Children.Add(_track);
        Children.Add(_fill);
        SizeChanged += (_, _) => UpdateFill();
        Fraction = 0;
    }

    /// <summary>The track, in Track ink.</summary>
    public Brush? TrackBrush
    {
        get => (Brush?)GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    /// <summary>The done part, in Green.</summary>
    public Brush? FillBrush
    {
        get => (Brush?)GetValue(FillBrushProperty);
        set => SetValue(FillBrushProperty, value);
    }

    /// <summary>Done over total, 0…1.</summary>
    public double Fraction
    {
        get => _fraction;
        set
        {
            _fraction = double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
            UpdateFill();
            AutomationProperties.SetName(
                this,
                string.Create(CultureInfo.CurrentCulture, $"{(int)Math.Round(_fraction * 100, MidpointRounding.AwayFromZero)} percent complete"));
        }
    }

    private void UpdateFill() => _fill.Width = Math.Max(_fraction > 0 ? BarHeight : 0, ActualWidth * _fraction);
}
