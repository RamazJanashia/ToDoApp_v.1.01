using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TodoApp.State;
using TodoCore;
using Windows.ApplicationModel.DataTransfer;
using TaskStatus = TodoCore.TaskStatus;

namespace TodoApp;

/// <summary>
/// A board column. Dropping a card anywhere in it (but not on another card) sets that status; the "+" shows while the
/// pointer is over the column and starts a new task in it. The cards themselves are owned and placed by
/// <see cref="BoardView"/>, which moves them between columns.
/// </summary>
public sealed partial class BoardColumn : UserControl
{
    private AppState? _state;
    private TaskStatus _status;

    public BoardColumn()
    {
        InitializeComponent();
        AddButton.Content = Glyphs.For("plus");

        Root.PointerEntered += (_, _) => AddButton.Opacity = 1;
        Root.PointerExited += (_, _) => AddButton.Opacity = 0;
        AddButton.Click += (_, _) => _state?.CreateDraftTask(status: _status);
        Root.DragEnter += OnDragOver;
        Root.DragOver += OnDragOver;
        Root.DragLeave += (_, _) => TargetBorder.Visibility = Visibility.Collapsed;
        Root.Drop += OnDrop;
    }

    /// <summary>Where this column's cards go.</summary>
    public Panel CardPanel => Cards;

    public void Render(AppState state, BoardColumnModel model)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(model);
        _state = state;
        _status = model.Status;

        StatusIcon.Glyph = Glyphs.For(model.Icon);
        VisualStateManager.GoToState(this, model.Status == TaskStatus.Done ? "IconDone" : "IconNormal", false);
        TitleText.Text = model.Title;
        CountText.Text = model.Cards.Count.ToString(CultureInfo.CurrentCulture);
        EmptyText.Text = model.EmptyText;
        EmptyText.Visibility = model.Cards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ToolTipService.SetToolTip(AddButton, "Add to " + model.Title);
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.Text)) return;
        e.AcceptedOperation = DataPackageOperation.Move;
        e.DragUIOverride.IsCaptionVisible = false;
        e.DragUIOverride.IsGlyphVisible = false;
        e.Handled = true;
        TargetBorder.Visibility = Visibility.Visible;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        TargetBorder.Visibility = Visibility.Collapsed;
        var state = _state;
        if (state is null || !e.DataView.Contains(StandardDataFormats.Text)) return;
        e.Handled = true;

        var deferral = e.GetDeferral();
        try
        {
            var text = await e.DataView.GetTextAsync();
            var items = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            state.HandleDropOnStatus(items, _status);
        }
        catch (Exception ex)
        {
            AppLog.Error("Couldn't read what was dropped on a column", ex);
        }
        finally
        {
            deferral.Complete();
        }
    }
}
