using HistoricEditor;
using Xunit;

namespace HistoricEditor.Tests;

// Import completi di personaggi reali, con le risposte di Wikipedia/Wikidata salvate in Fixtures/Import
// (vedi ImportFixtures): nessuna rete, meno di un secondo per personaggio.
//
// Ogni personaggio ha due controlli:
//  - i fatti storici certi, verificati uno per uno;
//  - il riepilogo completo dell'import (Fixtures/Import/<pagina>.atteso.txt): qualsiasi cambiamento fa fallire
//    il test con le righe diverse, così una miglioria al parser non può peggiorare un import senza che ce ne accorgiamo.
//
// Per aggiungere un personaggio: copiare uno dei test con il titolo della pagina it.wikipedia, eseguire i test una
// volta con HISTORIC_RECORD_IMPORT=1 (scarica le risposte e crea il riepilogo), rivedere il riepilogo e scrivere i fatti.
public class ImportRegressionTests
{
    [Fact]
    public async Task GaioGiulioCesare()
    {
        const string page = "Gaio Giulio Cesare";
        var r = await ImportFixtures.ImportAsync(page);
        var person = ImportedPersonCreator.BuildPerson(r);

        Assert.Equal("Q1048", r.WikidataId);
        Assert.Equal("Gaio Giulio Cesare", person.DisplayName);
        Assert.Equal(Sessi.Maschio, r.Sesso);
        Assert.Empty(r.UnreadEditions);

        // Nome romano: latino, italiano e nelle altre lingue.
        Assert.Equal(("Gaius", "Caesar", ""), (r.Praenomen, r.Cognomen, r.Agnomen));
        Assert.Equal(("Gaio", "Giulio", "Cesare"), (r.Prenome, r.Gentilizio, r.CognomeRomano));
        Assert.Equal(("Gaius", "Julius", "Caesar"), RomanParts(r, "en"));
        Assert.Equal(("Gayo", "Julio", "César"), RomanParts(r, "es"));

        // Nessuna etichetta olandese su Wikidata: il nome viene dal titolo della voce, le parti dall'introduzione
        // olandese («Gaius Julius Caesar (Latijnse uitspraak…»), perché manca anche l'etichetta olandese di «Iulius».
        var dutch = r.LocalizedNames.Single(n => n.Lingua == "nl");
        Assert.Equal("Julius Caesar", dutch.Nome);
        Assert.Equal(("Gaius", "Julius", "Caesar"), RomanParts(r, "nl"));

        // Date: 13 luglio 100 a.C. – 15 marzo 44 a.C., giuliane, confermate da tutte e sei le edizioni.
        Assert.StartsWith("13/07/100 a.C.", person.Birth);
        Assert.Equal("15/03/44 a.C.", person.Death);
        Assert.Equal(HistoricalCalendar.Giuliano, person.BirthCalendario);
        Assert.Equal(HistoricalCalendar.Giuliano, person.DeathCalendario);
        Assert.Equal(6, WikipediaSources(r.BirthDates[0]));
        Assert.Equal(6, WikipediaSources(r.DeathDates[0]));

        // Nascita: 13 luglio (16 fonti) o 12 luglio (Wikidata e tre edizioni, «12 o 13 luglio»); il 102 a.C. ha un solo
        // riferimento Wikidata: scartato, resta tra le date discordanti (2026-11-12).
        Assert.Equal("13/07/100 a.C.; 12/07/100 a.C.", person.Birth);
        Assert.Equal("102 a.C.", Assert.Single(r.DiscardedBirthDates).Value);
        Assert.Contains("102 a.C.", DateDiscrepancy.Describe("Nascita", person.BirthDateEvidence));

        // Luoghi: nato a Roma, ucciso nel Teatro di Pompeo; le edizioni concordano.
        Assert.Equal("Q220", Assert.Single(r.BirthPlaces).WikidataId);
        Assert.Equal("Q944814", Assert.Single(r.DeathPlaces).WikidataId);

        // Il Teatro di Pompeo non esiste più («ex edificio» su Wikidata): luogo storico dentro Roma, di tipo teatro romano.
        // Il Tempio del Divo Giulio esiste ancora come rovine: luogo specifico di tipo «Sito archeologico», in rovina.
        var theatre = r.DeathPlaces[0];
        Assert.Equal(("Teatro di Pompeo", "Teatro romano"), (theatre.HistoricName, theatre.TypeLabel));
        var temple = Assert.Single(r.BurialPlaces);
        Assert.Equal(("", "Sito archeologico", CondizioniLuogo.InRovina), (temple.HistoricName, temple.TypeLabel, temple.SpecificCondition));
        Assert.All(r.WikipediaLeads.Where(l => !l.IsWikidata), l => Assert.Equal((-100, 7, 13, -44, 3, 15), (l.BirthYear, l.BirthMonth, l.BirthDay, l.DeathYear, l.DeathMonth, l.DeathDay)));
        Assert.All(r.WikipediaLeads.Where(l => !l.IsWikidata), l => Assert.Equal(("Q220", "Q220"), (l.BirthPlaceKey, l.DeathPlaceKey)));
        Assert.Equal(0, WikipediaConsensus.Evaluate(r.WikipediaLeads).DifferingCount);

        // Famiglia, titoli e parenti.
        Assert.Contains(r.Dynasties, d => d.Gens && d.WikidataId == "Q510193");
        Assert.Equal("Dittatore romano", r.MainTitle);
        Assert.Equal(5, r.Titles.Count(t => t.Titolo == "Console"));
        Assert.Equal(["Q314710"], r.FatherIds);
        Assert.Equal(["Q232725"], r.MotherIds);
        Assert.Contains("Q1405", r.ChildIds);

        ImportFixtures.AssertMatchesSnapshot(page, r);
    }

