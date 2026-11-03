using TodoCore;
using static TodoApp.State.Tests.TestSupport;
using TaskStatus = TodoCore.TaskStatus;

namespace TodoApp.State.Tests;

public sealed class BoardModelTests : IDisposable
{
    private readonly PresentationFixture _f = new();

    public void Dispose() => _f.Dispose();

    [Fact]
    public void ThreeColumnsInStatusOrder()
    {
        var model = BoardModel.Build(_f.State);

        Assert.Equal(new[] { TaskStatus.Todo, TaskStatus.InProgress, TaskStatus.Done }, model.Columns.Select(c => c.Status).ToArray());
        Assert.Equal(new[] { "To Do", "In Progress", "Done" }, model.Columns.Select(c => c.Title).ToArray());
        Assert.Equal(new[] { "circle", "circle.lefthalf.filled", "checkmark.circle" }, model.Columns.Select(c => c.Icon).ToArray());
    }

    [Fact]
    public void EmptyTexts()
    {
        var model = BoardModel.Build(_f.State);

        Assert.Equal(new[] { "No tasks", "No tasks", "Drop finished work here" }, model.Columns.Select(c => c.EmptyText).ToArray());
        Assert.All(model.Columns, c => Assert.Empty(c.Cards));
    }

    [Fact]
    public void CardsLandInTheirStatusColumn()
    {
        var a = _f.Store.CreateTask("A");
        var b = _f.Store.CreateTask("B", status: TaskStatus.InProgress);
        var c = _f.Store.CreateTask("C", status: TaskStatus.Done);
        var d = _f.Store.CreateTask("D");
        _f.State.Select(SidebarItem.AllTasks);
        _f.State.SelectedTaskId = b.Id;

        var model = BoardModel.Build(_f.State);

        Assert.Equal(new[] { a.Id, d.Id }, model.Columns[0].Cards.Select(x => x.Id).ToArray());
        Assert.Equal(b.Id, Assert.Single(model.Columns[1].Cards).Id);
        Assert.True(model.Columns[1].Cards[0].IsSelected);
        Assert.Equal(c.Id, Assert.Single(model.Columns[2].Cards).Id);
        Assert.False(model.Columns[0].Cards[0].IsSelected);
    }

    [Fact]
    public void Cards_KeepTheirDueDateInTheTodayScope()
    {
        _f.Store.CreateTask("A", null, _f.Today);
        _f.State.Select(SidebarItem.Today);

        var card = Assert.Single(BoardModel.Build(_f.State).Columns[0].Cards);

        Assert.Equal("Today", card.Meta.DueText);
    }
}
