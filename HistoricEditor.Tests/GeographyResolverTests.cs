using System.IO;
using HistoricEditor;
using Xunit;

namespace HistoricEditor.Tests;

public class GeographyResolverTests : IDisposable
{
    private readonly string dbPath;
    private readonly AppDbContext context;

    public GeographyResolverTests()
    {
        dbPath = Path.Combine(Path.GetTempPath(), $"historic-test-{Guid.NewGuid():N}.db");
        context = new AppDbContext(dbPath);
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        context.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            File.Delete(dbPath);
        }
        catch (IOException)
        {
            // Il file temporaneo verrà ripulito dal sistema.
        }
    }

    [Fact]
    public void DeriveLevels_UnknownCountry_IsNotConfident()
    {
        var place = new HistoricalPlaceRecord { WikidataId = "Qx", CurrentName = "Foo", CurrentCountry = "Narnia" };

        var result = GeographyResolver.DeriveLevels(context, place);

        Assert.False(result.Confident);
        Assert.Equal(3, result.Levels.Count);
        Assert.Equal("Narnia", result.Levels[0].Nome);
        Assert.Equal("Foo", result.Levels[^1].Nome);
    }

    [Fact]
    public void DeriveLevels_FullItalianChain_IsConfident()
    {
        var place = new HistoricalPlaceRecord
        {
            WikidataId = "Qcomune",
            CurrentName = "Firenze",
            CurrentCountry = "Italia",
            AdministrativeDivisions =
            [
                new PlaceDivision { Relation = "Unità amministrativa (P131)", WikidataId = "Qprov", SubjectWikidataId = "Qcomune", Name = "Città metropolitana di Firenze" },
                new PlaceDivision { Relation = "Unità amministrativa (P131)", WikidataId = "Qreg", SubjectWikidataId = "Qprov", Name = "Toscana" },
                new PlaceDivision { Relation = "Unità amministrativa (P131)", WikidataId = "Qit", SubjectWikidataId = "Qreg", Name = "Italia" },
                new PlaceDivision { Relation = "Paese (P17)", WikidataId = "Qit", SubjectWikidataId = "Qcomune", Name = "Italia" },
            ],
        };

        var result = GeographyResolver.DeriveLevels(context, place);

        Assert.True(result.Confident);
        Assert.Null(result.SpecificName);
        Assert.Equal(new[] { "Italia", "Toscana", "Città metropolitana di Firenze", "Firenze" }, result.Levels.Select(l => l.Nome));
    }

    [Fact]
    public void DeriveLevels_Portugal_UsesInfoboxDivisions()
    {
        var place = new HistoricalPlaceRecord
        {
            WikidataId = "Qporto",
            CurrentName = "Porto",
            CurrentCountry = "Portogallo",
            InfoboxDivisions = ["Nord", "Porto"],
            AdministrativeDivisions =
            [
                new PlaceDivision { Relation = "Unità amministrativa (P131)", WikidataId = "Qdouro", SubjectWikidataId = "Qporto", Name = "Douro Litorale" },
            ],
        };

        var result = GeographyResolver.DeriveLevels(context, place);

        Assert.True(result.Confident);
        Assert.Equal(new[] { "Portogallo", "Nord", "Porto", "Porto" }, result.Levels.Select(l => l.Nome));
    }

    [Fact]
    public void DeriveLevels_Portugal_MarksMissingDistrictAsAbsent()
    {
        var place = new HistoricalPlaceRecord
        {
            WikidataId = "Qfunchal",
            CurrentName = "Funchal",
            CurrentCountry = "Portogallo",
            InfoboxDivisions = ["Madera", "no"],
        };

        var result = GeographyResolver.DeriveLevels(context, place);

        Assert.Equal("Madera", result.Levels[1].Nome);
        Assert.True(result.Levels[2].Assente);
        Assert.Equal("Funchal", result.Levels[^1].Nome);
    }

    [Fact]
    public void DeriveLevels_CarriesCoordinatesToLeaf()
    {
        var place = new HistoricalPlaceRecord
        {
            WikidataId = "Qcomune",
            CurrentName = "Firenze",
            CurrentCountry = "Italia",
            Latitudine = 43.77,
            Longitudine = 11.25,
            AdministrativeDivisions =
            [
                new PlaceDivision { Relation = "Paese (P17)", WikidataId = "Qit", SubjectWikidataId = "Qcomune", Name = "Italia" },
            ],
        };

        var result = GeographyResolver.DeriveLevels(context, place);

        Assert.Equal(43.77, result.Levels[^1].Latitudine);
        Assert.Equal(11.25, result.Levels[^1].Longitudine);
    }

    [Fact]
    public void EnsureChain_PersistsLevelCoordinates()
    {
        List<GeoLevel> levels =
        [
            new("Stato", "Italia"),
            new("Regione", "Toscana"),
            new("Provincia", "Firenze"),
            new("Comune", "Firenze") { Latitudine = 43.77, Longitudine = 11.25 },
        ];

        var leaf = GeographyResolver.EnsureChain(context, levels);

        Assert.NotNull(leaf);
        Assert.Equal(43.77, leaf!.Latitudine);
        Assert.Equal(11.25, leaf.Longitudine);
    }
}
