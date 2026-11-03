using TodoCore;

namespace TodoCore.Tests;

public class ProjectTreeTests
{
    private static readonly Project Loner = new() { Name = "Loner" };
    private static readonly Project Parent = new() { Name = "Parent" };
    private static readonly Project Child = new() { Name = "Child", ParentId = Parent.Id };
    private static readonly Project Grandchild = new() { Name = "Grandchild", ParentId = Child.Id };

    private static List<Project> Tree() => new() { Loner, Parent, Child, Grandchild };

    [Fact]
    public void Outline_ParentsAboveChildren()
    {
        var outline = Tree().Outline();
        Assert.Equal(new[] { "Loner", "Parent", "Child", "Grandchild" }, outline.Select(r => r.Project.Name));
        Assert.Equal(new[] { 0, 0, 1, 2 }, outline.Select(r => r.Depth));
        Assert.Equal(new[] { false, true, true, false }, outline.Select(r => r.HasChildren));
    }

    [Fact]
    public void Outline_ChildListedBeforeParentStillNests()
    {
        var outline = new List<Project> { Grandchild, Child, Parent }.Outline();
        Assert.Equal(new[] { "Parent", "Child", "Grandchild" }, outline.Select(r => r.Project.Name));
    }

    [Fact]
    public void Outline_CollapsedHidesEverythingBelow()
    {
        Assert.Equal(new[] { "Loner", "Parent" }, Tree().Outline(new HashSet<Guid> { Parent.Id }).Select(r => r.Project.Name));
        Assert.Equal(4, Tree().Outline(new HashSet<Guid> { Loner.Id }).Count);
    }

    [Fact]
    public void SubtreeIds_IncludesSelfAndDescendants() =>
        Assert.Equal(new HashSet<Guid> { Parent.Id, Child.Id, Grandchild.Id }, Tree().SubtreeIds(Parent.Id));

    [Fact]
    public void ParentChoices_ExcludeOwnSubtree()
    {
        Assert.Equal(new[] { Loner.Id }, Tree().ParentChoices(Parent.Id).Select(r => r.Id));
        Assert.Equal(4, Tree().ParentChoices(null).Count);
    }

    [Fact]
    public void Outline_MissingParentIsTopLevel()
    {
        var orphan = new Project { Name = "Orphan", ParentId = Guid.NewGuid() };
        Assert.Equal(0, new List<Project> { orphan }.Outline().Single().Depth);
    }

    [Fact]
    public void Outline_ParentLoopShowsEachOnce()
    {
        var aId = Guid.NewGuid();
        var bId = Guid.NewGuid();
        var loop = new List<Project>
        {
            new() { Id = aId, Name = "A", ParentId = bId },
            new() { Id = bId, Name = "B", ParentId = aId },
        };
        Assert.Equal(2, loop.Outline().Count);
        Assert.Equal(2, loop.SubtreeIds(aId).Count);
    }

    [Fact]
    public void MoveProject_BesideSubprojectJoinsItsParent()
    {
        var projects = Tree();
        Assert.True(projects.MoveProject(Loner.Id, Child.Id));
        var moved = projects.Single(p => p.Id == Loner.Id);
        Assert.Equal(Parent.Id, moved.ParentId);
        Assert.Equal(projects.FindIndex(p => p.Id == Child.Id) - 1, projects.FindIndex(p => p.Id == Loner.Id));
    }

    [Fact]
    public void MoveProject_IntoOwnSubtreeIsRefused()
    {
        var projects = Tree();
        Assert.False(projects.MoveProject(Parent.Id, Grandchild.Id));
        Assert.Equal(Tree(), projects);
    }
}
