using HistoricEditor;
using Xunit;

namespace HistoricEditor.Tests;

public class InfoboxReignParserTests
{
    [Fact]
    public void ReadsItalianSovranoTemplate()
    {
        var wikitext = "{{Sovrano\n|nome = Carlo V d'Asburgo\n|titolo = [[Imperatori del Sacro Romano Impero|Imperatore dei Romani]]\n"
            + "|inizio regno = 28 giugno 1519\n|fine regno = 27 agosto 1556<ref>La cronologia è confusa: 1556 o 1558.</ref>\n"
            + "|titolo1 = [[Sovrani d'Italia#Regnum Italiae|Re d'Italia]]\n|incoronazione1 = [[Bologna]], 22 febbraio 1530\n"
            + "|titolo3 = [[Sovrani di Spagna|Re di Spagna e delle Indie]]<ref>Formalmente [[Re d'Aragona]].</ref>\n"
            + "|inizio regno3 = 14 marzo 1516<ref group=\"N\">Giovanna regnò fino al 12 aprile 1555.</ref>\n|fine regno3 = 16 gennaio 1556\n}}";

        var reigns = InfoboxReignParser.Parse(wikitext);

        Assert.Equal(2, reigns.Count);
        Assert.Equal(["Imperatori del Sacro Romano Impero"], reigns[0].Links);
        Assert.Equal(("28/06/1519", "27/08/1556"), (reigns[0].Start, reigns[0].End));
        Assert.Equal("Sovrani di Spagna", reigns[1].Links[0]);
        Assert.Equal(("14/03/1516", "16/01/1556"), (reigns[1].Start, reigns[1].End));
    }

    [Fact]
    public void ReadsEnglishRoyaltyTemplate()
    {
        var wikitext = "{{Infobox royalty\n| succession   = [[Holy Roman Emperor]]<br>as Charles V\n"
            + "| reign        = 28 June 1519 –<br/>3 August 1556/24 February 1558{{efn|The chronology has been disputed.<ref name=\"R\">{{Cite book |last=Robertson\n"
            + "|year=1769}}</ref>}}\n"
            + "| succession1  = [[King of Spain]]<br>as Charles I\n| reign1       = 14 March 1516 – {{nowrap|16 January 1556}}\n}}";

        var reigns = InfoboxReignParser.Parse(wikitext);

        Assert.Equal(2, reigns.Count);
        Assert.Equal(("28/06/1519", "03/08/1556"), (reigns[0].Start, reigns[0].End));
        Assert.Equal("King of Spain", reigns[1].Links[0]);
        Assert.Equal(("14/03/1516", "16/01/1556"), (reigns[1].Start, reigns[1].End));
    }

    [Fact]
    public void ReadsFrenchSpanishAndDutchTemplates()
    {
        var french = InfoboxReignParser.Parse(
            "| fonction1               = [[Liste des souverains du Saint-Empire|Empereur du Saint-Empire]]<br>''Charles {{V}}''\n"
            + "| à partir du fonction1   = 28 juin 1519\n| jusqu'au fonction1      = 24 février 1558<ref name=\"E\">Setton (1984)</ref>\n"
            + "| fonction5 = [[Roi des Romains]]\n| à partir du fonction5 = {{date|23|octobre|1520}}\n| jusqu'au fonction5 = {{1er}} janvier 1530");
        Assert.Equal([("28/06/1519", "24/02/1558"), ("23/10/1520", "01/01/1530")], french.Select(r => (r.Start, r.End)));

        var spanish = InfoboxReignParser.Parse(
            "| título0 = [[Archivo:Armoiries Espagne.svg|40px]]<br>[[Anexo:Monarcas de Castilla|Rey de Castilla y León]]\n"
            + "| reinado0 = {{fecha|14|03|1516}}-{{fecha|16|01|1556}}<br/>({{edad en años y días|14|03|1516|16|01|1556}})\n"
            + "| título1 = [[Emperador del Sacro Imperio Romano]]\n| reinado1 = 23 de octubre de 1520-24 de febrero de 1558<br>\n"
            + "| título5 = [[Anexo:Soberanos de Austria|Archiduque de Austria]]\n| reinado5 = 12 de enero de 1519-1521");
        Assert.Equal(["Anexo:Monarcas de Castilla"], spanish[0].Links);
        Assert.Equal([("14/03/1516", "16/01/1556"), ("23/10/1520", "24/02/1558"), ("12/01/1519", "1521")], spanish.Select(r => (r.Start, r.End)));

        var dutch = InfoboxReignParser.Parse(
            "| functie             = [[Heilige Roomse Rijk|Rooms-Duits]] [[Lijst van Rooms-Duitse koningen en keizers|koning en keizer]]\n"
            + "| regeerperiode       = [[1519]] – [[1556]]\n| inauguratie         = [[28 juni]] [[1519]] in [[Frankfurt am Main|Frankfurt]]");
        Assert.Equal(["Heilige Roomse Rijk", "Lijst van Rooms-Duitse koningen en keizers"], dutch[0].Links);
        Assert.Equal(("1519", "1556"), (dutch[0].Start, dutch[0].End));
    }

    [Theory]
    [InlineData("June 28, 1519 – 1556", "28/06/1519", "1556")]
    [InlineData("June 1519 – 1556", "06/1519", "1556")]
    [InlineData("27 BC – AD 14", "27 a.C.", "")]
    [InlineData("1519 –", "1519", "")]
    [InlineData("", "", "")]
    public void ParsesRanges(string text, string start, string end)
        => Assert.Equal((start, end), InfoboxReignParser.ParseRange(text));

    [Fact]
    public void SkipsTitlesWithoutDatesOrLinks()
    {
        Assert.Empty(InfoboxReignParser.Parse("| titolo = [[Re d'Italia]]\n| incoronazione = 1530\n| titolo2 = Arciduca\n| inizio regno2 = 1519"));
    }
}