    [Fact]
    public async Task TitoImperatore()
    {
        const string page = "Tito (imperatore)";
        var r = await ImportFixtures.ImportAsync(page);
        var person = ImportedPersonCreator.BuildPerson(r);

        Assert.Equal("Q1421", r.WikidataId);
        Assert.Equal("Tito", person.DisplayName);
        Assert.Empty(r.UnreadEditions);

        // Nome romano: l'etichetta italiana è solo «Tito», le parti vengono dall'alias «Tito Flavio Vespasiano».
        Assert.Equal(("Titus", "Flavius", "Vespasianus"), (r.Praenomen, r.Nomen, r.Cognomen));
        Assert.Equal(("Tito", "Flavio", "Vespasiano"), (r.Prenome, r.Gentilizio, r.CognomeRomano));
        Assert.Equal(("Titus", "Flavius", "Vespasianus"), RomanParts(r, "en"));

        // Nomi nel tempo dal campo «nome completo» dell'infobox italiana (2026-11-14): l'introduzione dà solo il nome da
        // imperatore. Caesar prende il posto del nomen Flavius; da imperatore davanti c'è anche Imperator.
        Assert.Equal(["Tito Flavio Vespasiano", "Tito Cesare Vespasiano", "Imperatore Tito Cesare Vespasiano Augusto"], r.NomiNelTempo.Select(n => n.DisplayName));
        Assert.Equal(["Nome alla nascita", "Da figlio dell'imperatore", "Assunzione del potere"], r.NomiNelTempo.Select(n => n.Motivo));
        Assert.Equal(("39", "79"), (r.NomiNelTempo[0].Dal, r.NomiNelTempo[2].Dal));
        Assert.Equal([TipiParteNome.Imperator, TipiParteNome.Praenomen, TipiParteNome.Caesar, TipiParteNome.Cognomen, TipiParteNome.Augustus], r.NomiNelTempo[2].Parti.Select(p => p.Tipo));

        // Date: 30 dicembre 39 – 13 settembre 81. Anni a due cifre senza «d.C.» in tutte le introduzioni;
        // le date deprecate su Wikidata (40, 41) non entrano nella scheda.
        Assert.Equal("30/12/0039", person.Birth);
        Assert.Equal("13/09/0081", person.Death);
        Assert.Equal(6, WikipediaSources(Assert.Single(r.BirthDates)));
        Assert.Equal(6, WikipediaSources(Assert.Single(r.DeathDates)));
        Assert.All(r.WikipediaLeads.Where(l => !l.IsWikidata), l => Assert.Equal((39, 12, 30, 81, 9, 13), (l.BirthYear, l.BirthMonth, l.BirthDay, l.DeathYear, l.DeathMonth, l.DeathDay)));

        // Luoghi: Roma e Aquae Cutiliae. L'infobox inglese indica Napoli: unica edizione discordante.
        Assert.Equal("Q220", Assert.Single(r.BirthPlaces).WikidataId);
        Assert.Equal("Q383071", Assert.Single(r.DeathPlaces).WikidataId);
        Assert.Equal("Q383071", r.WikipediaLeads.Single(l => l.Language == "it").DeathPlaceKey);
        Assert.Equal("Q383071", r.WikipediaLeads.Single(l => l.Language == "de").DeathPlaceKey);
        var consensus = WikipediaConsensus.Evaluate(r.WikipediaLeads);
        Assert.Equal("inglese", Assert.Single(consensus.Editions, e => !e.Agrees).LanguageName);

        // Titoli e parenti: imperatore dopo Vespasiano (padre) e prima di Domiziano.
        Assert.Equal("Imperatore romano", r.MainTitle);
        var emperor = r.Titles.Single(t => t.Titolo == "Imperatore romano");
        Assert.Equal(("24/06/0079", "13/09/0081"), (emperor.Inizio, emperor.Fine));
        Assert.Equal(("Vespasiano", "Domiziano"), (emperor.Predecessore, emperor.Successore));
        Assert.Equal(["Q1419"], r.FatherIds);
        Assert.Contains(r.Dynasties, d => d.Gens && d.WikidataId == "Q12297672");

        // La «Dinastia flavia» è «parte di» un elenco di Wikipedia (list of Roman dynasties): non è una dinastia superiore.
        // Gens e dinastia sono campi separati (2026-11-12): gens Flavia e il suo ramo Flavii Sabini nel campo Gens, la
        // Dinastia flavia, senza superiore, nel campo Famiglia. Nell'elenco Tito compare solo sotto la dinastia.
        var flavian = r.Dynasties.Single(d => d.WikidataId == "Q200565");
        Assert.Equal((string.Empty, false), (flavian.ParentWikidataId, flavian.Gens));
        Assert.Equal("Dinastia flavia", r.Family);
        Assert.Equal("gens Flavia; Flavii Sabini", r.Gens);

        ImportFixtures.AssertMatchesSnapshot(page, r);
    }

