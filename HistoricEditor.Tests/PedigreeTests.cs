using HistoricEditor;
using Xunit;

namespace HistoricEditor.Tests;

public class PedigreeTests
{
    private static readonly Dictionary<int, (int? MotherId, int? FatherId)> Parents = new()
    {
        [1] = (3, 2),
        [2] = (null, 4),
        [3] = (null, null),
        [4] = (null, null),
    };

    private static IReadOnlyList<IReadOnlyList<PedigreeSlot>> Build()
        => Pedigree.Build(
            1,
            id => Parents.ContainsKey(id) ? new PersonSummary(id, $"P{id}", string.Empty, string.Empty) : null,
            id => Parents.GetValueOrDefault(id));

    [Fact]
    public void Build_HasFiveGenerationsWithDoublingSlots()
    {
        var tree = Build();

        Assert.Equal(5, tree.Count);
        Assert.Equal([1, 2, 4, 8, 16], tree.Select(g => g.Count));
    }

    [Fact]
    public void Build_PlacesFatherBeforeMotherAndLinksChild()
    {
        var parents = Build()[1];

        Assert.Equal(2, parents[0].Person?.Id);
        Assert.False(parents[0].IsMother);
        Assert.Equal(3, parents[1].Person?.Id);
        Assert.True(parents[1].IsMother);
        Assert.All(parents, slot => Assert.Equal(1, slot.ChildId));
    }

    [Fact]
    public void Build_MissingParentOfKnownPersonCanBeAdded()
    {
        var grandparents = Build()[2];

        Assert.Equal(4, grandparents[0].Person?.Id);
        Assert.True(grandparents[1].CanAdd);
        Assert.Equal(2, grandparents[1].ChildId);
    }

    [Fact]
    public void Build_SlotsAboveMissingPeopleAreNotAddable()
    {
        var greatGrandparents = Build()[3];

        // Il ramo della nonna paterna (non inserita) non ha antenati da aggiungere.
        Assert.False(greatGrandparents[2].CanAdd);
        Assert.False(greatGrandparents[3].CanAdd);
        Assert.True(greatGrandparents[0].CanAdd);
    }

    [Theory]
    [InlineData(1, 0, "Padre")]
    [InlineData(1, 1, "Madre")]
    [InlineData(2, 1, "Nonna paterna")]
    [InlineData(2, 2, "Nonno materno")]
    [InlineData(4, 15, "Trisnonna materna")]
    public void DescribeRole_NamesAncestors(int generation, int index, string expected)
        => Assert.Equal(expected, Pedigree.DescribeRole(generation, index));
}
