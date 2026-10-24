using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace TodoApp.State.Tests;

public sealed class CollectionSyncTests
{
    private sealed record Item(int Id, string Text = "");

    private static ObservableCollection<Item> Of(params int[] ids) => new(ids.Select(i => new Item(i)));

    private static IReadOnlyList<Item> Source(params int[] ids) => ids.Select(i => new Item(i)).ToList();

    private static List<NotifyCollectionChangedAction> Record(ObservableCollection<Item> target)
    {
        var actions = new List<NotifyCollectionChangedAction>();
        target.CollectionChanged += (_, e) => actions.Add(e.Action);
        return actions;
    }

    [Fact]
    public void CollectionSync_MinimalChanges()
    {
        var target = Of(1, 2, 3);
        var originals = target.ToList();
        var actions = Record(target);

        CollectionSync.Sync(target, Source(1, 2, 3), i => i.Id);

        Assert.Empty(actions);
        for (var i = 0; i < 3; i++) Assert.Same(originals[i], target[i]);
    }

    [Fact]
    public void Reorder_UsesMove()
    {
        var target = Of(1, 2, 3);
        var originals = target.ToList();
        var actions = Record(target);

        CollectionSync.Sync(target, Source(3, 1, 2), i => i.Id);

        Assert.Equal(new[] { NotifyCollectionChangedAction.Move }, actions);
        Assert.Equal(new[] { 3, 1, 2 }, target.Select(i => i.Id).ToArray());
        Assert.Same(originals[2], target[0]);
        Assert.Same(originals[0], target[1]);
    }

    [Fact]
    public void RemovedAndAddedItems_AreRemovedAndInserted()
    {
        var target = Of(1, 2, 3);
        var kept = target[0];
        var actions = Record(target);

        CollectionSync.Sync(target, Source(1, 3, 4), i => i.Id);

        Assert.Equal(new[] { NotifyCollectionChangedAction.Remove, NotifyCollectionChangedAction.Add }, actions);
        Assert.Equal(new[] { 1, 3, 4 }, target.Select(i => i.Id).ToArray());
        Assert.Same(kept, target[0]);
    }

    [Fact]
    public void ChangedItem_IsReplacedInPlace()
    {
        var target = Of(1, 2, 3);
        var untouched = target[2];
        var actions = Record(target);

        CollectionSync.Sync(target, new[] { new Item(1), new Item(2, "edited"), new Item(3) }, i => i.Id);

        Assert.Equal(new[] { NotifyCollectionChangedAction.Replace }, actions);
        Assert.Equal("edited", target[1].Text);
        Assert.Same(untouched, target[2]);
    }

    [Fact]
    public void InsertInTheMiddle_OnlyAddsOne()
    {
        var target = Of(1, 3);
        var actions = Record(target);

        CollectionSync.Sync(target, Source(1, 2, 3), i => i.Id);

        Assert.Equal(new[] { NotifyCollectionChangedAction.Add }, actions);
        Assert.Equal(new[] { 1, 2, 3 }, target.Select(i => i.Id).ToArray());
    }

    [Fact]
    public void FromAndToEmpty()
    {
        var target = Of();
        CollectionSync.Sync(target, Source(5, 6), i => i.Id);
        Assert.Equal(new[] { 5, 6 }, target.Select(i => i.Id).ToArray());

        CollectionSync.Sync(target, Source(), i => i.Id);
        Assert.Empty(target);
    }

    [Fact]
    public void AlwaysEndsEqualToTheSource()
    {
        var random = new Random(42);
        for (var round = 0; round < 200; round++)
        {
            var before = Enumerable.Range(0, 8).OrderBy(_ => random.Next()).Take(random.Next(0, 9)).ToArray();
            var after = Enumerable.Range(0, 10).OrderBy(_ => random.Next()).Take(random.Next(0, 11)).ToArray();
            var target = Of(before);

            CollectionSync.Sync(target, Source(after), i => i.Id);

            Assert.Equal(after, target.Select(i => i.Id).ToArray());
        }
    }
}
