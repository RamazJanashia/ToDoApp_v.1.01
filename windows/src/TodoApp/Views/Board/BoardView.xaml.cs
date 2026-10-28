using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TodoApp.State;
using TodoCore;

namespace TodoApp;

/// <summary>
/// To Do · In Progress · Done. Columns share the width equally (at least 210 each, 14 apart); when they don't fit the
/// board scrolls sideways and a fade shows at the right edge. Cards are kept per task and moved between columns, so
/// a status change doesn't rebuild the board.
/// </summary>
public sealed partial class BoardView : UserControl
{
    private const double Spacing = 14;
    private const double PageMargin = 32;
    private const double MinimumColumnWidth = 210;

    private readonly Dictionary<Guid, BoardCard> _cards = [];
    private readonly BoardColumn[] _columns;

    public BoardView()
    {
        InitializeComponent();
        _columns = [TodoColumn, InProgressColumn, DoneColumn];
        Root.SizeChanged += (_, _) => Layout();
    }

    public void Render(AppState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var model = BoardModel.Build(state);

        var wanted = model.Columns.SelectMany(c => c.Cards).Select(c => c.Id).ToHashSet();
        foreach (var gone in _cards.Keys.Where(id => !wanted.Contains(id)).ToList())
        {
            if (_cards[gone].Parent is Panel parent) parent.Children.Remove(_cards[gone]);
            _cards.Remove(gone);
        }

        for (var i = 0; i < _columns.Length && i < model.Columns.Count; i++)
        {
            var column = _columns[i];
            var columnModel = model.Columns[i];
            column.Render(state, columnModel);
            var tasks = columnModel.Cards.Select(c => c.Task).ToList();
            var panel = column.CardPanel;

            for (var index = 0; index < columnModel.Cards.Count; index++)
            {
                var row = columnModel.Cards[index];
                _cards.TryGetValue(row.Id, out var card);
                var inPlace = card is not null && index < panel.Children.Count && ReferenceEquals(panel.Children[index], card);
                if (!inPlace)
                {
                    // WinUI refuses to put an element back once it has been taken out of a panel, so a card that
                    // has to move (another column, or another position) is replaced by a fresh one.
                    if (card?.Parent is Panel old) old.Children.Remove(card);
                    card = new BoardCard();
                    _cards[row.Id] = card;
                    panel.Children.Insert(Math.Min(index, panel.Children.Count), card);
                }

                card!.Bind(state, row, columnModel.Status, () => tasks);
            }

            // What is left after the wanted cards belongs to another column now; its own pass places it there.
            while (panel.Children.Count > columnModel.Cards.Count) panel.Children.RemoveAt(panel.Children.Count - 1);
        }

        Layout();
    }

    private void Layout()
    {
        var width = Root.ActualWidth;
        var height = Root.ActualHeight;
        if (width <= 0 || height <= 0) return;

        var available = width - PageMargin * 2 - Spacing * 2;
        var columnWidth = Math.Max(MinimumColumnWidth, available / 3);
        foreach (var column in _columns) column.Width = columnWidth;
        Columns.Height = height;
        Fade.Visibility = columnWidth * 3 + Spacing * 2 + PageMargin * 2 > width + 1 ? Visibility.Visible : Visibility.Collapsed;
    }
}
