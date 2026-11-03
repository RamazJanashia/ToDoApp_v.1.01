using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using TodoApp.State;

namespace TodoApp;

/// <summary>
/// The round task checkbox (CheckCircle in Components.swift): a soft grey disc when open, which brightens and shows a
/// tick on hover, and a green ring and tick when done. A tap raises <see cref="Toggled"/> and never reaches the row.
/// Hosts set the brushes with ThemeResource so they follow the theme.
/// </summary>
public sealed partial class CheckCircle : Grid
{
    public static readonly DependencyProperty OpenBrushProperty = RegisterBrush(nameof(OpenBrush));
    public static readonly DependencyProperty OpenHoverBrushProperty = RegisterBrush(nameof(OpenHoverBrush));
    public static readonly DependencyProperty DoneBrushProperty = RegisterBrush(nameof(DoneBrush));
    public static readonly DependencyProperty TickBrushProperty = RegisterBrush(nameof(TickBrush));

    private readonly Ellipse _disc = new();
    private readonly FontIcon _tick = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly ScaleTransform _scale = new() { ScaleX = 1, ScaleY = 1 };
    private static readonly Windows.UI.ViewManagement.UISettings Settings = new();

    private readonly Storyboard _pulse = new();
    private bool _isDone;
    private double _size = 20;
    private bool _hovering;

    public CheckCircle()
    {
        // Transparent rather than null, so the whole circle takes the pointer even when only a ring is drawn.
        Background = new SolidColorBrush(Colors.Transparent);
        RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
        RenderTransform = _scale;
        _tick.Glyph = Glyphs.For("checkmark");
        Children.Add(_disc);
        Children.Add(_tick);
        BuildPulse();
        Resize();
        UpdateLabel();

        PointerEntered += (_, _) => SetHovering(true);
        PointerExited += (_, _) => SetHovering(false);
        Tapped += OnTapped;
        // A double-click on the circle is two toggles' worth of clicking, not "open details".
        DoubleTapped += (_, e) => e.Handled = true;
        Update();
    }

    /// <summary>Raised when the circle is clicked.</summary>
    public event EventHandler? Toggled;

    /// <summary>The open disc (CheckboxFill).</summary>
    public Brush? OpenBrush
    {
        get => (Brush?)GetValue(OpenBrushProperty);
        set => SetValue(OpenBrushProperty, value);
    }

    /// <summary>The open disc under the pointer (CheckboxHover).</summary>
    public Brush? OpenHoverBrush
    {
        get => (Brush?)GetValue(OpenHoverBrushProperty);
        set => SetValue(OpenHoverBrushProperty, value);
    }

    /// <summary>The ring and tick of a done task (Green).</summary>
    public Brush? DoneBrush
    {
        get => (Brush?)GetValue(DoneBrushProperty);
        set => SetValue(DoneBrushProperty, value);
    }

    /// <summary>The hover tick on an open disc, drawn at 0.7 opacity (Background).</summary>
    public Brush? TickBrush
    {
        get => (Brush?)GetValue(TickBrushProperty);
        set => SetValue(TickBrushProperty, value);
    }

    public bool IsDone
    {
        get => _isDone;
        set
        {
            if (_isDone == value) return;
            _isDone = value;
            UpdateLabel();
            Update();
        }
    }

    /// <summary>The diameter: 20 in the list, 16 on board cards.</summary>
    public double Size
    {
        get => _size;
        set
        {
            _size = value;
            Resize();
            Update();
        }
    }

    private static DependencyProperty RegisterBrush(string name) => DependencyProperty.Register(
        name,
        typeof(Brush),
        typeof(CheckCircle),
        new PropertyMetadata(null, (d, _) => ((CheckCircle)d).Update()));

    private void SetHovering(bool hovering)
    {
        _hovering = hovering;
        Update();
    }

    private void OnTapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = true;
        // With animations turned off in Windows (Reduce Motion on the Mac), the change just happens.
        if (Settings.AnimationsEnabled)
        {
            _pulse.Stop();
            _pulse.Begin();
        }

        Toggled?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>A quick squeeze to 0.86 and back, as the Mac's spring does.</summary>
    private void BuildPulse()
    {
        foreach (var property in new[] { nameof(ScaleTransform.ScaleX), nameof(ScaleTransform.ScaleY) })
        {
            var animation = new DoubleAnimation
            {
                To = 0.86,
                Duration = new Duration(TimeSpan.FromMilliseconds(90)),
                AutoReverse = true,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            };
            Storyboard.SetTarget(animation, _scale);
            Storyboard.SetTargetProperty(animation, property);
            _pulse.Children.Add(animation);
        }
    }

    private void Resize()
    {
        Width = Height = _size;
        _disc.Width = _disc.Height = _size;
    }

    private void UpdateLabel()
    {
        var label = _isDone ? "Mark as Incomplete" : "Complete";
        ToolTipService.SetToolTip(this, label);
        AutomationProperties.SetName(this, label);
    }

    private void Update()
    {
        if (_isDone)
        {
            _disc.Fill = null;
            _disc.Stroke = DoneBrush;
            _disc.StrokeThickness = 1.6;
            _tick.Visibility = Visibility.Visible;
            _tick.FontSize = _size * 0.46;
            _tick.Foreground = DoneBrush;
            _tick.Opacity = 1;
        }
        else
        {
            _disc.Fill = _hovering ? OpenHoverBrush : OpenBrush;
            _disc.Stroke = null;
            _disc.StrokeThickness = 0;
            _tick.Visibility = _hovering ? Visibility.Visible : Visibility.Collapsed;
            _tick.FontSize = _size * 0.44;
            _tick.Foreground = TickBrush;
            _tick.Opacity = 0.7;
        }
    }
}
