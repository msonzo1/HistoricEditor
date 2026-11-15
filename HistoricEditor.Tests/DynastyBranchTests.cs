using HistoricEditor;
using Xunit;

namespace HistoricEditor.Tests;

// Gens e dinastia separate (2026-11-12, da Traiano): la gens e i suoi rami vanno nel campo Gens, la dinastia resta
// indipendente (non più appesa al ramo della gens, come stabilito il 2026-11-11).
public class DynastyBranchTests
{
    private static readonly ImportedDynasty GensFlavia = new("Gens Flavia", "Q12297672", string.Empty, string.Empty, Gens: true);
    private static readonly ImportedDynasty FlaviiSabini = new("Flavii Sabini", "Q121033352", "Gens Flavia", "Q12297672");
    private static readonly ImportedDynasty DinastiaFlavia = new("Dinastia flavia", "Q200565", string.Empty, string.Empty);

    // Flavii Sabini è un ramo della Gens Flavia: è una gens anche lui. La Dinastia flavia resta una dinastia, senza superiore.
    [Fact]
    public void GensBranch_IsMarkedAsGens_DynastyStaysIndependent()
    {
        var result = WikipediaImportService.MarkGensLine([GensFlavia, DinastiaFlavia, FlaviiSabini]);
        Assert.True(Assert.Single(result, d => d.WikidataId == "Q121033352").Gens);
        var dinastia = Assert.Single(result, d => d.WikidataId == "Q200565");
        Assert.False(dinastia.Gens);
        Assert.Equal(string.Empty, dinastia.ParentWikidataId);
    }

    // Senza gens nulla cambia (Asburgo; Lorena).
    [Fact]
    public void WithoutGens_NothingChanges()
    {
        var asburgo = new ImportedDynasty("Asburgo", "Q2", string.Empty, string.Empty);
        var lorena = new ImportedDynasty("Lorena", "Q3", string.Empty, string.Empty);
        Assert.Equal([asburgo, lorena], WikipediaImportService.MarkGensLine([asburgo, lorena]));
    }

    // Una dinastia con una dinastia superiore che non è una gens resta una dinastia.
    [Fact]
    public void DynastyWithNonGensParent_StaysDynasty()
    {
        var ramo = new ImportedDynasty("Asburgo di Spagna", "Q4", "Asburgo", "Q2");
        var result = WikipediaImportService.MarkGensLine([GensFlavia, ramo]);
        Assert.False(Assert.Single(result, d => d.WikidataId == "Q4").Gens);
    }
}