    [Fact]
    public async Task Vespasiano()
    {
        const string page = "Vespasiano";
        var r = await ImportFixtures.ImportAsync(page);
        var person = ImportedPersonCreator.BuildPerson(r);

        Assert.Equal("Q1419", r.WikidataId);
        Assert.Equal("Vespasiano", person.DisplayName);
        Assert.Empty(r.UnreadEditions);

        // Nome: il cognomen «Sabinus» è contestato su Wikidata e non conta, e senza cognomen l'agnomen «Vespasianus»
        // diventa il cognomen (come per il figlio Tito); le parti italiane vengono dall'introduzione
        // («Nato Tito Flavio Vespasiano»), perché etichetta («Vespasiano») ed elementi Wikidata non le danno in italiano.
        Assert.Equal(("Titus", "Flavius", "Vespasianus", ""), (r.Praenomen, r.Nomen, r.Cognomen, r.Agnomen));
        Assert.Equal(("Tito", "Flavio", "Vespasiano", ""), (r.Prenome, r.Gentilizio, r.CognomeRomano, r.Agnome));
        Assert.Equal("", r.NomeLatino); // P1559 «Titus Flavius Vespasianus» coincide con il nome composto
        Assert.Equal(("Titus", "Flavius", "Vespasianus"), RomanParts(r, "de"));
        Assert.Equal(("Tito", "Flavio", "Vespasiano"), RomanParts(r, "es"));

        // Nomi nel tempo dal campo «nome completo» dell'infobox italiana (2026-11-14): nato Titus Flavius Vespasianus,
        // da imperatore Imperator Caesar Vespasianus Augustus, che è il principale; Imperator, Caesar e Augustus sono titoli.
        Assert.Equal(["Tito Flavio Vespasiano", "Imperatore Cesare Vespasiano Augusto"], r.NomiNelTempo.Select(n => n.DisplayName));
        Assert.Equal(("9", "Nome alla nascita"), (r.NomiNelTempo[0].Dal, r.NomiNelTempo[0].Motivo));
        Assert.Equal(("69", "Assunzione del potere", true), (r.NomiNelTempo[1].Dal, r.NomiNelTempo[1].Motivo, r.NomiNelTempo[1].Principale));
        Assert.Equal([TipiParteNome.Imperator, TipiParteNome.Caesar, TipiParteNome.Cognomen, TipiParteNome.Augustus], r.NomiNelTempo[1].Parti.Select(p => p.Tipo));

        // Date: 17 novembre 9 – 23 giugno 79 nelle introduzioni (anni a una e due cifre).
        Assert.Equal("17/11/0009", person.Birth);
        var italian = r.WikipediaLeads.Single(l => l.Language == "it");
        Assert.Equal((9, 11, 17, 79, 6, 23), (italian.BirthYear, italian.BirthMonth, italian.BirthDay, italian.DeathYear, italian.DeathMonth, italian.DeathDay));

        // Morte: Wikidata indica il 4 giugno 79 senza fonti, quattro edizioni il 23 giugno e la spagnola «23 o 24».
        // Vince il giorno con più fonti; le fonti sono solo le edizioni che lo riportano (2026-11-10). Il 4 giugno
        // (Wikidata) e il 24 (solo la spagnola) hanno una fonte sola: scartati, restano tra le date discordanti.
        Assert.Equal("23/06/0079", person.Death);
        var death = Assert.Single(r.DeathDates);
        Assert.Equal(["Wikipedia in italiano", "Wikipedia in tedesco", "Wikipedia in francese", "Wikipedia in spagnolo", "Wikipedia in olandese"], death.Sources.Select(s => s.Title));
        Assert.Equal(["04/06/0079", "24/06/0079"], r.DiscardedDeathDates.Select(d => d.Value).Order());
        var wikidataRow = Assert.Single(WikipediaConsensus.Evaluate(r.WikipediaLeads).Editions, e => e.IsWikidata);
        Assert.Equal(["giorno di morte"], wikidataRow.Differences);
        Assert.Contains("4 giugno 79", wikidataRow.Death);

        // Falacrine non indica un comune su Wikidata: si propone quello più vicino, Cittareale, da confermare.
        var falacrine = Assert.Single(r.BirthPlaces);
        Assert.Equal("Q927670", falacrine.WikidataId);
        Assert.Contains(falacrine.AdministrativeDivisions, d => d.Relation == PlaceDivision.NearbyMunicipalityRelation && d.WikidataId == "Q224043");

        // Località antica (2026-11-10): diventa un luogo storico. Wikidata non ha un nome italiano né latino: l'italiano
        // viene dall'introduzione della voce, il latino dall'infobox italiana.
        Assert.Equal(("Falacrine", "Vicus Phalacrinae"), (falacrine.HistoricName, falacrine.LatinName));

        // Aquae Cutiliae indica il comune (Castel Sant'Angelo): luogo storico creato senza conferma. La località si chiama
        // «Aquae Cutiliae», come nell'etichetta italiana (2026-11-12); le terme sono rovine ancora presenti: al suo interno
        // il luogo specifico «Terme di Cotilia» (dall'alias italiano), di tipo «Sito archeologico», in rovina.
        var cutiliae = Assert.Single(r.DeathPlaces);
        Assert.Equal("Q383071", cutiliae.WikidataId);
        Assert.DoesNotContain(cutiliae.AdministrativeDivisions, d => d.Relation == PlaceDivision.NearbyMunicipalityRelation);
        Assert.Equal(("Aquae Cutiliae", ""), (cutiliae.HistoricName, cutiliae.LatinName));
        Assert.Equal(("Terme di Cotilia", "Sito archeologico", CondizioniLuogo.InRovina), (cutiliae.HistoricBuildingName, cutiliae.HistoricBuildingType, cutiliae.SpecificCondition));

        // La sepoltura su Wikidata è una pagina di disambiguazione («Inglaterra»): scartata.
        Assert.Empty(r.BurialPlaces);

        // Infobox francese con due parametri sulla stessa riga: il luogo di nascita non ingloba la data di morte.
        Assert.Equal("Falacrine Italie", r.WikipediaLeads.Single(l => l.Language == "fr").BirthPlace);

        Assert.Equal("Imperatore romano", r.MainTitle);
        Assert.Equal(["Q1033173"], r.FatherIds);
        Assert.Contains("Q1421", r.ChildIds);

        ImportFixtures.AssertMatchesSnapshot(page, r);
    }

