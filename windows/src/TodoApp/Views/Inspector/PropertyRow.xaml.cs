using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using TodoApp.State;
using TodoCore;

namespace TodoApp;

/// <summary>
/// One property of the task details (PropertyRow in InspectorView.swift): a 14 px icon tinted by its token and the value,
/// in tertiary ink while it's a placeholder. Clicking opens <see cref="FlyoutContent"/> in a flyout to the left; the row
/// keeps the selection fill while it's open.
/// </summary>
public sealed partial class PropertyRow : UserControl
{
    private static readonly HashSet<string> Tints = ["Secondary", "Tertiary", "Red", "Green", "PriorityMedium", "PriorityLow", "Project"];

    private string _tint = "Secondary";
    private ProjectColor? _projectColor;

    public PropertyRow()
    {
        InitializeComponent();
        Popover.Opening += (_, _) =>
        {
            // The flyout lives in the popup layer, outside the window's content, so it doesn't inherit a forced theme.
            Surface.RequestedTheme = ActualTheme;
            VisualStateManager.GoToState(this, "Open", false);
            FlyoutOpening?.Invoke(this, EventArgs.Empty);
        };
        Popover.Closed += (_, _) => VisualStateManager.GoToState(this, "Closed", false);
        ActualThemeChanged += (_, _) => ApplyTint();
    }

    /// <summary>Raised just before the flyout shows, so the host can fill it for the current task.</summary>
    public event EventHandler? FlyoutOpening;

    /// <summary>What the flyout shows, on the panel surface. Each content sets its own width and padding.</summary>
    public UIElement? FlyoutContent
    {
        get => Surface.Child;
        set => Surface.Child = value;
    }

    public bool IsFlyoutOpen => Popover.IsOpen;

    public void CloseFlyout() => Popover.Hide();

    public void Show(PropertyRowModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        IconGlyph.Glyph = TintGlyph.Glyph = Glyphs.For(model.Icon);
        ValueText.Text = model.Text;
        VisualStateManager.GoToState(this, model.IsPlaceholder ? "Placeholder" : "Value", false);
        AutomationProperties.SetName(RowButton, model.Text);
        _tint = Tints.Contains(model.IconToken) ? model.IconToken : "Secondary";
        _projectColor = model.IconProjectColor;
        ApplyTint();
    }

    private void ApplyTint()
    {
        if (_tint == "Project" && _projectColor is { } color)
        {
            TintGlyph.Foreground = ProjectColors.Brush(color, ActualTheme);
            VisualStateManager.GoToState(this, "TintProject", false);
            return;
        }

        VisualStateManager.GoToState(this, _tint == "Project" ? "TintSecondary" : "Tint" + _tint, false);
    }
}
