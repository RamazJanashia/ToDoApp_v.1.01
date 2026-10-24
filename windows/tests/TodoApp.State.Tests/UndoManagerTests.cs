namespace TodoApp.State.Tests;

public class UndoManagerTests
{
    [Fact]
    public void Register_ThenUndo_RunsAction()
    {
        var manager = new UndoManager();
        var ran = 0;
        manager.Register("Edit", () => ran++);
        Assert.True(manager.CanUndo);
        Assert.Equal(0, ran);

        manager.Undo();

        Assert.Equal(1, ran);
        Assert.False(manager.CanUndo);
    }

    [Fact]
    public void UndoRegistersRedo()
    {
        var manager = new UndoManager();
        var value = 0;
        void Set(int to)
        {
            var old = value;
            value = to;
            manager.Register("Set", () => Set(old));
        }

        Set(1);
        manager.Undo();
        Assert.Equal(0, value);
        Assert.True(manager.CanRedo);
        Assert.False(manager.CanUndo);

        manager.Redo();
        Assert.Equal(1, value);
        Assert.True(manager.CanUndo);
        Assert.False(manager.CanRedo);

        manager.Undo();
        Assert.Equal(0, value);
    }

    [Fact]
    public void NewRegistration_ClearsRedo()
    {
        var manager = new UndoManager();
        manager.Register("A", () => manager.Register("A", () => { }));
        manager.Undo();
        Assert.True(manager.CanRedo);

        manager.Register("B", () => { });

        Assert.False(manager.CanRedo);
        Assert.Equal("B", manager.UndoActionName);
    }

    [Fact]
    public void ActionNames_Follow_Stacks()
    {
        var manager = new UndoManager();
        Assert.Null(manager.UndoActionName);
        Assert.Null(manager.RedoActionName);

        void Register(string name) => manager.Register(name, () => Register(name));
        Register("First");
        Register("Second");
        Assert.Equal("Second", manager.UndoActionName);
        Assert.Null(manager.RedoActionName);

        manager.Undo();
        Assert.Equal("First", manager.UndoActionName);
        Assert.Equal("Second", manager.RedoActionName);

        manager.Redo();
        Assert.Equal("Second", manager.UndoActionName);
        Assert.Null(manager.RedoActionName);
    }

    [Fact]
    public void Changed_FiresOncePerOperation()
    {
        var manager = new UndoManager();
        var fired = 0;
        manager.Changed += (_, _) => fired++;
        manager.Register("A", () => manager.Register("A", () => { }));
        Assert.Equal(1, fired);

        manager.Undo();
        Assert.Equal(2, fired);

        manager.Redo();
        Assert.Equal(3, fired);
    }
}