    [Fact]
    public async Task Traiano()
    {
        const string page = "Traiano";
        var r = await ImportFixtures.ImportAsync(page);
        var person = ImportedPersonCreator.BuildPerson(r);

        Assert.Equal("Q1425", r.WikidataId);
        Assert.Equal("Traiano", person.DisplayName);
        Assert.Empty(r.UnreadEditions);

        // Nascita: 18 settembre 53, da tutte e sei le edizioni. La tedesca («* 18. September 53, vielleicht Italica…;
        // † 8. August 117…») prima non leggeva la nascita e prendeva la morte per nascita (nato nel 117); la francese
        // («né sous le nom de Marcus Ulpius Traianus le 18 septembre 53») non si leggeva affatto. Il 15 settembre
        // (un solo riferimento Wikidata) è scartato.
        Assert.Equal("18/09/0053", person.Birth);
        Assert.Equal(6, WikipediaSources(Assert.Single(r.BirthDates)));
        Assert.Equal(["15/09/0053"], r.DiscardedBirthDates.Select(d => d.Value));
        var german = r.WikipediaLeads.Single(l => l.Language == "de");
        Assert.Equal((53, 9, 18, 117, 8, 8), (german.BirthYear, german.BirthMonth, german.BirthDay, german.DeathYear, german.DeathMonth, german.DeathDay));
        var french = r.WikipediaLeads.Single(l => l.Language == "fr");
        Assert.Equal((53, 9, 18), (french.BirthYear, french.BirthMonth, french.BirthDay));

        // Morte: le fonti indicano più giorni dell'agosto 117 («mort le 8 ou 9 août 117»): restano le date indicate da
        // almeno due fonti, prima quella con più fonti (8, poi 9, poi 7 agosto); l'11 (solo la spagnola) è scartato.
        Assert.Equal("08/08/0117; 09/08/0117; 07/08/0117", person.Death);
        Assert.Equal((9, 8), (french.DeathDay, french.DeathDayAlt));
        Assert.Equal(["11/08/0117"], r.DiscardedDeathDates.Select(d => d.Value));

        // Luoghi: Italica e Selinunte in Cilicia non esistono più (luoghi storici nei comuni di oggi); i loro resti sì:
        // luoghi specifici «Sito archeologico di …», in rovina.
        var italica = Assert.Single(r.BirthPlaces);
        Assert.Equal(("Q658893", "Italica"), (italica.WikidataId, italica.HistoricName));
        Assert.Equal(("Sito archeologico di Italica", "Sito archeologico", CondizioniLuogo.InRovina), (italica.HistoricBuildingName, italica.HistoricBuildingType, italica.SpecificCondition));
        var selinus = Assert.Single(r.DeathPlaces);
        Assert.Equal(("Q1424724", "Selinunte", "Sito archeologico di Selinunte"), (selinus.WikidataId, selinus.HistoricName, selinus.HistoricBuildingName));

        // Gens e dinastia separate: gens Ulpia e il ramo Ulpii Traiani nel campo Gens, la dinastia Nerva-Antonina (senza
        // etichetta né voce italiana su Wikidata) nel campo Famiglia, senza dinastia superiore.
        Assert.Equal("gens Ulpia; Ulpii Traiani", r.Gens);
        Assert.Equal("Nerva–Antonine dynasty", r.Family);
        var dynasty = r.Dynasties.Single(d => d.WikidataId == "Q3202662");
        Assert.Equal((false, string.Empty), (dynasty.Gens, dynasty.ParentWikidataId));

        // Nomi nel tempo dal campo «nome completo» dell'infobox italiana (2026-11-14): nato Marcus Ulpius Traianus, adottato da
        // Nerva (filiazione «Divi Nervae filius»), da imperatore con Imperator e Augustus; Germanicus è un agnomen trionfale.
        Assert.Equal(["Marco Ulpio Traiano", "Cesare Nerva Traiano Germanico", "Imperatore Cesare Nerva Traiano Augusto Germanico"], r.NomiNelTempo.Select(n => n.DisplayName));
        Assert.Equal(["Nome alla nascita", "Adozione", "Assunzione del potere"], r.NomiNelTempo.Select(n => n.Motivo));
        Assert.Equal(("Divi Nervae filius", TipiParteNome.Agnomen), (r.NomiNelTempo[2].Filiazione, r.NomiNelTempo[2].Parti[^1].Tipo));

        Assert.Equal("Imperatore romano", r.MainTitle);
        Assert.Contains("Q1427", r.ChildIds);
        ImportFixtures.AssertMatchesSnapshot(page, r);
    }

