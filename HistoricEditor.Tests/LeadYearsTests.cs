using Xunit;

namespace HistoricEditor.Tests;

public class LeadYearsTests
{
    [Theory]
    [InlineData("Napoleone Bonaparte (Ajaccio, 15 agosto 1769 – Longwood, 5 maggio 1821) è stato un militare.", 1769, 1821)]
    [InlineData("Napoleon Bonaparte (born Napoleone Buonaparte; 15 August 1769 – 5 May 1821) was a French general.", 1769, 1821)]
    [InlineData("Johann Sebastian Bach (* 21. März 1685 in Eisenach; † 28. Juli 1750 in Leipzig) war ein Komponist.", 1685, 1750)]
    [InlineData("Ludwig (1845-1886) war König.", 1845, 1886)]
    public void ReadsCommonEraYears(string extract, int birth, int death)
    {
        Assert.Equal((birth, death), WikipediaImportService.ExtractLeadYears(extract));
    }

    [Fact]
    public void SkipsPronunciationParenthesis()
    {
        var extract = "Carlo Magno (pronuncia /ˈkarlo ˈmaɲɲo/) (2 aprile 742 – Aquisgrana, 28 gennaio 814) fu re dei Franchi.";

        Assert.Equal((742, 814), WikipediaImportService.ExtractLeadYears(extract));
    }

    [Theory]
    [InlineData("Gaio Giulio Cesare (Roma, 13 luglio 100 a.C. – Roma, 15 marzo 44 a.C.) è stato un politico.", -100, -44)]
    [InlineData("Gaius Julius Caesar (12 July 100 BC – 15 March 44 BC) was a Roman general.", -100, -44)]
    [InlineData("Gaius Iulius Caesar (* 13. Juli 100 v. Chr. in Rom; † 15. März 44 v. Chr. ebenda) war ein Politiker.", -100, -44)]
    [InlineData("Jules César (100 av. J.-C. – 44 av. J.-C.) est un homme d'État.", -100, -44)]
    [InlineData("Platone (428 – 348 a.C.) fu un filosofo.", -428, -348)]
    public void ReadsBeforeChristYearsAsNegative(string extract, int birth, int death)
    {
        Assert.Equal((birth, death), WikipediaImportService.ExtractLeadYears(extract));
    }

    [Fact]
    public void KeepsBirthBeforeChristAndDeathAfterChrist()
    {
        var extract = "Augusto (Roma, 23 settembre 63 a.C. – Nola, 19 agosto 14 d.C.) è stato un imperatore.";

        Assert.Equal((-63, 14), WikipediaImportService.ExtractLeadYears(extract));
    }

    [Fact]
    public void ReturnsNothingWithoutYears()
    {
        Assert.Equal((null, null), WikipediaImportService.ExtractLeadYears("Testo (senza anni) qualunque."));
        Assert.Equal((null, null), WikipediaImportService.ExtractLeadYears(""));
    }

    [Theory]
    [InlineData("Leonardo da Vinci (Anchiano, 15 aprile 1452 – Amboise, 2 maggio 1519) è stato un inventore.", 1452, 1519, "Anchiano", "Amboise", "4/15", "5/2")]
    [InlineData("Leonardo da Vinci (* 15. April 1452 in Anchiano bei Vinci; † 2. Mai 1519 in Amboise) war ein Maler.", 1452, 1519, "Anchiano bei Vinci", "Amboise", "4/15", "5/2")]
    [InlineData("Leonardo da Vinci (Anchiano, 15 de abril de 1452-Amboise, 2 de mayo de 1519) fue un polímata.", 1452, 1519, "Anchiano", "Amboise", "4/15", "5/2")]
    [InlineData("Léonard de Vinci (en italien : Leonardo da Vinci), né le 14 avril 1452 à Vinci (Toscane) et mort le 2 mai 1519 à Amboise (Touraine), est un peintre.", 1452, 1519, "Vinci", "Amboise", "4/14", "5/2")]
    [InlineData("Gaius Iulius Caesar (* 13. Juli 100 v. Chr. in Rom; † 15. März 44 v. Chr. ebenda) war ein Politiker.", -100, -44, "Rom", "Rom", "7/13", "3/15")]
    [InlineData("Leonardo da Vinci (April 15, 1452 – 2 May 1519) was a polymath.", 1452, 1519, "", "", "4/15", "5/2")]
    [InlineData("Napoleon Bonaparte (Ajaccio, 15 augustus 1769 – Sint-Helena, mei 1821) was een Frans staatsman.", 1769, 1821, "Ajaccio", "Sint-Helena", "8/15", "5/")]
    [InlineData("Carlo Magno (742 – Aquisgrana, 28 gennaio 814) è stato re.", 742, 814, "", "Aquisgrana", "/", "1/28")]
    [InlineData("Henri Tudor, en gallois Harri Tudur, né le 28 janvier 1457 au château de Pembroke, principauté de Galles, et mort le 21 avril 1509 au palais de Richmond, Surrey, fut roi d'Angleterre. Une révolte (menée par le duc de Buckingham en octobre 1483) échoue.", 1457, 1509, "", "", "1/28", "4/21")]
    public void ReadsPlacesAndDaysFromLead(string extract, int birth, int death, string birthPlace, string deathPlace, string birthDay, string deathDay)
    {
        var lead = WikipediaImportService.ExtractLead(extract);

        Assert.Equal((birth, death, birthPlace, deathPlace), (lead.BirthYear, lead.DeathYear, lead.BirthPlace, lead.DeathPlace));
        Assert.Equal(birthDay, $"{lead.BirthMonth}/{lead.BirthDay}");
        Assert.Equal(deathDay, $"{lead.DeathMonth}/{lead.DeathDay}");
    }

    [Theory]
    [InlineData("+1452-04-15T00:00:00Z", 11, "15/04/1452")]
    [InlineData("+0742-04-02T00:00:00Z", 9, "742")]
    [InlineData("+0014-08-19T00:00:00Z", 11, "19/08/14 d.C.")]
    [InlineData("-0044-03-15T00:00:00Z", 11, "15/03/44 a.C.")]
    [InlineData("+1450-00-00T00:00:00Z", 8, "anni 1450")]
    [InlineData("+1500-00-00T00:00:00Z", 7, "XV secolo")]
    [InlineData("-0500-00-00T00:00:00Z", 7, "V secolo a.C.")]
    public void FormatDate_HandlesPrecisionAndEra(string raw, int precision, string expected)
    {
        Assert.Equal(expected, WikipediaImportService.FormatDate(raw, precision));
        Assert.NotNull(HistoricalYear.Parse(expected));
    }
}
