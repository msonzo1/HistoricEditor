using System.Text.Json;
using System.Text.RegularExpressions;
using HistoricEditor;
using Xunit;

namespace HistoricEditor.Tests;

public class ArtworkImportTests
{
    // Segnaposto: @I:Q5 (elemento), @T:+1503-00-00T00:00:00Z/9 (data e precisione), @A:+79.4/Q174728 (misura e unità).
    private static JsonElement Json(string text)
    {
        text = Regex.Replace(text, @"@I:(Q\d+)", m => "{\"snaktype\":\"value\",\"datavalue\":{\"value\":{\"id\":\"" + m.Groups[1].Value + "\"}}}");
        text = Regex.Replace(text, @"@T:([^/]+)/(\d+)", m => "{\"snaktype\":\"value\",\"datavalue\":{\"value\":{\"time\":\"" + m.Groups[1].Value + "\",\"precision\":" + m.Groups[2].Value + "}}}");
        text = Regex.Replace(text, @"@A:([^/]+)/(Q\d+)", m => "{\"rank\":\"normal\",\"mainsnak\":{\"datavalue\":{\"value\":{\"amount\":\"" + m.Groups[1].Value + "\",\"unit\":\"http://www.wikidata.org/entity/" + m.Groups[2].Value + "\"}}}}");
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    [Fact]
    public void Wikidata_People_ReadsCreatorAttributionsWorkshopPatronAndSubjects()
    {
        var claims = Json("""
        {
          "P170": [
            {"rank":"normal","mainsnak":@I:Q762},
            {"rank":"normal","mainsnak":@I:Q4233718,"qualifiers":{"P1774":[@I:Q5669]}},
            {"rank":"normal","mainsnak":@I:Q1001,"qualifiers":{"P5102":[@I:Q230768]}},
            {"rank":"deprecated","mainsnak":@I:Q999}
          ],
          "P88": [{"rank":"normal","mainsnak":@I:Q1395822}],
          "P180": [{"rank":"normal","mainsnak":@I:Q153032}]
        }
        """);

        var people = WikipediaImportService.ReadArtworkPeopleClaims(claims);

        Assert.Equal(
            [("Q762", OperaRuoli.Autore), ("Q5669", OperaRuoli.Bottega), ("Q1001", OperaRuoli.Attribuita), ("Q1395822", OperaRuoli.Committente), ("Q153032", OperaRuoli.Soggetto)],
            people);
    }

    [Fact]
    public void Wikidata_Date_UsesEarliestLatestOrCirca()
    {
        var range = Json("""{"P571":[{"rank":"normal","mainsnak":@T:+1503-00-00T00:00:00Z/8,"qualifiers":{"P1319":[@T:+1503-00-00T00:00:00Z/9],"P1326":[@T:+1506-00-00T00:00:00Z/9]}}]}""");
        var circa = Json("""{"P571":[{"rank":"normal","mainsnak":@T:+1490-00-00T00:00:00Z/9,"qualifiers":{"P1480":[@I:Q5727902]}}]}""");

        Assert.Equal("1503-1506", WikipediaImportService.ReadArtworkDate(range));
        Assert.Equal("circa 1490", WikipediaImportService.ReadArtworkDate(circa));
        Assert.Equal(string.Empty, WikipediaImportService.ReadArtworkDate(Json("{}")));
    }

    [Fact]
    public void Wikidata_Materials_KeepBestRankAndSeparateSupport()
    {
        var claims = Json("""
        {"P186":[
          {"rank":"preferred","mainsnak":@I:Q296955},
          {"rank":"preferred","mainsnak":@I:Q106857865,"qualifiers":{"P518":[@I:Q861259]}},
          {"rank":"normal","mainsnak":@I:Q287,"qualifiers":{"P518":[@I:Q1737943]}}
        ]}
        """);

        Assert.Equal([("Q296955", false), ("Q106857865", true)], WikipediaImportService.ReadMaterialClaims(claims));
        Assert.Equal("olio su tavola di pioppo", WikipediaImportService.FormatTechnique(["colore a olio"], ["tavola di pioppo"]));
        Assert.Equal("marmo", WikipediaImportService.FormatTechnique(["marmo", "Marmo"], []));
    }

    [Fact]
    public void Wikidata_Dimensions_FormatWithUnits()
    {
        var painting = Json("""{"P2048":[@A:+79.4/Q174728],"P2049":[@A:+53.4/Q174728]}""");
        var mixed = Json("""{"P2048":[@A:+5.17/Q11573],"P2049":[@A:+199/Q174728],"P2386":[@A:+30/Q174728]}""");

        Assert.Equal("79,4 × 53,4 cm", WikipediaImportService.ReadDimensions(painting));
        Assert.Equal("5,17 m × 199 cm, Ø 30 cm", WikipediaImportService.ReadDimensions(mixed));
    }

    [Fact]
    public void Wikidata_Type_MapsItalianLabels()
    {
        Assert.Equal("Pala d'altare", WikipediaImportService.MapArtworkType(["dipinto", "pala d’altare"]));
        Assert.Equal("Scultura", WikipediaImportService.MapArtworkType(["statua equestre"]));
        Assert.Equal("Affresco", WikipediaImportService.MapArtworkType(["ciclo di affreschi", "affresco"]));
        Assert.Equal("Oggetto liturgico", WikipediaImportService.MapArtworkType(["opera d'arte", "oggetto liturgico"]));
    }

    [Fact]
    public void Wikidata_MatchingHuman_ChecksYearsWithTolerance()
    {
        var monet = Json("""{"P31":[{"rank":"normal","mainsnak":@I:Q5}],"P569":[{"rank":"normal","mainsnak":@T:+1840-11-14T00:00:00Z/11}],"P570":[{"rank":"normal","mainsnak":@T:+1926-12-05T00:00:00Z/11}]}""");
        var painting = Json("""{"P31":[{"rank":"normal","mainsnak":@I:Q3305213}]}""");

        Assert.True(WikipediaImportService.IsMatchingHuman(monet, 1840, 1926));
        Assert.True(WikipediaImportService.IsMatchingHuman(monet, 1841, null));
        Assert.True(WikipediaImportService.IsMatchingHuman(monet, null, null));
        Assert.False(WikipediaImportService.IsMatchingHuman(monet, 1870, 1926));
        Assert.False(WikipediaImportService.IsMatchingHuman(painting, null, null));
    }

    [Theory]
    [InlineData("Oil on canvas", "olio su tela")]
    [InlineData("Tempera and gold leaf on wood panel", "tempera e foglia d’oro su tavola")]
    [InlineData("Pen and brown ink, black chalk", "penna e inchiostro bruno, matita nera")]
    [InlineData("Marble", "marmo")]
    [InlineData("Oil on canvas, mounted on Masonite", "Oil on canvas, mounted on Masonite")]
    public void Museum_Medium_TranslatedOnlyWhenEveryWordIsKnown(string medium, string expected)
        => Assert.Equal(expected, ArtworkText.TranslateMedium(medium));

    [Theory]
    [InlineData("ca. 1665–67", "circa 1665–67")]
    [InlineData("c. 1630", "circa 1630")]
    [InlineData("late 15th century", "fine XV secolo")]
    [InlineData("1st century BCE", "I secolo a.C.")]
    [InlineData("1887", "1887")]
    public void Museum_Date_Translated(string date, string expected)
        => Assert.Equal(expected, ArtworkText.TranslateDate(date));

    [Theory]
    [InlineData("17 x 24 in. (43.2 x 61 cm)", "43,2 × 61 cm")]
    [InlineData("Framed: 233.5 x 184 x 12 cm (91 15/16 x 72 7/16 x 4 3/4 in.); Unframed: 202.5 x 152.7 cm (79 3/4 x 60 1/8 in.)", "202,5 × 152,7 cm")]
    [InlineData("89.9 × 94.1 cm (35 3/8 × 37 1/16 in.); Framed: 103.2 × 107 × 5.8 cm", "89,9 × 94,1 cm")]
    [InlineData("H. 12 in.", "")]
    public void Museum_Dimensions_FirstMetricMeasureWithoutFrame(string text, string expected)
        => Assert.Equal(expected, ArtworkText.ExtractMetricDimensions(text));

    [Fact]
    public void Museum_ArtistDisplayAndRoles()
    {
        Assert.Equal(("Claude Monet", 1840, 1926), ArtworkText.ParseArtistDisplay("Claude Monet (French, 1840–1926)"));
        Assert.Equal(("Anonimo", null, null), ArtworkText.ParseArtistDisplay("Anonimo"));
        Assert.Equal(OperaRuoli.Autore, ArtworkText.MapArtistRole("Artist"));
        Assert.Equal(OperaRuoli.Attribuita, ArtworkText.MapArtistRole("Attributed to"));
        Assert.Equal(OperaRuoli.Bottega, ArtworkText.MapArtistRole("Workshop of"));
        Assert.Null(ArtworkText.MapArtistRole("Follower of"));
        Assert.Equal("Dipinto", ArtworkText.TranslateType("Paintings"));
        Assert.Equal("Sandro Botticelli", MuseumImportService.FlipName("Botticelli, Sandro"));
        Assert.Equal("Q5582", MuseumImportService.WikidataIdFromUrl("https://www.wikidata.org/wiki/Q5582"));
    }

    [Fact]
    public void Met_ParsesObjectWithArtistWikidataAndPublicDomainImage()
    {
        var o = Json("""
        {"objectID":436524,"title":"Sunflowers","objectDate":"1887","medium":"Oil on canvas","dimensions":"17 x 24 in. (43.2 x 61 cm)",
         "artistPrefix":"","artistDisplayName":"Vincent van Gogh","artistBeginDate":"1853","artistEndDate":"1890",
         "artistWikidata_URL":"https://www.wikidata.org/wiki/Q5582","objectWikidata_URL":"https://www.wikidata.org/wiki/Q9213165",
         "isPublicDomain":true,"primaryImage":"https://images.example/big.jpg","primaryImageSmall":"https://images.example/small.jpg",
         "objectURL":"https://www.metmuseum.org/art/collection/search/436524","objectName":"Painting","creditLine":"Rogers Fund, 1949",
         "constituents":[{"role":"Artist","name":"Vincent van Gogh","constituentWikidata_URL":"https://www.wikidata.org/wiki/Q5582"}]}
        """);

        var import = MuseumImportService.ParseMetObject(o);
        var result = MuseumImportService.ParseMetResult(o);

        Assert.Equal("Sunflowers", import.Titolo);
        Assert.Equal("Dipinto", import.Tipo);
        Assert.Equal("olio su tela", import.Tecnica);
        Assert.Equal("43,2 × 61 cm", import.Dimensioni);
        Assert.Equal("Q9213165", import.WikidataId);
        Assert.Equal("https://images.example/big.jpg", import.ImmagineUrl);
        var artist = Assert.Single(import.Persone);
        Assert.Equal(("Q5582", OperaRuoli.Autore, 1853), (artist.WikidataId, artist.Ruolo, artist.AnnoNascita));
        Assert.Equal("436524", result?.Id);
        Assert.Equal("Vincent van Gogh · 1887", result?.Dettaglio);

        var restricted = MuseumImportService.ParseMetObject(Json("""{"title":"X","isPublicDomain":false,"primaryImage":"https://images.example/big.jpg"}"""));
        Assert.Equal(string.Empty, restricted.ImmagineUrl);
    }

    [Fact]
    public void ArtInstitute_ParsesArtworkAndBuildsIiifImage()
    {
        var root = Json("""
        {"data":{"id":16568,"title":"Water Lilies","artist_title":"Claude Monet","artist_display":"Claude Monet (French, 1840–1926)",
          "date_display":"1906","medium_display":"Oil on canvas","dimensions":"89.9 × 94.1 cm (35 3/8 × 37 1/16 in.)",
          "artwork_type_title":"Painting","image_id":"abc","is_public_domain":true,"credit_line":"Ryerson Collection"},
         "config":{"iiif_url":"https://www.artic.edu/iiif/2"}}
        """);

        var import = MuseumImportService.ParseAicArtwork(root);

        Assert.Equal("https://www.artic.edu/iiif/2/abc/full/843,/0/default.jpg", import.ImmagineUrl);
        Assert.Equal("https://www.artic.edu/artworks/16568", import.Fonti[0]);
        var artist = Assert.Single(import.Persone);
        Assert.Equal(("Claude Monet", 1840, 1926), (artist.Nome, artist.AnnoNascita, artist.AnnoMorte));
        Assert.Equal(string.Empty, artist.WikidataId);
    }

    [Fact]
    public void Cleveland_ParsesCreatorsAndCc0Image()
    {
        var d = Json("""
        {"id":148758,"title":"The Crucifixion of Saint Andrew","creation_date":"1606–7","technique":"oil on canvas","type":"Painting",
         "measurements":"Framed: 233.5 x 184 x 12 cm; Unframed: 202.5 x 152.7 cm","share_license_status":"CC0","url":"https://clevelandart.org/art/1976.2",
         "creators":[{"description":"Caravaggio (Italian, 1571–1610)","role":"artist","birth_year":"1571","death_year":"1610"},
                     {"description":"Someone (Italian, 1600–1650)","role":"follower of"}],
         "images":{"web":{"url":"https://cdn.example/web.jpg"},"print":{"url":"https://cdn.example/print.jpg"}}}
        """);

        var import = MuseumImportService.ParseClevelandArtwork(d);

        Assert.Equal("https://cdn.example/print.jpg", import.ImmagineUrl);
        Assert.Equal("202,5 × 152,7 cm", import.Dimensioni);
        var artist = Assert.Single(import.Persone);
        Assert.Equal(("Caravaggio", 1571, 1610), (artist.Nome, artist.AnnoNascita, artist.AnnoMorte));
    }

    [Fact]
    public void Europeana_ParsesLanguageAwareFieldsAndAgentLabels()
    {
        var item = Json("""
        {"id":"/90402/SK_A_3381","title":["Judith met het hoofd van Holofernes"],
         "dcTitleLangAware":{"en":["Judith with the Head of Holofernes"],"nl":["Judith met het hoofd van Holofernes"]},
         "edmAgentLabel":[{"def":"Botticelli, Sandro"},{"def":"Botticelli, Sandro"}],
         "dcTypeLangAware":{"def":["http://data.europeana.eu/concept/47"],"en":["painting"]},
         "dataProvider":["Rijksmuseum"],"country":["Netherlands"],"rights":["http://creativecommons.org/publicdomain/mark/1.0/"],
         "edmIsShownBy":["https://img.example/full.jpg"],"edmIsShownAt":["http://hdl.handle.net/10934/RM0001"],
         "guid":"https://www.europeana.eu/item/90402/SK_A_3381?utm_source=api"}
        """);

        var import = MuseumImportService.ParseEuropeanaItem(item);

        Assert.Equal("Judith with the Head of Holofernes", import.Titolo);
        Assert.Equal("Dipinto", import.Tipo);
        Assert.Equal("Sandro Botticelli", Assert.Single(import.Persone).Nome);
        Assert.Equal("Rijksmuseum · Netherlands", import.Collocazione);
        Assert.Equal("https://img.example/full.jpg", import.ImmagineUrl);
        Assert.Contains("https://www.europeana.eu/item/90402/SK_A_3381", import.Fonti);
        Assert.Contains("Licenza dell’immagine: http://creativecommons.org/publicdomain/mark/1.0/", import.Fonti);
    }

    [Fact]
    public void MergeSources_AddsOnlyNewLines()
    {
        var merged = ArtworksWindow.MergeSources("Vasari, Le vite\r\nhttps://it.wikipedia.org/wiki/Gioconda", ["https://it.wikipedia.org/wiki/Gioconda", "https://www.wikidata.org/wiki/Q12418", " "]);

        Assert.Equal(["Vasari, Le vite", "https://it.wikipedia.org/wiki/Gioconda", "https://www.wikidata.org/wiki/Q12418"], merged.Split(Environment.NewLine));
    }
}
