using HistoricEditor;
using Xunit;

namespace HistoricEditor.Tests;

public class DynastyTreeTests
{
    [Fact]
    public void Build_NestsBranches_SortedByName()
    {
        var tree = DynastyTree.Build(
        [
            new Dinastia { Id = 1, Nome = "Asburgo" },
            new Dinastia { Id = 2, Nome = "Asburgo di Spagna", ParentId = 1 },
            new Dinastia { Id = 3, Nome = "Asburgo d'Austria", ParentId = 1 },
            new Dinastia { Id = 4, Nome = "Aviz" },
            new Dinastia { Id = 5, Nome = "Borbone" },
        ]);

        Assert.Equal(new[] { "Asburgo", "Aviz", "Borbone" }, tree.Select(n => n.Nome));
        Assert.Equal(new[] { "Asburgo d'Austria", "Asburgo di Spagna" }, tree[0].Rami.Select(n => n.Nome));
    }

    [Fact]
    public void Build_KeepsDynastiesInCycle_AsRoots()
    {
        var tree = DynastyTree.Build(
        [
            new Dinastia { Id = 1, Nome = "A", ParentId = 2 },
            new Dinastia { Id = 2, Nome = "B", ParentId = 1 },
        ]);

        Assert.Equal(2, tree.Sum(r => r.SelfAndDescendants().Count()));
    }
}