    [Fact]
    public async Task MarcoAurelio()
    {
        const string page = "Marco Aurelio";
        var r = await ImportFixtures.ImportAsync(page);
        var person = ImportedPersonCreator.BuildPerson(r);

        Assert.Equal("Q1430", r.WikidataId);
        Assert.Equal("Marco Aurelio", person.DisplayName);

        // Wikidata elenca i nomi di tutta la vita: Catilius e Annius, Severus e Verus sono abbandonati («causa della fine»
        // = cambio di nome per adozione). Il nome è quello attuale, Marcus Aelius Aurelius Antoninus; il nome italiano
        // completo non è scritto da nessuna parte, quindi le parti si adattano con le regole dell'italiano (2026-11-13).
        Assert.Equal(("Marcus", "Aelius Aurelius", "Antoninus"), (r.Praenomen, r.Nomen, r.Cognomen));
        Assert.Equal(("Marco", "Elio Aurelio", "Antonino"), (r.Prenome, r.Gentilizio, r.CognomeRomano));

        // Nato nella gens Annia (gens di nascita «sconosciuta» su Wikidata, ricavata dal nomen Annius), adottato nella gens
        // Aurelia (P5025 con causa «adozione»): due campi separati (2026-11-14); entrambe registrate come gens.
        Assert.Equal(("gens Annia", "gens Aurelia"), (r.Gens, r.GensAdozione));
        Assert.Contains(r.Dynasties, d => d.WikidataId == "Q906810" && d.Gens);
        Assert.Equal(("gens Annia", "gens Aurelia"), (person.Gens, person.GensAdozione));

        // Nomi nel tempo dal campo «nome completo» dell'infobox italiana (2026-11-14): Marcus Annius Catilius Severus alla
        // nascita, Marcus Annius Verus dopo il matrimonio, Marcus Aurelius Caesar Augusti filius dopo l'adozione, Imperator
        // Caesar Marcus Aurelius Antoninus Augustus da imperatore. L'italiano viene dalle coppie dell'introduzione.
        Assert.Equal(["Marco Annio Catilio Severo", "Marco Annio Vero", "Marco Aurelio Cesare", "Imperatore Cesare Marco Aurelio Antonino Augusto"], r.NomiNelTempo.Select(n => n.DisplayName));
        Assert.Equal(["Nome alla nascita", "Dopo il matrimonio", "Adozione", "Assunzione del potere"], r.NomiNelTempo.Select(n => n.Motivo));
        Assert.Equal([TipiParteNome.Praenomen, TipiParteNome.Nomen, TipiParteNome.Nomen, TipiParteNome.Cognomen], r.NomiNelTempo[0].Parti.Select(p => p.Tipo));
        Assert.Equal(("Augusti filius", TipiParteNome.Caesar), (r.NomiNelTempo[2].Filiazione, r.NomiNelTempo[2].Parti[2].Tipo));
        Assert.Equal(("121", "161"), (r.NomiNelTempo[0].Dal, r.NomiNelTempo[3].Dal));

        // Nella scheda le parti della persona sono il riassunto del nome principale.
        Assert.Equal(("Marcus", "Aurelius", "Antoninus"), (person.Praenomen, person.Nomen, person.Cognomen));
        Assert.Equal("Imperator Caesar Marcus Aurelius Antoninus Augustus", person.NomeLatino);
        Assert.Equal(4, person.NomiNelTempo.Count);

        ImportFixtures.AssertMatchesSnapshot(page, r);
    }

