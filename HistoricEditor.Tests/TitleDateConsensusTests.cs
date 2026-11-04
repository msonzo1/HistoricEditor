using HistoricEditor;
using Xunit;

namespace HistoricEditor.Tests;

public class TitleDateConsensusTests
{
    private static TitleDateSource Source(string fonte, string inizio, string fine)
        => new() { Fonte = fonte, Inizio = inizio, Fine = fine };

    [Theory]
    [InlineData("25/12/800", 800, 12, 25)]
    [InlineData("12/800", 800, 12, null)]
    [InlineData("1519", 1519, null, null)]
    [InlineData("44 a.C.", -44, null, null)]
    [InlineData("50 d.C.", 50, null, null)]
    public void Parse_ReadsImportedFormats(string text, int year, int? month, int? day)
        => Assert.Equal(new TitleDate(year, month, day), TitleDateConsensus.Parse(text));

    [Theory]
    [InlineData("IX secolo")]
    [InlineData("")]
    [InlineData("31/13/800")]
    public void Parse_RejectsUnknownText(string text)
        => Assert.Null(TitleDateConsensus.Parse(text));

    [Fact]
    public void Evaluate_IsHiddenWithASingleSource()
        => Assert.Equal(ConsensusLevel.Hidden, TitleDateConsensus.Evaluate([Source("P39", "800", "814"), Source("P97", "", "")]).Level);

    [Fact]
    public void Evaluate_IsGreenWhenDatesAgreeAtCommonPrecision()
    {
        var result = TitleDateConsensus.Evaluate([Source("P39", "25/12/800", "28/01/814"), Source("P97", "800", "814")]);

        Assert.Equal(ConsensusLevel.Green, result.Level);
        Assert.Contains("✓ P97: 800 → 814", result.Details);
    }

    [Fact]
    public void Evaluate_IsOrangeWhenOnlyDayDiffers()
    {
        var result = TitleDateConsensus.Evaluate([Source("A", "02/02/1516", ""), Source("B", "23/01/1516", ""), Source("C", "02/02/1516", "")]);

        Assert.Equal(ConsensusLevel.Orange, result.Level);
        Assert.Equal(1, result.DifferingCount);
        Assert.Contains("✗ B: 23/01/1516 → — (diverso: inizio)", result.Details);
    }

    [Fact]
    public void Evaluate_IsRedWhenYearsDiffer()
    {
        var result = TitleDateConsensus.Evaluate([Source("A", "1516", "1556"), Source("B", "1519", "1556")]);

        Assert.Equal(ConsensusLevel.Red, result.Level);
    }
}

public class TitleRankingTests
{
    [Fact]
    public void ChooseMain_PrefersHighestRankThenPreferredStatement()
    {
        List<PersonTitle> titles =
        [
            new() { Titolo = "Duca di Baviera", WikidataId = "Q1" },
            new() { Titolo = "Re dei Franchi", WikidataId = "Q2" },
            new() { Titolo = "Imperatore del Sacro Romano Impero", WikidataId = "Q3" },
        ];

        Assert.Equal("Q3", TitleRanking.ChooseMain(titles, new HashSet<string> { "Q1" })!.WikidataId);
        Assert.Equal("Q2", TitleRanking.ChooseMain(titles[..2], new HashSet<string>())!.WikidataId);
        Assert.Equal("Q1", TitleRanking.ChooseMain([new() { Titolo = "Senatore", WikidataId = "Q4" }, titles[0]], new HashSet<string>())!.WikidataId);
    }

    [Theory]
    // Carlo V imperatore: Wikidata dà solo l'anno, le infobox il giorno (lo spagnolo 1520 è di un altro anno e non conta).
    [InlineData("1519", new[] { "1519", "28/06/1519", "28/06/1519", "23/10/1520", "1519" }, "28/06/1519")]
    [InlineData("1519", new[] { "06/1519", "1519" }, "06/1519")]
    // Due giorni diversi nello stesso anno: la data resta com'è e il LED segnala la differenza.
    [InlineData("1519", new[] { "28/06/1519", "23/10/1519" }, "1519")]
    // Già più precisa delle fonti, o fonti di un altro anno.
    [InlineData("26/01/1556", new[] { "27/08/1556", "1556" }, "26/01/1556")]
    [InlineData("1519", new[] { "1520" }, "1519")]
    // Data mancante: si riempie solo se le fonti concordano.
    [InlineData("", new[] { "28/06/1519", "1519" }, "28/06/1519")]
    [InlineData("", new[] { "1519", "1520" }, "")]
    [InlineData("anni 1510", new[] { "1519" }, "anni 1510")]
    public void Refine_CompletesWithAgreeingSources(string current, string[] candidates, string expected)
        => Assert.Equal(expected, TitleDateConsensus.Refine(current, candidates, InfoboxReignParser.Format));
}
