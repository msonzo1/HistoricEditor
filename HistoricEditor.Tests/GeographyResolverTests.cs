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
    public void EnsureChain_ReusesPlacesIgnoringCase()
    {
        var first = GeographyResolver.EnsureChain(context, [new GeoLevel { Tipo = "Stato", Nome = "Italia" }, new GeoLevel { Tipo = "Comune", Nome = "Milano" }]);
        var second = GeographyResolver.EnsureChain(context, [new GeoLevel { Tipo = "stato", Nome = "ITALIA" }, new GeoLevel { Tipo = "Comune", Nome = "milano" }]);

        Assert.Equal(first!.Id, second!.Id);
        Assert.Equal(2, context.Luoghi.Count());
    }

    [Fact]
    public void EnsureChain_FailureLeavesNoPartialChain()
    {
        Assert.ThrowsAny<Exception>(() => GeographyResolver.EnsureChain(context, [new GeoLevel { Tipo = "Stato", Nome = "Italia" }, new GeoLevel { Tipo = null!, Nome = "Milano" }]));

        using var fresh = new AppDbContext(dbPath);
        Assert.Empty(fresh.Luoghi);
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

    private static PlaceDivision P131(string subject, string id, string name, params string[] types)
        => new() { Relation = "Unità amministrativa (P131)", WikidataId = id, SubjectWikidataId = subject, Name = name, TypeIds = [.. types] };

    private static PlaceDivision P17(string subject, string id, string name)
        => new() { Relation = "Paese (P17)", WikidataId = id, SubjectWikidataId = subject, Name = name };

    [Fact]
    public void DeriveLevels_UnitedKingdomTypes_LondonHasNoSingleDistrict()
    {
        var place = new HistoricalPlaceRecord
        {
            WikidataId = "Q84", CurrentName = "Londra", CurrentCountry = "Regno Unito", TypeIds = ["Q515"],
            AdministrativeDivisions =
            [
                P131("Q84", "Q23306", "Grande Londra", "Q159313", "Q180673"),
                P131("Q23306", "Q23939248", "Londra", "Q48091"),
                P131("Q23939248", "Q21", "Inghilterra", "Q3336843"),
                P17("Q84", "Q145", "Regno Unito"),
            ],
        };

        var result = GeographyResolver.DeriveLevels(context, place);

        Assert.False(result.Confident);
        Assert.Equal(new[] { "Regno Unito", "Inghilterra", "Grande Londra", "", "Londra" }, result.Levels.Select(l => l.Nome));
        Assert.True(result.Levels[3].Assente);
    }

    [Fact]
    public void DeriveLevels_UnitedKingdomTypes_UnitaryAuthorityIsDistrictInCeremonialCounty()
    {
        var place = new HistoricalPlaceRecord
        {
            WikidataId = "Q131491", CurrentName = "Brighton", CurrentCountry = "Regno Unito", TypeIds = ["Q7897276", "Q18511725"],
            AdministrativeDivisions =
            [
                P131("Q131491", "Q1022488", "Brighton and Hove", "Q515", "Q1006876", "Q1136601", "Q21503295"),
                P131("Q1022488", "Q23293", "East Sussex", "Q180673"),
                P131("Q23293", "Q48015", "Inghilterra sud-orientale", "Q48091"),
                P131("Q48015", "Q21", "Inghilterra", "Q3336843"),
                P17("Q131491", "Q145", "Regno Unito"),
            ],
        };

        var result = GeographyResolver.DeriveLevels(context, place);

        Assert.True(result.Confident);
        Assert.Equal(new[] { "Regno Unito", "Inghilterra", "East Sussex", "Brighton and Hove", "Brighton" }, result.Levels.Select(l => l.Nome));
    }

    [Fact]
    public void DeriveLevels_UnitedKingdomTypes_DistrictBelowHomonymousCounty()
    {
        var place = new HistoricalPlaceRecord
        {
            WikidataId = "Q170263", CurrentName = "Chester", CurrentCountry = "Regno Unito", TypeIds = ["Q515", "Q1357964"],
            AdministrativeDivisions =
            [
                P131("Q170263", "Q1070591", "Cheshire West and Chester", "Q769603", "Q1006876", "Q1136601", "Q1187580"),
                P131("Q1070591", "Q23064", "Cheshire", "Q180673"),
                P131("Q23064", "Q47967", "Inghilterra nord-occidentale", "Q48091"),
                P131("Q47967", "Q21", "Inghilterra", "Q3336843"),
                P17("Q170263", "Q145", "Regno Unito"),
            ],
        };

        var result = GeographyResolver.DeriveLevels(context, place);

        Assert.True(result.Confident);
        Assert.Equal(new[] { "Regno Unito", "Inghilterra", "Cheshire", "Cheshire West and Chester", "Chester" }, result.Levels.Select(l => l.Nome));
    }

    [Fact]
    public void DeriveLevels_Edinburgh_IsConfident()
    {
        var place = new HistoricalPlaceRecord
        {
            WikidataId = "Qedi",
            CurrentName = "Edimburgo",
            CurrentCountry = "Regno Unito",
            AdministrativeDivisions =
            [
                P131("Qedi", "Qcouncil", "City of Edinburgh"),
                P131("Qcouncil", "Q22", "Scozia"),
                P17("Qedi", "Q145", "Regno Unito"),
            ],
        };

        var result = GeographyResolver.DeriveLevels(context, place);

        Assert.True(result.Confident);
        Assert.Equal(new[] { "Regno Unito", "Scozia", "City of Edinburgh", "", "Edimburgo" }, result.Levels.Select(l => l.Nome));
    }

    [Fact]
    public void DeriveLevels_AustrianStatutoryCity_MarksDistrictAbsent()
    {
        var place = new HistoricalPlaceRecord
        {
            WikidataId = "Qlinz",
            CurrentName = "Linz",
            CurrentCountry = "Austria",
            AdministrativeDivisions =
            [
                P131("Qlinz", "Qoo", "Alta Austria"),
                P131("Qoo", "Q40", "Austria"),
                P17("Qlinz", "Q40", "Austria"),
            ],
        };

        var result = GeographyResolver.DeriveLevels(context, place);

        Assert.True(result.Confident);
        Assert.Equal("Alta Austria", result.Levels[1].Nome);
        Assert.True(result.Levels[2].Assente);
        Assert.Equal("Linz", result.Levels[^1].Nome);
    }

    [Fact]
    public void DeriveLevels_Vienna_IsLandAndCity()
    {
        var place = new HistoricalPlaceRecord
        {
            WikidataId = "Q1741",
            CurrentName = "Vienna",
            CurrentCountry = "Austria",
            AdministrativeDivisions = [P17("Q1741", "Q40", "Austria")],
        };

        var result = GeographyResolver.DeriveLevels(context, place);

        Assert.True(result.Confident);
        Assert.Equal("Vienna", result.Levels[1].Nome);
        Assert.True(result.Levels[2].Assente);
        Assert.Equal("Vienna", result.Levels[^1].Nome);
    }

    [Fact]
    public void DeriveLevels_Cognac_SkipsMetropolitanFrance()
    {
        var place = new HistoricalPlaceRecord
        {
            WikidataId = "Q285",
            CurrentName = "Cognac",
            CurrentCountry = "Francia",
            AdministrativeDivisions =
            [
                P131("Q285", "Q701022", "arrondissement di Cognac"),
                P131("Q701022", "Q3266", "Charente"),
                P131("Q3266", "Q18678082", "Nuova Aquitania"),
                P131("Q18678082", "Q212429", "Francia metropolitana"),
                P131("Q212429", "Q142", "Francia"),
                P17("Q285", "Q142", "Francia"),
            ],
        };

        var result = GeographyResolver.DeriveLevels(context, place);

        Assert.True(result.Confident);
        Assert.Null(result.SpecificName);
        Assert.Equal(new[] { "Francia", "Nuova Aquitania", "Charente", "arrondissement di Cognac", "Cognac" }, result.Levels.Select(l => l.Nome));
    }

    [Fact]
    public void DeriveLevels_ItalyTypes_LiberoConsorzio()
    {
        var place = new HistoricalPlaceRecord
        {
            WikidataId = "Q430988", CurrentName = "Alessandria della Rocca", CurrentCountry = "Italia", TypeIds = ["Q747074"],
            AdministrativeDivisions =
            [
                P131("Q430988", "Q26160406", "Libero consorzio comunale di Agrigento", "Q21190155"),
                P131("Q26160406", "Q1460", "Sicilia", "Q1710033", "Q3502482"),
                P131("Q1460", "Q38", "Italia"),
                P17("Q430988", "Q38", "Italia"),
            ],
        };

        var result = GeographyResolver.DeriveLevels(context, place);

        Assert.True(result.Confident);
        Assert.Equal(new[] { "Italia", "Sicilia", "Libero consorzio comunale di Agrigento", "Alessandria della Rocca" }, result.Levels.Select(l => l.Nome));
    }

    [Fact]
    public void DeriveLevels_ItalyTypes_SkipsUnknownIntermediate()
    {
        var place = new HistoricalPlaceRecord
        {
            WikidataId = "Qc", CurrentName = "Bergamo", CurrentCountry = "Italia", TypeIds = ["Q747074"],
            AdministrativeDivisions =
            [
                P131("Qc", "Qx", "Unione dei comuni", "Q1110"),
                P131("Qx", "Qp", "provincia di Bergamo", "Q15089"),
                P131("Qp", "Qr", "Lombardia", "Q16110"),
                P17("Qc", "Q38", "Italia"),
            ],
        };

        var result = GeographyResolver.DeriveLevels(context, place);

        Assert.True(result.Confident);
        Assert.Null(result.SpecificName);
        Assert.Equal(new[] { "Italia", "Lombardia", "provincia di Bergamo", "Bergamo" }, result.Levels.Select(l => l.Nome));
    }

    [Fact]
    public void DeriveLevels_ItalyTypes_AostaHasNoProvince()
    {
        var place = new HistoricalPlaceRecord
        {
            WikidataId = "Q1977", CurrentName = "Aosta", CurrentCountry = "Italia", TypeIds = ["Q747074"],
            AdministrativeDivisions =
            [
                P131("Q1977", "Q1222", "Valle d'Aosta", "Q1710033"),
                P17("Q1977", "Q38", "Italia"),
            ],
        };

        var result = GeographyResolver.DeriveLevels(context, place);

        Assert.True(result.Confident);
        Assert.True(result.Levels[2].Assente);
        Assert.Equal("Aosta", result.Levels[^1].Nome);
    }

    [Fact]
    public void DeriveLevels_ItalyTypes_NonComuneUsesComuneAsLeaf()
    {
        var place = new HistoricalPlaceRecord
        {
            WikidataId = "Qpal", CurrentName = "Reggia di Caserta", CurrentCountry = "Italia", TypeIds = ["Q53536964"],
            AdministrativeDivisions =
            [
                P131("Qpal", "Qc", "Caserta", "Q747074"),
                P131("Qc", "Qp", "provincia di Caserta", "Q15089"),
                P131("Qp", "Qr", "Campania", "Q16110"),
                P17("Qpal", "Q38", "Italia"),
            ],
        };

        var result = GeographyResolver.DeriveLevels(context, place);

        Assert.True(result.Confident);
        Assert.Equal("Reggia di Caserta", result.SpecificName);
        Assert.Equal(new[] { "Italia", "Campania", "provincia di Caserta", "Caserta" }, result.Levels.Select(l => l.Nome));
    }

    [Fact]
    public void DeriveLevels_FranceTypes_ParisIsCommuneAndDepartment()
    {
        var place = new HistoricalPlaceRecord
        {
            WikidataId = "Q90", CurrentName = "Parigi", CurrentCountry = "Francia", TypeIds = ["Q484170", "Q22923920", "Q200250"],
            AdministrativeDivisions =
            [
                P131("Q90", "Q16665915", "metropoli della Grande Parigi", "Q18706073"),
                P131("Q16665915", "Q13917", "Île-de-France", "Q36784"),
                P131("Q13917", "Q212429", "Francia metropolitana", "Q192498"),
                P131("Q212429", "Q142", "Francia"),
                P17("Q90", "Q142", "Francia"),
            ],
        };

        var result = GeographyResolver.DeriveLevels(context, place);

        Assert.True(result.Confident);
        Assert.Equal(new[] { "Francia", "Île-de-France", "Parigi", "", "Parigi" }, result.Levels.Select(l => l.Nome));
        Assert.True(result.Levels[3].Assente);
    }

    [Fact]
    public void DeriveLevels_FranceTypes_LyonMetropolis()
    {
        var place = new HistoricalPlaceRecord
        {
            WikidataId = "Q456", CurrentName = "Lione", CurrentCountry = "Francia", TypeIds = ["Q484170", "Q22927616"],
            AdministrativeDivisions =
            [
                P131("Q456", "Q16665897", "Metropoli di Lione", "Q22923920", "Q200250"),
                P131("Q16665897", "Q18338206", "Alvernia-Rodano-Alpi", "Q36784"),
                P131("Q18338206", "Q142", "Francia"),
                P17("Q456", "Q142", "Francia"),
            ],
        };

        var result = GeographyResolver.DeriveLevels(context, place);

        Assert.True(result.Confident);
        Assert.Equal(new[] { "Francia", "Alvernia-Rodano-Alpi", "Metropoli di Lione", "", "Lione" }, result.Levels.Select(l => l.Nome));
    }

    [Fact]
    public void DeriveLevels_FranceTypes_CorsicaUsesDepartment()
    {
        var place = new HistoricalPlaceRecord
        {
            WikidataId = "Q40104", CurrentName = "Ajaccio", CurrentCountry = "Francia", TypeIds = ["Q484170", "Q515"],
            AdministrativeDivisions =
            [
                P131("Q40104", "Q700367", "arrondissement di Ajaccio", "Q194203"),
                P131("Q700367", "Q3336", "Corsica del Sud", "Q6465"),
                P131("Q3336", "Q14112", "Corsica", "Q36784", "Q22923920"),
                P131("Q14112", "Q142", "Francia"),
                P17("Q40104", "Q142", "Francia"),
            ],
        };

        var result = GeographyResolver.DeriveLevels(context, place);

        Assert.True(result.Confident);
        Assert.Equal(new[] { "Francia", "Corsica", "Corsica del Sud", "arrondissement di Ajaccio", "Ajaccio" }, result.Levels.Select(l => l.Nome));
    }

    [Fact]
    public void DeriveLevels_GermanyTypes_DistrictTownSkipsRegierungsbezirk()
    {
        var place = new HistoricalPlaceRecord
        {
            WikidataId = "Q3920", CurrentName = "Lüneburg", CurrentCountry = "Germania", TypeIds = ["Q1548525", "Q42744322"],
            AdministrativeDivisions =
            [
                P131("Q3920", "Q5910", "circondario di Lüneburg", "Q85332736"),
                P131("Q5910", "Qrb", "distretto governativo di Lüneburg", "Q22721"),
                P131("Qrb", "Q1197", "Bassa Sassonia", "Q1221156"),
                P131("Q1197", "Q183", "Germania"),
                P17("Q3920", "Q183", "Germania"),
            ],
        };

        var result = GeographyResolver.DeriveLevels(context, place);

        Assert.True(result.Confident);
        Assert.Null(result.SpecificName);
        Assert.Equal(new[] { "Germania", "Bassa Sassonia", "circondario di Lüneburg", "Lüneburg" }, result.Levels.Select(l => l.Nome));
    }

    [Fact]
    public void DeriveLevels_GermanyTypes_IndependentCityIsDistrictAndMunicipality()
    {
        var place = new HistoricalPlaceRecord
        {
            WikidataId = "Q1726", CurrentName = "Monaco di Baviera", CurrentCountry = "Germania", TypeIds = ["Q1549591", "Q42744322", "Q85631896"],
            AdministrativeDivisions =
            [
                P131("Q1726", "Q10562", "Alta Baviera", "Q22721"),
                P131("Q10562", "Q980", "Baviera", "Q1221156"),
                P17("Q1726", "Q183", "Germania"),
            ],
        };

        var result = GeographyResolver.DeriveLevels(context, place);

        Assert.True(result.Confident);
        Assert.Equal(new[] { "Germania", "Baviera", "Monaco di Baviera", "Monaco di Baviera" }, result.Levels.Select(l => l.Nome));
    }

    [Fact]
    public void DeriveLevels_GermanyTypes_BerlinIsLandDistrictAndMunicipality()
    {
        var place = new HistoricalPlaceRecord
        {
            WikidataId = "Q64", CurrentName = "Berlino", CurrentCountry = "Germania", TypeIds = ["Q1221156", "Q114401982", "Q42744322"],
            AdministrativeDivisions = [P17("Q64", "Q183", "Germania")],
        };

        var result = GeographyResolver.DeriveLevels(context, place);

        Assert.True(result.Confident);
        Assert.Equal(new[] { "Germania", "Berlino", "Berlino", "Berlino" }, result.Levels.Select(l => l.Nome));
    }

    [Fact]
    public void DeriveLevels_GermanyTypes_CastleUsesMunicipalityAsLeaf()
    {
        var place = new HistoricalPlaceRecord
        {
            WikidataId = "Qcastle", CurrentName = "Castello di Heidelberg", CurrentCountry = "Germania", TypeIds = ["Q23413"],
            AdministrativeDivisions =
            [
                P131("Qcastle", "Q2966", "Heidelberg", "Q42744322", "Q2327515"),
                P131("Q2966", "Q8178", "distretto governativo di Karlsruhe", "Q22721"),
                P131("Q8178", "Q985", "Baden-Württemberg", "Q1221156"),
                P17("Qcastle", "Q183", "Germania"),
            ],
        };

        var result = GeographyResolver.DeriveLevels(context, place);

        Assert.True(result.Confident);
        Assert.Equal("Castello di Heidelberg", result.SpecificName);
        Assert.Equal(new[] { "Germania", "Baden-Württemberg", "Heidelberg", "Heidelberg" }, result.Levels.Select(l => l.Nome));
    }

    [Fact]
    public void DeriveLevels_SpainTypes_SkipsComarca()
    {
        var place = new HistoricalPlaceRecord
        {
            WikidataId = "Q1492", CurrentName = "Barcellona", CurrentCountry = "Spagna", TypeIds = ["Q515", "Q33146843"],
            AdministrativeDivisions =
            [
                P131("Q1492", "Q15348", "Barcelonès", "Q937876"),
                P131("Q15348", "Q81949", "provincia di Barcellona", "Q162620", "Q4379648"),
                P131("Q81949", "Q5705", "Catalogna", "Q10742", "Q2736168"),
                P131("Q5705", "Q29", "Spagna"),
                P17("Q1492", "Q29", "Spagna"),
            ],
        };

        var result = GeographyResolver.DeriveLevels(context, place);

        Assert.True(result.Confident);
        Assert.Null(result.SpecificName);
        Assert.Equal(new[] { "Spagna", "Catalogna", "provincia di Barcellona", "Barcellona" }, result.Levels.Select(l => l.Nome));
    }

    [Fact]
    public void DeriveLevels_SpainTypes_MadridHasNoProvince()
    {
        var place = new HistoricalPlaceRecord
        {
            WikidataId = "Q2807", CurrentName = "Madrid", CurrentCountry = "Spagna", TypeIds = ["Q2074737"],
            AdministrativeDivisions =
            [
                P131("Q2807", "Q5756", "Comunità di Madrid", "Q10742"),
                P17("Q2807", "Q29", "Spagna"),
            ],
        };

        var result = GeographyResolver.DeriveLevels(context, place);

        Assert.True(result.Confident);
        Assert.Equal("Comunità di Madrid", result.Levels[1].Nome);
        Assert.True(result.Levels[2].Assente);
        Assert.Equal("Madrid", result.Levels[^1].Nome);
    }

    [Fact]
    public void DeriveLevels_SpainTypes_CeutaIsCommunityAndMunicipality()
    {
        var place = new HistoricalPlaceRecord
        {
            WikidataId = "Q5823", CurrentName = "Ceuta", CurrentCountry = "Spagna", TypeIds = ["Q16532593"],
            AdministrativeDivisions = [P17("Q5823", "Q29", "Spagna")],
        };

        var result = GeographyResolver.DeriveLevels(context, place);

        Assert.True(result.Confident);
        Assert.Equal("Ceuta", result.Levels[1].Nome);
        Assert.True(result.Levels[2].Assente);
        Assert.Equal("Ceuta", result.Levels[^1].Nome);
    }

    [Fact]
    public void DeriveLevels_PortugalTypes_MadeiraWithoutInfobox()
    {
        var place = new HistoricalPlaceRecord
        {
            WikidataId = "Q25444", CurrentName = "Funchal", CurrentCountry = "Portogallo", TypeIds = ["Q13217644"],
            AdministrativeDivisions =
            [
                P131("Q25444", "Q26253", "Madera", "Q192611", "Q2914565", "Q3775649"),
                P17("Q25444", "Q45", "Portogallo"),
            ],
        };

        var result = GeographyResolver.DeriveLevels(context, place);

        Assert.True(result.Confident);
        Assert.Equal("Madera", result.Levels[1].Nome);
        Assert.True(result.Levels[2].Assente);
        Assert.Equal("Funchal", result.Levels[^1].Nome);
    }

    [Fact]
    public void DeriveLevels_PortugalTypes_MainlandWithoutInfoboxIsNotConfident()
    {
        var place = new HistoricalPlaceRecord
        {
            WikidataId = "Q83247", CurrentName = "Braga", CurrentCountry = "Portogallo", TypeIds = ["Q13217644"],
            AdministrativeDivisions =
            [
                P131("Q83247", "Q326203", "distretto di Braga", "Q192611", "Q3032141"),
                P131("Q326203", "Q45", "Portogallo"),
                P17("Q83247", "Q45", "Portogallo"),
            ],
        };

        var result = GeographyResolver.DeriveLevels(context, place);

        Assert.False(result.Confident);
        Assert.True(result.Levels[1].Assente);
        Assert.Equal("distretto di Braga", result.Levels[2].Nome);
        Assert.Equal("Braga", result.Levels[^1].Nome);
    }

    [Fact]
    public void DeriveLevels_BelgiumTypes_PrefersRegionOverCountryParent()
    {
        var place = new HistoricalPlaceRecord
        {
            WikidataId = "Q12892", CurrentName = "Anversa", CurrentCountry = "Belgio", TypeIds = ["Q493522", "Q15273785"],
            AdministrativeDivisions =
            [
                P131("Q12892", "Q90895", "arrondissement di Anversa", "Q91028"),
                P131("Q90895", "Q1116", "provincia di Anversa", "Q83116", "Q15051068"),
                P131("Q1116", "Q31", "Belgio"),
                P131("Q1116", "Q9337", "Regione fiamminga", "Q83057"),
                P131("Q9337", "Q31", "Belgio"),
                P17("Q12892", "Q31", "Belgio"),
            ],
        };

        var result = GeographyResolver.DeriveLevels(context, place);

        Assert.True(result.Confident);
        Assert.Equal(new[] { "Belgio", "Regione fiamminga", "provincia di Anversa", "arrondissement di Anversa", "Anversa" }, result.Levels.Select(l => l.Nome));
    }

    [Fact]
    public void DeriveLevels_BelgiumTypes_BrusselsHasNoProvince()
    {
        var place = new HistoricalPlaceRecord
        {
            WikidataId = "Q239", CurrentName = "Bruxelles", CurrentCountry = "Belgio", TypeIds = ["Q493522", "Q1549591"],
            AdministrativeDivisions =
            [
                P131("Q239", "Q90870", "arrondissement di Bruxelles-Capitale", "Q91028"),
                P131("Q90870", "Q89959", "Comunità francese", "Q89934"),
                P131("Q90870", "Q240", "Regione di Bruxelles-Capitale", "Q83057"),
                P131("Q89959", "Q31", "Belgio"),
                P131("Q240", "Q31", "Belgio"),
                P17("Q239", "Q31", "Belgio"),
            ],
        };

        var result = GeographyResolver.DeriveLevels(context, place);

        Assert.True(result.Confident);
        Assert.Equal("Regione di Bruxelles-Capitale", result.Levels[1].Nome);
        Assert.True(result.Levels[2].Assente);
        Assert.Equal("arrondissement di Bruxelles-Capitale", result.Levels[3].Nome);
        Assert.Equal("Bruxelles", result.Levels[^1].Nome);
    }

    [Fact]
    public void DeriveLevels_NetherlandsTypes_CityInsideHomonymousMunicipality()
    {
        var place = new HistoricalPlaceRecord
        {
            WikidataId = "Q727", CurrentName = "Amsterdam", CurrentCountry = "Paesi Bassi", TypeIds = ["Q515", "Q1852859"],
            AdministrativeDivisions =
            [
                P131("Q727", "Q9899", "Amsterdam", "Q2039348"),
                P131("Q9899", "Q701", "Olanda Settentrionale", "Q134390"),
                P131("Q701", "Q55", "Paesi Bassi"),
                P17("Q727", "Q55", "Paesi Bassi"),
            ],
        };

        var result = GeographyResolver.DeriveLevels(context, place);

        Assert.True(result.Confident);
        Assert.Null(result.SpecificName);
        Assert.Equal(new[] { "Paesi Bassi", "Olanda Settentrionale", "Amsterdam" }, result.Levels.Select(l => l.Nome));
    }

    [Fact]
    public void DeriveLevels_UnitedKingdomTypes_SkipsMetropolitanBoroughAndRegion()
    {
        var place = new HistoricalPlaceRecord
        {
            WikidataId = "Q18125", CurrentName = "Manchester", CurrentCountry = "Regno Unito", TypeIds = ["Q1115575", "Q7897276"],
            AdministrativeDivisions =
            [
                P131("Q18125", "Q21525592", "City of Manchester", "Q1002812", "Q21503295"),
                P131("Q21525592", "Q23099", "Grande Manchester", "Q180673", "Q769628"),
                P131("Q23099", "Q47967", "Inghilterra nord-occidentale", "Q48091"),
                P131("Q47967", "Q21", "Inghilterra", "Q3336843"),
                P131("Q21", "Q145", "Regno Unito"),
                P17("Q18125", "Q145", "Regno Unito"),
            ],
        };

        var result = GeographyResolver.DeriveLevels(context, place);

        Assert.True(result.Confident);
        Assert.Equal(new[] { "Regno Unito", "Inghilterra", "Grande Manchester", "City of Manchester", "Manchester" }, result.Levels.Select(l => l.Nome));
    }

    [Fact]
    public void DeriveLevels_UnitedKingdomTypes_WalesPrincipalArea()
    {
        var place = new HistoricalPlaceRecord
        {
            WikidataId = "Q10690", CurrentName = "Cardiff", CurrentCountry = "Regno Unito", TypeIds = ["Q515"],
            AdministrativeDivisions =
            [
                P131("Q10690", "Q24342199", "Cardiff", "Q15979307", "Q21503295"),
                P131("Q24342199", "Q25", "Galles", "Q3336843"),
                P17("Q10690", "Q145", "Regno Unito"),
            ],
        };

        var result = GeographyResolver.DeriveLevels(context, place);

        Assert.True(result.Confident);
        Assert.Equal(new[] { "Regno Unito", "Galles", "Cardiff", "", "Cardiff" }, result.Levels.Select(l => l.Nome));
        Assert.True(result.Levels[3].Assente);
    }

    [Fact]
    public void DeriveLevels_IrelandTypes_CityCountyAndProvince()
    {
        var place = new HistoricalPlaceRecord
        {
            WikidataId = "Q1761", CurrentName = "Dublino", CurrentCountry = "Irlanda", TypeIds = ["Q515"],
            AdministrativeDivisions =
            [
                P131("Q1761", "Q4169280", "Dublin City", "Q179872", "Q13455645"),
                P131("Q4169280", "Q159736", "Leinster", "Q202156"),
                P131("Q159736", "Q27", "Irlanda"),
                P17("Q1761", "Q27", "Irlanda"),
            ],
        };

        var result = GeographyResolver.DeriveLevels(context, place);

        Assert.True(result.Confident);
        Assert.Equal(new[] { "Irlanda", "Leinster", "Dublin City", "Dublino" }, result.Levels.Select(l => l.Nome));
    }

    [Fact]
    public void DeriveLevels_EstoniaTypes_TownInCityMunicipality()
    {
        var place = new HistoricalPlaceRecord
        {
            WikidataId = "Q13972", CurrentName = "Tartu", CurrentCountry = "Estonia", TypeIds = ["Q11881845"],
            AdministrativeDivisions =
            [
                P131("Q13972", "Q42307965", "Tartu City", "Q21583365"),
                P131("Q42307965", "Q192370", "Contea di Tartu", "Q189672"),
                P131("Q192370", "Q191", "Estonia"),
                P17("Q13972", "Q191", "Estonia"),
            ],
        };

        var result = GeographyResolver.DeriveLevels(context, place);

        Assert.True(result.Confident);
        Assert.Equal(new[] { "Estonia", "Contea di Tartu", "Tartu City", "Tartu" }, result.Levels.Select(l => l.Nome));
    }

    [Fact]
    public void DeriveLevels_LatviaTypes_StateCityCoversMunicipality()
    {
        var place = new HistoricalPlaceRecord
        {
            WikidataId = "Q1773", CurrentName = "Riga", CurrentCountry = "Lettonia", TypeIds = ["Q109329953", "Q1549591"],
            AdministrativeDivisions =
            [
                P131("Q1773", "Q211", "Lettonia"),
                P17("Q1773", "Q211", "Lettonia"),
            ],
        };

        var result = GeographyResolver.DeriveLevels(context, place);

        Assert.True(result.Confident);
        Assert.Equal(new[] { "Lettonia", "Riga", "Riga" }, result.Levels.Select(l => l.Nome));
    }

    [Fact]
    public void DeriveLevels_LatviaTypes_TownInMunicipality_SkipsFormerMunicipality()
    {
        var place = new HistoricalPlaceRecord
        {
            WikidataId = "Q107582", CurrentName = "Cēsis", CurrentCountry = "Lettonia", TypeIds = ["Q15584664", "Q515"],
            AdministrativeDivisions =
            [
                P131("Q107582", "Q1989164", "Cēsis Municipality (2009)", "Q110211119"),
                P131("Q107582", "Q97230394", "Comune di Cēsis", "Q3345345"),
                P131("Q97230394", "Q211", "Lettonia"),
                P131("Q1989164", "Q211", "Lettonia"),
                P17("Q107582", "Q211", "Lettonia"),
            ],
        };

        var result = GeographyResolver.DeriveLevels(context, place);

        Assert.True(result.Confident);
        Assert.Equal(new[] { "Lettonia", "Comune di Cēsis", "Cēsis" }, result.Levels.Select(l => l.Nome));
    }

    [Fact]
    public void DeriveLevels_LithuaniaTypes_TownInDistrictMunicipality()
    {
        var place = new HistoricalPlaceRecord
        {
            WikidataId = "Q191015", CurrentName = "Trakai", CurrentCountry = "Lituania", TypeIds = ["Q515"],
            AdministrativeDivisions =
            [
                P131("Q191015", "Q1987743", "Comune distrettuale di Trakai", "Q17301072"),
                P131("Q1987743", "Q188061", "Contea di Vilnius", "Q273789"),
                P131("Q188061", "Q37", "Lituania"),
                P17("Q191015", "Q37", "Lituania"),
            ],
        };

        var result = GeographyResolver.DeriveLevels(context, place);

        Assert.True(result.Confident);
        Assert.Equal(new[] { "Lituania", "Contea di Vilnius", "Comune distrettuale di Trakai", "Trakai" }, result.Levels.Select(l => l.Nome));
    }

    [Fact]
    public void DeriveLevels_LithuaniaTypes_CityMunicipalityWithoutCounty_IsNotConfident()
    {
        var place = new HistoricalPlaceRecord
        {
            WikidataId = "Q216", CurrentName = "Vilnius", CurrentCountry = "Lituania", TypeIds = ["Q515"],
            AdministrativeDivisions =
            [
                P131("Q216", "Q923117", "Comune urbano di Vilnius", "Q17166803"),
                P131("Q923117", "Q37", "Lituania"),
                P17("Q216", "Q37", "Lituania"),
            ],
        };

        var result = GeographyResolver.DeriveLevels(context, place);

        Assert.False(result.Confident);
        Assert.Equal(new[] { "Lituania", "", "Comune urbano di Vilnius", "Vilnius" }, result.Levels.Select(l => l.Nome));
    }

    [Fact]
    public void DeriveLevels_DenmarkTypes_CityInMunicipalityAndRegion()
    {
        var place = new HistoricalPlaceRecord
        {
            WikidataId = "Q25319", CurrentName = "Aarhus", CurrentCountry = "Danimarca", TypeIds = ["Q1549591"],
            AdministrativeDivisions =
            [
                P131("Q25319", "Q240262", "Comune di Aarhus", "Q2177636"),
                P131("Q240262", "Q26586", "Jutland Centrale", "Q62326"),
                P131("Q26586", "Q35", "Danimarca"),
                P17("Q25319", "Q35", "Danimarca"),
            ],
        };

        var result = GeographyResolver.DeriveLevels(context, place);

        Assert.True(result.Confident);
        Assert.Equal(new[] { "Danimarca", "Jutland Centrale", "Comune di Aarhus", "Aarhus" }, result.Levels.Select(l => l.Nome));
    }

    [Fact]
    public void DeriveLevels_NorwayTypes_OsloIsCountyAndMunicipality()
    {
        var place = new HistoricalPlaceRecord
        {
            WikidataId = "Q585", CurrentName = "Oslo", CurrentCountry = "Norvegia", TypeIds = ["Q1549591", "Q1306755"],
            AdministrativeDivisions =
            [
                P131("Q585", "Q5245991", "Comune di Oslo", "Q755707", "Q192299"),
                P131("Q5245991", "Q20", "Norvegia"),
                P17("Q585", "Q20", "Norvegia"),
            ],
        };

        var result = GeographyResolver.DeriveLevels(context, place);

        Assert.True(result.Confident);
        Assert.Equal(new[] { "Norvegia", "Østlandet", "Comune di Oslo", "Comune di Oslo", "Oslo" }, result.Levels.Select(l => l.Nome));
    }

    [Fact]
    public void DeriveLevels_NorwayTypes_RegionImpliedFromCounty()
    {
        var place = new HistoricalPlaceRecord
        {
            WikidataId = "Q25804", CurrentName = "Trondheim", CurrentCountry = "Norvegia", TypeIds = ["Q15092344", "Q515"],
            AdministrativeDivisions =
            [
                P131("Q25804", "Q1324436", "Comune di Trondheim", "Q755707"),
                P131("Q1324436", "Q127676", "Trøndelag", "Q192299"),
                P131("Q127676", "Q20", "Norvegia"),
                P17("Q25804", "Q20", "Norvegia"),
            ],
        };

        var result = GeographyResolver.DeriveLevels(context, place);

        Assert.True(result.Confident);
        Assert.Equal(new[] { "Norvegia", "Trøndelag", "Trøndelag", "Comune di Trondheim", "Trondheim" }, result.Levels.Select(l => l.Nome));
    }

    [Fact]
    public void DeriveLevels_SwedenTypes_UrbanAreaInMunicipalityAndCounty()
    {
        var place = new HistoricalPlaceRecord
        {
            WikidataId = "Q25286", CurrentName = "Uppsala", CurrentCountry = "Svezia", TypeIds = ["Q12813115", "Q515"],
            AdministrativeDivisions =
            [
                P131("Q25286", "Q59091", "Comune di Uppsala", "Q127448"),
                P131("Q59091", "Q104926", "Contea di Uppsala", "Q200547"),
                P131("Q104926", "Q34", "Svezia"),
                P17("Q25286", "Q34", "Svezia"),
            ],
        };

        var result = GeographyResolver.DeriveLevels(context, place);

        Assert.True(result.Confident);
        Assert.Equal(new[] { "Svezia", "Svealand", "Contea di Uppsala", "Comune di Uppsala", "Uppsala" }, result.Levels.Select(l => l.Nome));
    }

    [Fact]
    public void DeriveLevels_FinlandTypes_MunicipalityIsCity()
    {
        var place = new HistoricalPlaceRecord
        {
            WikidataId = "Q38511", CurrentName = "Turku", CurrentCountry = "Finlandia", TypeIds = ["Q856076", "Q515"],
            AdministrativeDivisions =
            [
                P131("Q38511", "Q5712", "Finlandia Propria", "Q193512"),
                P131("Q5712", "Q33", "Finlandia"),
                P17("Q38511", "Q33", "Finlandia"),
            ],
        };

        var result = GeographyResolver.DeriveLevels(context, place);

        Assert.True(result.Confident);
        Assert.Equal(new[] { "Finlandia", "Finlandia Propria", "Turku" }, result.Levels.Select(l => l.Nome));
    }

    public static TheoryData<string, string[]> OtherCountriesChains => new()
    {
        { "Vienna", ["Austria", "Vienna", "Vienna", "Vienna"] },
        { "Graz", ["Austria", "Stiria", "Graz", "Graz"] },
        { "Hallstatt", ["Austria", "Alta Austria", "Distretto di Gmunden", "Hallstatt"] },
        { "Lugano", ["Svizzera", "Canton Ticino", "Distretto di Lugano", "Lugano"] },
        { "Cracovia", ["Polonia", "Voivodato della Piccola Polonia", "Cracovia", "Cracovia", "Cracovia"] },
        { "Wieliczka", ["Polonia", "Voivodato della Piccola Polonia", "Distretto di Wieliczka", "Gmina Wieliczka", "Wieliczka"] },
        { "Brno", ["Repubblica Ceca", "Moravia Meridionale", "Distretto di Brno-città", "Brno"] },
        { "Pécs", ["Ungheria", "Provincia di Baranya", "Distretto di Pécs", "Pécs"] },
        { "Dubrovnik", ["Croazia", "Contea di Dubrovnik-Narenta", "Dubrovnik"] },
        { "Sarajevo", ["Bosnia ed Erzegovina", "Federazione di Bosnia ed Erzegovina", "Cantone di Sarajevo", "Sarajevo"] },
        { "Tirana", ["Albania", "Prefettura di Tirana", "Municipalità di Tirana", "Tirana"] },
        { "Nauplia", ["Grecia", "Peloponneso", "Argolide", "Comune di Nauplia", "Nauplia"] },
        { "Sibiu", ["Romania", "Distretto di Sibiu", "Sibiu"] },
        { "Bursa", ["Turchia", "Provincia di Bursa", "Bursa"] },
        { "Leopoli", ["Ucraina", "Oblast di Leopoli", "Distretto di Leopoli", "Comunità di Leopoli", "Leopoli"] },
        { "Kazan", ["Russia", "Tatarstan", "Kazan"] },
        { "Reykjavík", ["Islanda", "Regione della Capitale", "Reykjavíkurborg", "Reykjavík"] },
    };

    private static readonly Dictionary<string, (string Country, string CountryId, (string Id, string Name, string[] Types)[] Chain)> OtherChains = new()
    {
        ["Vienna"] = ("Austria", "Q40", [("Q1741", "Vienna", ["Q261543", "Q871419", "Q667509"])]),
        ["Graz"] = ("Austria", "Q40", [("Q13298", "Graz", ["Q871419", "Q262882", "Q667509"]), ("Q41358", "Stiria", ["Q261543"])]),
        ["Hallstatt"] = ("Austria", "Q40", [("Q131633", "Hallstatt", ["Q667509", "Q562061"]), ("Q253221", "Distretto di Gmunden", ["Q871419"]), ("Q41967", "Alta Austria", ["Q261543"])]),
        ["Lugano"] = ("Svizzera", "Q39", [("Q7024", "Lugano", ["Q54935504", "Q70208"]), ("Q660612", "Distretto di Lugano", ["Q662914", "Q3032137"]), ("Q12724", "Canton Ticino", ["Q23058"])]),
        ["Cracovia"] = ("Polonia", "Q36", [("Q31487", "Cracovia", ["Q925381", "Q1549591"]), ("Q54159", "Voivodato della Piccola Polonia", ["Q150093"])]),
        ["Wieliczka"] = ("Polonia", "Q36", [("Q472621", "Wieliczka", ["Q515"]), ("Q2185724", "Gmina Wieliczka", ["Q3491915"]), ("Q609424", "Distretto di Wieliczka", ["Q247073"]), ("Q54159", "Voivodato della Piccola Polonia", ["Q150093"])]),
        ["Brno"] = ("Repubblica Ceca", "Q213", [("Q14960", "Brno", ["Q5153359"]), ("Q850531", "Distretto di Brno-città", ["Q548611"]), ("Q192697", "Moravia Meridionale", ["Q38911"])]),
        ["Pécs"] = ("Ungheria", "Q28", [("Q45779", "Pécs", ["Q681277", "Q13218690"]), ("Q594019", "Distretto di Pécs", ["Q1162835"]), ("Q186195", "Provincia di Baranya", ["Q188604"])]),
        ["Dubrovnik"] = ("Croazia", "Q224", [("Q1722", "Dubrovnik", ["Q15105893"]), ("Q58289", "Contea di Dubrovnik-Narenta", ["Q57053"])]),
        ["Sarajevo"] = ("Bosnia ed Erzegovina", "Q225", [("Q11194", "Sarajevo", ["Q515", "Q102104752"]), ("Q18276", "Cantone di Sarajevo", ["Q18279"]), ("Q11198", "Federazione di Bosnia ed Erzegovina", ["Q140937749"])]),
        ["Tirana"] = ("Albania", "Q222", [("Q19689", "Tirana", ["Q515"]), ("Q13037436", "Municipalità di Tirana", ["Q768307"]), ("Q229892", "Prefettura di Tirana", ["Q104251"])]),
        ["Nauplia"] = ("Grecia", "Q41", [("Q202337", "Nauplia", ["Q515"]), ("Q2419494", "Comune di Nauplia", ["Q1349648"]), ("Q191897", "Argolide", ["Q1234255"]), ("Q202484", "Peloponneso", ["Q207299"])]),
        ["Sibiu"] = ("Romania", "Q218", [("Q83324", "Sibiu", ["Q640364"]), ("Q184797", "Distretto di Sibiu", ["Q1776764"])]),
        ["Bursa"] = ("Turchia", "Q43", [("Q40738", "Bursa", ["Q2716259", "Q515"]), ("Q43690", "Provincia di Bursa", ["Q48336"])]),
        ["Leopoli"] = ("Ucraina", "Q212", [("Q36036", "Leopoli", ["Q12131624"]), ("Q105061345", "Comunità di Leopoli", ["Q104841013"]), ("Q97454599", "Distretto di Leopoli", ["Q1267632"]), ("Q164193", "Oblast di Leopoli", ["Q3348196"])]),
        ["Kazan"] = ("Russia", "Q159", [("Q900", "Kazan", ["Q7930989"]), ("Q5481", "Tatarstan", ["Q41162", "Q43263"])]),
        ["Reykjavík"] = ("Islanda", "Q189", [("Q1764", "Reykjavík", ["Q515"]), ("Q1675663", "Reykjavíkurborg", ["Q955655"]), ("Q203304", "Regione della Capitale", ["Q842100"])]),
    };

    [Theory]
    [MemberData(nameof(OtherCountriesChains))]
    public void DeriveLevels_OtherCountriesTypes(string key, string[] expected)
    {
        var (country, countryId, chain) = OtherChains[key];
        var leaf = chain[0];
        var divisions = new List<PlaceDivision>();
        for (var i = 0; i < chain.Length; i++)
        {
            var parent = i + 1 < chain.Length ? chain[i + 1] : (countryId, country, Array.Empty<string>());
            divisions.Add(P131(chain[i].Id, parent.Item1, parent.Item2, parent.Item3));
        }
        divisions.Add(P17(leaf.Id, countryId, country));
        var place = new HistoricalPlaceRecord
        {
            WikidataId = leaf.Id, CurrentName = leaf.Name, CurrentCountry = country, TypeIds = [.. leaf.Types],
            AdministrativeDivisions = divisions,
        };

        var result = GeographyResolver.DeriveLevels(context, place);

        Assert.True(result.Confident);
        Assert.Equal(expected, result.Levels.Select(l => l.Nome));
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