    [Fact]
    public async Task Augusto()
    {
        const string page = "Augusto";
        var r = await ImportFixtures.ImportAsync(page);
        var person = ImportedPersonCreator.BuildPerson(r);

        Assert.Equal("Q1405", r.WikidataId);

        // Nato nella gens Octavia (ramo Octavii Rufi), adottato per testamento da Cesare nella gens Iulia: su Wikidata il
        // ramo «Julii Caesares» ha la causa «adozione» e va con la gens d'adozione, non con quella di nascita (2026-11-14).
        Assert.DoesNotContain("Caesares", r.Gens);
        Assert.Contains("Ottavia", r.Gens);
        Assert.Contains("Caesares", r.GensAdozione);
        Assert.Contains("Giulia", r.GensAdozione);
        Assert.Equal((r.Gens, r.GensAdozione), (person.Gens, person.GensAdozione));

        // Nomi latini delle gens e dei rami (2026-11-14), dalle etichette incoerenti di Wikidata: «Iulii» → «gens Iulia»,
        // «Julii Caesares» → «Iulii Caesares», che è anche il nome italiano (manca l'etichetta italiana).
        Assert.Equal("gens Giulia; Iulii Caesares", r.GensAdozione);
        Assert.Equal(("gens Iulia", "Iulii Caesares", "gens Octavia"),
            (r.Dynasties.Single(d => d.WikidataId == "Q510193").NomeLatino, r.Dynasties.Single(d => d.WikidataId == "Q3268376").NomeLatino, r.Dynasties.Single(d => d.WikidataId == "Q1759265").NomeLatino));
        Assert.Equal("Domus Iulio-Claudia", r.Dynasties.Single(d => d.WikidataId == "Q192841").NomeLatino);

        // Octavianus è su Wikidata un agnomen «adoptive suffix nomina»: è il nomen d'origine (aggettivo dal nomen di
        // nascita Octavius), non un agnomen (2026-11-14).
        Assert.Equal(string.Empty, r.Agnomen);
        var adopted = r.NomiNelTempo.Single(n => n.LatinName == "Gaius Iulius Caesar Octavianus");
        Assert.Equal((TipiParteNome.NomenOrigine, "Octavianus"), (adopted.Parti[^1].Tipo, adopted.Parti[^1].Latino));

        // La filiazione è una riga nel punto in cui si legge (2026-11-14): Imperator Caesar Divi filius Augustus.
        var principal = r.NomiNelTempo[^1];
        Assert.Equal([TipiParteNome.Imperator, TipiParteNome.Cognomen, TipiParteNome.Filiazione, TipiParteNome.Augustus], principal.Parti.Select(p => p.Tipo));
        Assert.Equal(("Divi filius", "Imperator Caesar Augustus"), (principal.Filiazione, principal.LatinName));

        ImportFixtures.AssertMatchesSnapshot(page, r);
    }

