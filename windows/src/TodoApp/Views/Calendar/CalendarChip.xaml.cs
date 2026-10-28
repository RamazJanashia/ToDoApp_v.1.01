using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TodoApp.State;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI.Text;

namespace TodoApp;

/// <summary>
/// One task on a day cell. A click selects it, a double-click opens its details, a drag reschedules it onto another
/// day, and right-click opens <see cref="TaskContextMenu"/>. The dot is green when done, red for high priority, the
/// project's colour, or grey. Chips are built fresh by their day cell each time it is drawn.
/// </summary>
public sealed partial class CalendarChip : UserControl
{
    private readonly AppState _state;
    private readonly CalendarChipModel _model;
    private bool _hovering;

    public CalendarChip(AppState state, CalendarChipModel model)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(model);
        _state = state;
        _model = model;
        InitializeComponent();

        TitleText.Text = model.Title;
        TitleText.TextDecorations = model.IsCompleted ? TextDecorations.Strikethrough : TextDecorations.None;
        Root.ContextFlyout = TaskContextMenu.Build(model.Task, state);
        AutomationProperties.SetName(this, model.Title);
        ApplyDot();
        UpdateStates();

        ActualThemeChanged += (_, _) => ApplyDot();
        Root.PointerEntered += (_, _) => SetHovering(true);
        Root.PointerExited += (_, _) => SetHovering(false);
        Root.Tapped += (_, e) =>
        {
            e.Handled = true;
            _state.HandleClick(_model.Id, isDoubleClick: false);
        };
        // A double-click on a chip opens its details, and is not a double-click on the day behind it.
        Root.DoubleTapped += (_, e) =>
        {
            e.Handled = true;
            _state.HandleClick(_model.Id, isDoubleClick: true);
        };
        Root.DragStarting += (_, args) =>
        {
            args.Data.SetText(_model.Id.ToString("D").ToUpperInvariant());
            args.AllowedOperations = DataPackageOperation.Move | DataPackageOperation.Copy;
        };
    }

    private void SetHovering(bool hovering)
    {
        _hovering = hovering;
        UpdateStates();
    }

    private void UpdateStates()
    {
        VisualStateManager.GoToState(this, _model.IsSelected ? "ChipSelected" : _hovering ? "ChipHover" : "ChipNormal", false);
        VisualStateManager.GoToState(this, _model.IsCompleted ? "TitleDone" : "TitleOpen", false);
    }

    private void ApplyDot() => Dot.Fill = DotBrushes.For(_model, ActualTheme);
}

/// <summary>The colours of calendar dots, which depend on the theme.</summary>
internal static class DotBrushes
{
    public static Brush For(CalendarChipModel chip, ElementTheme theme) => chip.DotToken switch
    {
        "Green" => Resource("GreenBrush", theme),
        "Red" => Resource("RedBrush", theme),
        "Project" when chip.DotProjectColor is { } color => ProjectColors.Brush(color, theme),
        _ => Resource("SecondaryTextBrush", theme),
    };

    /// <summary>A brush from Theme/Colors.xaml for the theme ("Default" holds the dark values).</summary>
    public static Brush Resource(string key, ElementTheme theme)
    {
        var themeKey = theme == ElementTheme.Light ? "Light" : "Default";
        foreach (var dictionary in Application.Current.Resources.MergedDictionaries)
        {
            if (dictionary.ThemeDictionaries.TryGetValue(themeKey, out var themed)
                && themed is ResourceDictionary resources
                && resources.TryGetValue(key, out var value)
                && value is Brush brush)
            {
                return brush;
            }
        }

        throw new KeyNotFoundException($"No {themeKey} brush named {key}");
    }
}
