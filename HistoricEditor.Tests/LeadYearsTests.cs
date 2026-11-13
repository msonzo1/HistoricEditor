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
    [InlineData("Karl V. (spanisch Carlos I; * 24. Februar 1500 im Prinzenhof, Gent, Burgundische Niederlande; † 21. September 1558 in Cuacos de Yuste, Spanien) war ein Kaiser.", 1500, 1558, "Prinzenhof, Gent, Burgundische Niederlande", "Cuacos de Yuste, Spanien", "2/24", "9/21")]
    [InlineData("Leonardo da Vinci (April 15, 1452 – 2 May 1519) was a polymath.", 1452, 1519, "", "", "4/15", "5/2")]
    [InlineData("Napoleon Bonaparte (Ajaccio, 15 augustus 1769 – Sint-Helena, mei 1821) was een Frans staatsman.", 1769, 1821, "Ajaccio", "Sint-Helena", "8/15", "5/")]
    [InlineData("Carlo Magno (742 – Aquisgrana, 28 gennaio 814) è stato re.", 742, 814, "", "Aquisgrana", "/", "1/28")]
    [InlineData("Henri Tudor, en gallois Harri Tudur, né le 28 janvier 1457 au château de Pembroke, principauté de Galles, et mort le 21 avril 1509 au palais de Richmond, Surrey, fut roi d'Angleterre. Une révolte (menée par le duc de Buckingham en octobre 1483) échoue.", 1457, 1509, "", "", "1/28", "4/21")]
    // Anni a una o due cifre senza «d.C.»: valgono subito dopo il mese (Tito, 2026-11-10).
    [InlineData("Tito Cesare Vespasiano Augusto (in latino Titus Caesar Vespasianus Augustus; nelle epigrafi: IMP·T·CAESAR·VESPASIANUS·AVG·PON·M·TR·POT; Roma, 30 dicembre 39 – Aquae Cutiliae, 13 settembre 81) è stato un imperatore romano.", 39, 81, "Roma", "Aquae Cutiliae", "12/30", "9/13")]
    [InlineData("Titus (* 30. Dezember 39 in Rom; † 13. September 81 in Aquae Cutiliae, Latium) war römischer Kaiser.", 39, 81, "Rom", "Aquae Cutiliae, Latium", "12/30", "9/13")]
    [InlineData("Titus Flavius Vespasianus ( ; 30 December AD 39 – 13 September 81) was Roman emperor from AD 79 to 81.", 39, 81, "", "", "12/30", "9/13")]
    [InlineData("Tito Flavio Vespasiano (en latín: Titus Flavius Vespasianus;​ 30 de diciembre de 39-13 de septiembre de 81), comúnmente conocido como Tito.", 39, 81, "", "", "12/30", "9/13")]
    [InlineData("Titus (en latin : Imperator Titus Caesar Vespasianus Augustus), né le 30 décembre 39 et mort le 13 septembre 81, est un empereur romain.", 39, 81, "", "", "12/30", "9/13")]
    public void ReadsPlacesAndDaysFromLead(string extract, int birth, int death, string birthPlace, string deathPlace, string birthDay, string deathDay)
    {
        var lead = WikipediaImportService.ExtractLead(extract);

        Assert.Equal((birth, death, birthPlace, deathPlace), (lead.BirthYear, lead.DeathYear, lead.BirthPlace, lead.DeathPlace));
        Assert.Equal(birthDay, $"{lead.BirthMonth}/{lead.BirthDay}");
        Assert.Equal(deathDay, $"{lead.DeathMonth}/{lead.DeathDay}");
    }

    [Theory]
    [InlineData("+1452-04-15T00:00:00Z", 11, "15/04/1452")]
    [InlineData("+0742-04-02T00:00:00Z", 9, "0742")]
    [InlineData("+0014-08-19T00:00:00Z", 11, "19/08/0014")]
    [InlineData("-0044-03-15T00:00:00Z", 11, "15/03/44 a.C.")]
    [InlineData("+1450-00-00T00:00:00Z", 8, "anni 1450")]
    [InlineData("+1500-00-00T00:00:00Z", 7, "XV secolo")]
    [InlineData("-0500-00-00T00:00:00Z", 7, "V secolo a.C.")]
    public void FormatDate_HandlesPrecisionAndEra(string raw, int precision, string expected)
    {
        Assert.Equal(expected, WikipediaImportService.FormatDate(raw, precision));
        Assert.NotNull(HistoricalYear.Parse(expected));
    }

    [Fact]
    public void ReadsPlacesFromEnglishInfobox()
    {
        var wikitext = "{{Infobox royalty\n| birth_date   = {{birth date|1500|2|24|df=yes}}\n| birth_place  = [[Prinsenhof, Ghent]], Flanders<ref>Note</ref>\n"
            + "| death_date   = {{death date and age|1558|9|21|1500|2|24|df=yes}}\n| death_place  = [[Monastery of Yuste]], Castile\n| burial_place = [[El Escorial]], Spain\n}}";

        Assert.Equal(("Prinsenhof, Ghent, Flanders", "Monastery of Yuste, Castile"), WikipediaImportService.ExtractInfoboxPlaces(wikitext));
    }

    [Theory]
    [InlineData("|luogo di nascita = [[Gand]]\n|luogo di morte = [[Monastero di Yuste]], [[Cuacos de Yuste]]", "Gand", "Monastero di Yuste, Cuacos de Yuste")]
    [InlineData("| lieu de naissance = [[Gand]] ([[comté de Flandre|Flandre]])\n| lieu de décès = [[Monastère de Yuste]] ({{Espagne}})", "Gand (Flandre)", "Monastère de Yuste")]
    [InlineData("| lugar de nacimiento = [[Gante]]<br />[[Condado de Flandes]]\n| lugar de defunción = <!-- x -->[[Cuacos de Yuste]]", "Gante, Condado de Flandes", "Cuacos de Yuste")]
    [InlineData("| birth_place = \n| death_place = {{flagicon|ESP}}", "", "")]
    public void ReadsPlacesFromOtherInfoboxes(string wikitext, string birth, string death)
    {
        Assert.Equal((birth, death), WikipediaImportService.ExtractInfoboxPlaces(wikitext));
    }

    // Introduzioni reali di «Gaio Giulio Cesare» nelle sei edizioni (accorciate dopo la prima frase).
    [Theory]
    [InlineData("Gaio Giulio Cesare (in latino Gaius Iulius Caesar,  pronuncia restaurata o classica; AFI: [ˈɡäː.i.ʊs ˈjuː.li.ʊs ˈkɐ̯ɛ̯.sär]; nelle epigrafi C·IVLIVS·C·F·CAESAR e DIVVS IVLIVS; in greco antico Γάϊος Ἰούλιος Καῖσαρ?, Gáïos Iúlios Kaîsar; Roma, 13 luglio 100 a.C. – Roma, 15 marzo 44 a.C.) è stato un politico.", "Roma", "Roma")]
    [InlineData("Gaius Julius Caesar (12 or 13 July 100 BC – 15 March 44 BC) was a Roman general.", "", "")]
    [InlineData("Gaius Iulius Caesar (deutsch auch Gaius Julius Cäsar; * 13. Juli 100 v. Chr. in Rom; † 15. März 44 v. Chr. ebenda) war ein römischer Staatsmann.", "Rom", "Rom")]
    [InlineData("Jules César (en latin : Caius Iulius Caesar à sa naissance, Imperator Iulius Caesar Divus après sa mort), aussi simplement appelé César, est un conquérant, homme d'État et écrivain romain, né le 12 ou le 13 juillet 100 av. J.-C. à Rome et mort assassiné le 15 mars 44 av. J.-C. (aux ides de mars) dans la même ville.\nSon parcours unique.", "Rome", "Rome")]
    [InlineData("Cayo o Gayo Julio César (en latín: Gaius Iulius Caesar;\u200b 12 o 13 de julio de 100 a. C.-15 de marzo de 44 a. C.) fue un político y militar romano.", "", "")]
    [InlineData("Gaius Julius Caesar (Latijnse uitspraak: [ˈgaːius ˈjuːlius ˈkaɪsar]; Latijn: C·IVLIVS·C·F·C·N·CAESAR, of Gaius Iulius Gaii filius Gaii nepos Caesar; na 42 v.Chr. IMP·C·IVLIVS·CAESAR·DIVVS, of Imperator Gaius Iulius Caesar divus) (Rome, 12 of 13 juli ± 100 v.Chr. – aldaar, Idus van maart (15 maart) 44 v.Chr.) was een Romeins politicus.", "Rome", "Rome")]
    public void ReadsCaesarInEveryEdition(string extract, string birthPlace, string deathPlace)
    {
        var lead = WikipediaImportService.ExtractLead(extract);

        Assert.Equal((-100, -44), (lead.BirthYear, lead.DeathYear));
        Assert.Equal((7, 13), (lead.BirthMonth, lead.BirthDay));
        Assert.Equal((3, 15), (lead.DeathMonth, lead.DeathDay));
        Assert.Equal((birthPlace, deathPlace), (lead.BirthPlace, lead.DeathPlace));
    }

    [Fact]
    public void SingleYearParenthesisInAnotherSentenceIsKept()
    {
        // La seconda parentesi è in un'altra frase: non sostituisce l'anno di nascita della prima.
        var extract = "Mario Rossi (born 1950) is a writer. He was mayor (1990–2000) of a town.";

        Assert.Equal((1950, (int?)null), WikipediaImportService.ExtractLeadYears(extract));
    }

    // Traiano (2026-11-12): in tedesco l'anno breve è seguito da una virgola e da un luogo incerto; in francese il nome
    // di nascita sta tra «né» e la data e la morte indica due giorni.
    [Fact]
    public void ShortYearFollowedByComma_AndUncertainPlace()
    {
        var lead = WikipediaImportService.ExtractLead("Trajan (* 18. September 53, vielleicht Italica, in Hispania Baetica oder in Rom; † 8. August 117 in Selinus, Kilikien) war römischer Kaiser.");
        Assert.Equal((53, 9, 18, 117, 8, 8), (lead.BirthYear, lead.BirthMonth, lead.BirthDay, lead.DeathYear, lead.DeathMonth, lead.DeathDay));
        Assert.StartsWith("Italica", lead.BirthPlace);
    }

    [Fact]
    public void French_BirthNameBeforeTheDate_AndTwoDeathDays()
    {
        var lead = WikipediaImportService.ExtractLead("Trajan, né sous le nom de Marcus Ulpius Traianus le 18 septembre 53 à Italica, en Bétique, et mort le 8 ou 9 août 117 à Selinus, en Cilicie, est empereur romain.");
        Assert.Equal((53, 9, 18), (lead.BirthYear, lead.BirthMonth, lead.BirthDay));
        Assert.Equal((117, 8, 9, 8), (lead.DeathYear, lead.DeathMonth, lead.DeathDay, lead.DeathDayAlt));
        Assert.Equal("Selinus", lead.DeathPlace);
    }

    // Se si legge solo la parte con «†», è la morte, non la nascita (Traiano risultava nato nel 117).
    [Fact]
    public void DeathMarker_OnlyDeathRead()
    {
        var lead = WikipediaImportService.ExtractLead("Trajan (* um das Jahr, irgendwo; † 8. August 117 in Selinus) war Kaiser.");
        Assert.Equal((null, 117, 8, 8), (lead.BirthYear, lead.DeathYear, lead.DeathMonth, lead.DeathDay));
    }

    [Theory]
    [InlineData("12 of 13 juli ", 13, 12)]
    [InlineData("le 8 ou 9 août ", 9, 8)]
    [InlineData("7 o 8 agosto ", 8, 7)]
    [InlineData("7. oder 8. August ", 8, 7)]
    [InlineData("23 o 24 de junio de ", 24, 23)]
    [InlineData("8 agosto ", 8, null)]
    public void AlternativeDay(string textBeforeYear, int day, int? alternative)
        => Assert.Equal(alternative, WikipediaImportService.AlternativeDay(textBeforeYear, day));
}