    [Fact]
    public async Task GneoPompeoMagno()
    {
        const string page = "Gneo Pompeo Magno";
        var r = await ImportFixtures.ImportAsync(page);

        Assert.Equal("Q125414", r.WikidataId);

        // Nomi nel tempo dal campo «nome completo» (2026-11-14): la nota dopo «Gnaeus Pompeius», con il titolo in corsivo
        // «Guerre mitridatiche» di Appiano, non diventa un nome.
        Assert.Equal(["Gnaeus Pompeius", "Gnaeus Pompeius Magnus"], r.NomiNelTempo.Select(n => n.LatinName));
        Assert.Equal("Gnaeus Pompeius Magnus", r.NomiNelTempo.Single(n => n.Principale).LatinName);

        // Nascita nella Regio V Picenum, regione dell'Italia augustea («Regioni dell'Italia augustea», il nome del tipo su Wikidata): luogo storico sotto lo Stato di oggi, da confermare.
        var picenum = Assert.Single(r.BirthPlaces);
        Assert.Equal(("Q510990", "Regio V Picenum", "Regioni dell'Italia augustea"), (picenum.WikidataId, picenum.HistoricName, picenum.TypeLabel));
        Assert.Equal(string.Empty, picenum.HistoricBuildingName);
        var picenumLevels = ImportSnapshot.DerivePlace(picenum);
        Assert.Equal(["Stato Italia"], picenumLevels.Levels.Select(l => $"{l.Tipo} {l.Nome}"));
        Assert.False(picenumLevels.Confident);

        // Morte a Pelusio, sito archeologico egizio: luogo storico nel governatorato di Porto Said, non nella città
        // (a 30 km), con i resti come luogo specifico in rovina.
        var pelusium = Assert.Single(r.DeathPlaces);
        Assert.Equal(("Q1145715", "Pelusio"), (pelusium.WikidataId, pelusium.HistoricName));
        Assert.Equal(("Sito archeologico di Pelusio", "Sito archeologico", CondizioniLuogo.InRovina), (pelusium.HistoricBuildingName, pelusium.HistoricBuildingType, pelusium.SpecificCondition));
        var pelusiumLevels = ImportSnapshot.DerivePlace(pelusium);
        Assert.Equal(["Stato Egitto", "Governatorato Governatorato di Porto Said"], pelusiumLevels.Levels.Select(l => $"{l.Tipo} {l.Nome}"));
        Assert.True(pelusiumLevels.Confident);

        ImportFixtures.AssertMatchesSnapshot(page, r);
    }

    private static (string, string, string) RomanParts(WikipediaImportResult r, string language)
        => r.LocalizedNames.Single(n => n.Lingua == language) is var n ? (n.Prenome, n.Gentilizio, n.CognomeRomano) : default;

    private static int WikipediaSources(HistoricalDateEvidence evidence)
        => evidence.Sources.Count(s => s.Title.StartsWith("Wikipedia in ", StringComparison.Ordinal));
}
