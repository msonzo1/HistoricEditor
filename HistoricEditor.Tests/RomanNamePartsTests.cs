using System.IO;
using HistoricEditor;
using Xunit;

namespace HistoricEditor.Tests;

// Parti del nome romano come righe ordinate (tipo, latino, italiano), 2026-11-14.
public class RomanNamePartsTests
{
    private static RomanNamePart Part(string tipo, string latino, string italiano) => new() { Tipo = tipo, Latino = latino, Italiano = italiano };

    // Nome da imperatore di Marco Aurelio: titoli prima e dopo, nell'ordine scritto.
    private static RomanNameRecord Emperor() => new()
    {
        Parti =
        [
            Part(TipiParteNome.Titolo, "Imperator", "Imperatore"),
            Part(TipiParteNome.Titolo, "Caesar", "Cesare"),
            Part(TipiParteNome.Praenomen, "Marcus", "Marco"),
            Part(TipiParteNome.Nomen, "Aurelius", "Aurelio"),
            Part(TipiParteNome.Cognomen, "Antoninus", "Antonino"),
            Part(TipiParteNome.Titolo, "Augustus", "Augusto"),
        ],
        Filiazione = "M. f.",
        Dal = "161",
        Motivo = "Assunzione del potere",
        Principale = true,
    };

    [Fact]
    public void Record_ComposesNamesInWrittenOrder()
    {
        var name = Emperor();
        Assert.Equal("Imperator Caesar Marcus Aurelius Antoninus Augustus", name.LatinName);
        Assert.Equal("Imperatore Cesare Marco Aurelio Antonino Augusto", name.ItalianName);
        Assert.Equal("IMP. CAES. M. Aurelius M. f. Antoninus AVG.", name.Epigraphic);

        name.FieldsFromParts();
        Assert.Equal(("Marcus", "Aurelius", "Antoninus", ""), (name.Praenomen, name.Nomen, name.Cognomen, name.Agnomen));
        Assert.Equal(("Marco", "Aurelio", "Antonino"), (name.Prenome, name.Gentilizio, name.CognomeRomano));
    }

    // Più nomina o cognomina: una riga ciascuno; il riassunto li unisce con uno spazio.
    [Fact]
    public void Record_KeepsSeveralNominaAndCognomina()
    {
        var name = new RomanNameRecord
        {
            Parti =
            [
                Part(TipiParteNome.Praenomen, "Marcus", "Marco"),
                Part(TipiParteNome.Nomen, "Annius", "Annio"),
                Part(TipiParteNome.Nomen, "Catilius", "Catilio"),
                Part(TipiParteNome.Cognomen, "Severus", "Severo"),
            ],
        };
        name.FieldsFromParts();
        Assert.Equal(("Annius Catilius", "Annio Catilio"), (name.Nomen, name.Gentilizio));
        Assert.Equal("M. Annius Catilius Severus", name.Epigraphic);
    }

    // Nomi salvati prima delle righe: una riga per parola, con l'italiano accanto se le parole sono tante quante in latino.
    [Fact]
    public void PartsFromFields_SplitsWordsAndPairsItalian()
    {
        var name = new RomanNameRecord { Praenomen = "Marcus", Nomen = "Aelius Aurelius", Cognomen = "Antoninus", Prenome = "Marco", Gentilizio = "Elio Aurelio", CognomeRomano = "Antonino Pio Augusto" };
        name.PartsFromFields();

        Assert.Equal(
            [("Praenomen", "Marcus", "Marco"), ("Nomen", "Aelius", "Elio"), ("Nomen", "Aurelius", "Aurelio"), ("Cognomen", "Antoninus", "Antonino Pio Augusto")],
            name.Parti.Select(p => (p.Tipo, p.Latino, p.Italiano)));
    }

    // Nel modulo l'italiano vuoto (o ancora quello proposto) segue il latino; uno scritto a mano resta.
    [Fact]
    public void Part_ProposesItalianOnlyInTheForm()
    {
        var part = new RomanNamePart { Tipo = TipiParteNome.Nomen, AutoItalian = true };
        part.Latino = "Aelius";
        Assert.Equal("Elio", part.Italiano);
        part.Latino = "Aurelius";
        Assert.Equal("Aurelio", part.Italiano);
        part.Italiano = "Aurelio (adottivo)";
        part.Latino = "Aurelia";
        Assert.Equal("Aurelio (adottivo)", part.Italiano);

        var stored = new RomanNamePart { Latino = "Aelius" };
        Assert.Equal(string.Empty, stored.Italiano);
    }

    // Il principale con i titoli: nella persona il riassunto e il nome latino completo; riletto, restano righe e ordine.
    [Fact]
    public void SetNames_KeepsTitlesAndOrderOfPrincipal()
    {
        var person = new HistoricalPerson { Name = "Marco Aurelio" };
        RomanNaming.SetNames(person, [Emperor()]);

        Assert.Equal(("Marcus", "Aurelius", "Antoninus"), (person.Praenomen, person.Nomen, person.Cognomen));
        Assert.Equal("Imperator Caesar Marcus Aurelius Antoninus Augustus", person.NomeLatino);
        Assert.Single(person.NomiNelTempo);

        var principal = Assert.Single(RomanNaming.NamesOf(person));
        // La filiazione del campo (nomi salvati prima delle righe di filiazione) diventa una riga dopo il nomen.
        Assert.Equal(7, principal.Parti.Count);
        Assert.Equal((TipiParteNome.Filiazione, "M. f."), (principal.Parti[4].Tipo, principal.Parti[4].Latino));
        Assert.Equal(string.Empty, principal.NomeLatino);
        Assert.Equal("IMP. CAES. M. Aurelius M. f. Antoninus AVG.", RomanNaming.Epigraphic(person));

        // Se l'import cambia i campi della persona, il principale si ricompone da quelli.
        person.Cognomen = "Antoninus Pius";
        person.NomeLatino = string.Empty;
        var updated = Assert.Single(RomanNaming.NamesOf(person));
        Assert.Equal("Marcus Aurelius Antoninus Pius", updated.LatinName);
    }

    [Fact]
    public void Store_KeepsPartsOfNames()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"historic-parts-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            int id;
            using (var store = new PeopleStore(dir))
            {
                var person = new HistoricalPerson { Name = "Marco Aurelio", Onomastica = Onomastiche.Romana };
                RomanNaming.SetNames(person,
                [
                    new RomanNameRecord { Parti = [Part(TipiParteNome.Praenomen, "Marcus", "Marco"), Part(TipiParteNome.Nomen, "Annius", "Annio"), Part(TipiParteNome.Cognomen, "Verus", "Vero")], Motivo = "Nome alla nascita" },
                    Emperor(),
                ]);
                store.Add(person);
                id = person.Id;
            }

            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            using var reopened = new PeopleStore(dir);
            var names = RomanNaming.NamesOf(reopened.GetPerson(id)!);
            Assert.Equal(["Marco Annio Vero", "Imperatore Cesare Marco Aurelio Antonino Augusto"], names.Select(n => n.DisplayName));
            Assert.Equal([false, true], names.Select(n => n.Principale));
            Assert.Equal(TipiParteNome.Titolo, names[1].Parti[0].Tipo);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(dir, recursive: true);
        }
    }

    // Nomi nel tempo dall'introduzione: solo coppie italiano/latino dello stesso nome; senza altri nomi oltre a quello
    // iniziale non si importa nulla (resta il nome di Wikidata).
    [Fact]
    public void RomanNamesOverTime_ReadsOnlyNamePairs()
    {
        var known = new Dictionary<string, string> { ["Iulius"] = TipiParteNome.Nomen, ["Caesar"] = TipiParteNome.Cognomen };
        Assert.Empty(WikipediaImportService.RomanNamesOverTime(
            "Gaio Giulio Cesare (in latino: Gaius Iulius Caesar; Roma, 13 luglio 100 a.C. – Roma, 15 marzo 44 a.C.) è stato un militare. Nato a Roma (Italia) da famiglia patrizia…",
            known, -100, null, ruler: false));

        var names = WikipediaImportService.RomanNamesOverTime(
            "Imperatore Cesare Augusto (in latino Imperator Caesar Augustus; Roma, 23 settembre 63 a.C. – Nola, 19 agosto 14) è stato il primo imperatore. Nato Gaio Ottavio Turino (Gaius Octavius Thurinus), divenne Gaio Giulio Cesare Ottaviano (Gaius Iulius Caesar Octavianus) con l'adozione.",
            known, -63, -27, ruler: true);
        Assert.Equal(["Gaio Ottavio Turino", "Gaio Giulio Cesare Ottaviano", "Imperatore Cesare Augusto"], names.Select(n => n.DisplayName));
        Assert.Equal(("63 a.C.", "27 a.C."), (names[0].Dal, names[2].Dal));
        // Dopo il nomen Iulius «Caesar» è un cognomen, altrove un titolo.
        Assert.Equal(TipiParteNome.Cognomen, names[1].Parti[2].Tipo);
        Assert.Equal([TipiParteNome.Titolo, TipiParteNome.Titolo, TipiParteNome.Titolo], names[2].Parti.Select(p => p.Tipo));
    }

    // Forme nelle altre lingue per riga (2026-11-14): ogni parte ha le sue, il nome completo si compone nell'ordine.
    [Fact]
    public void Translations_ComposeFullNameAndSummary()
    {
        var name = Emperor();
        name.TranslationsFrom("es", "Marco", "Aurelio", "Antonino", "");
        name.Parti[1].SetTranslation("es", "César");
        name.Parti[5].SetTranslation("es", "Augusto");

        Assert.Equal("César Marco Aurelio Antonino Augusto", name.FullName("es"));
        Assert.Equal(5, name.TranslatedCount("es"));
        Assert.Equal(("Marco", "Aurelio", "Antonino", ""), name.Summary("es"));
        Assert.Equal(["es"], name.Languages);

        // Più parole che righe dello stesso tipo: tutte nella prima.
        var several = new RomanNameRecord { Parti = [Part(TipiParteNome.Praenomen, "Marcus", "Marco"), Part(TipiParteNome.Nomen, "Aurelius", "Aurelio")] };
        several.TranslationsFrom("en", "Marcus", "Aelius Aurelius", "", "");
        Assert.Equal("Aelius Aurelius", several.Parti[1].Translation("en"));
    }

    // Il principale riporta le sue forme nelle parti per lingua della persona (una lingua nuova si aggiunge, una tolta
    // perde le parti); riletto, le forme tornano nelle righe. Le schede salvate prima le ricavano dalle parti per lingua.
    [Fact]
    public void SetNames_SyncsLanguagesOfPrincipal()
    {
        var person = new HistoricalPerson
        {
            Name = "Marco Aurelio",
            LocalizedNames = [new LocalizedPersonName { Lingua = "fr", LinguaNome = "francese", Nome = "Marc Aurèle", Prenome = "Marc", Gentilizio = "Aurèle" }],
        };
        var emperor = Emperor();
        emperor.TranslationsFrom("es", "Marco", "Aurelio", "Antonino", "");
        RomanNaming.SetNames(person, [emperor]);

        var spanish = person.LocalizedNames.Single(l => l.Lingua == "es");
        Assert.Equal(("spagnolo", "Marco", "Aurelio", "Antonino"), (spanish.LinguaNome, spanish.Prenome, spanish.Gentilizio, spanish.CognomeRomano));
        var french = person.LocalizedNames.Single(l => l.Lingua == "fr");
        Assert.Equal(("Marc Aurèle", ""), (french.Nome, french.Prenome));

        var principal = Assert.Single(RomanNaming.NamesOf(person));
        Assert.Equal("Marco Aurelio Antonino", principal.FullName("es"));

        var old = new HistoricalPerson
        {
            Name = "Gaio Giulio Cesare", Praenomen = "Gaius", Nomen = "Iulius", Cognomen = "Caesar",
            LocalizedNames = [new LocalizedPersonName { Lingua = "en", LinguaNome = "inglese", Prenome = "Gaius", Gentilizio = "Julius", CognomeRomano = "Caesar" }],
        };
        Assert.Equal("Gaius Julius Caesar", Assert.Single(RomanNaming.NamesOf(old)).FullName("en"));
    }

    [Fact]
    public void Store_KeepsTranslationsOfParts()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"historic-parts-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            int id;
            using (var store = new PeopleStore(dir))
            {
                var person = new HistoricalPerson { Name = "Marco Aurelio", Onomastica = Onomastiche.Romana };
                var birth = new RomanNameRecord { Parti = [Part(TipiParteNome.Praenomen, "Marcus", "Marco"), Part(TipiParteNome.Nomen, "Annius", "Annio"), Part(TipiParteNome.Cognomen, "Verus", "Vero")], Motivo = "Nome alla nascita" };
                birth.Parti[1].SetTranslation("en", "Annius");
                RomanNaming.SetNames(person, [birth, Emperor()]);
                store.Add(person);
                id = person.Id;
            }

            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            using var reopened = new PeopleStore(dir);
            var names = RomanNaming.NamesOf(reopened.GetPerson(id)!);
            Assert.Equal("Annius", names[0].FullName("en"));
            Assert.Equal(1, names[0].TranslatedCount("en"));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(dir, recursive: true);
        }
    }

    // Campo «nome completo» dell'infobox italiana, nei formati usati dalle voci (parentesi, punto e virgola, <br>).
    [Fact]
    public void InfoboxRomanNames_ReadsNamesAndMoments()
    {
        var wikitext = "{{Magistrato romano\n|nome = Tito\n|nome completo = ''Titus Flavius Vespasianus'' (alla nascita)<br />''Titus Caesar Vespasianus'' (da figlio dell'imperatore)<br />''Imperator Titus Caesar Vespasianus Augustus'' (dopo l'ascesa al potere imperiale)\n|titolo = [[Imperatore romano]]\n}}";
        var value = WikipediaImportService.InfoboxFullNameValue(wikitext);
        Assert.Equal(
            [("Titus Flavius Vespasianus", "alla nascita"), ("Titus Caesar Vespasianus", "da figlio dell'imperatore"), ("Imperator Titus Caesar Vespasianus Augustus", "dopo l'ascesa al potere imperiale")],
            WikipediaImportService.InfoboxRomanNames(value).Select(n => (n.Latin, n.Moment)));

        Assert.Equal(
            [("Marcus Annius Verus", "dopo il matrimonio"), ("Marcus Aurelius Caesar Augusti filius", "dopo l'adozione")],
            WikipediaImportService.InfoboxRomanNames("''Marcus Annius Verus'' dopo il matrimonio; ''Marcus Aurelius Caesar Augusti filius'' dopo l'adozione").Select(n => (n.Latin, n.Moment)));

        // Un solo nome (Cesare) non dà nomi nel tempo: resta il nome di Wikidata.
        Assert.Empty(WikipediaImportService.RomanNamesOverTime(null, new Dictionary<string, string>(), -100, null, ruler: false, "''Gaius Iulius Caesar''"));

        var names = WikipediaImportService.RomanNamesOverTime(null, new Dictionary<string, string>(), 39, 79, ruler: true, value);
        Assert.Equal(["Tito Flavio Vespasiano", "Tito Cesare Vespasiano", "Imperatore Tito Cesare Vespasiano Augusto"], names.Select(n => n.DisplayName));
        Assert.Equal([false, false, true], names.Select(n => n.Principale));
    }

    // Infobox e introduzione unite (2026-11-14): un nome presente in entrambe una volta sola (l'introduzione omette
    // «Imperator»: vale la forma dell'infobox), uno presente in una sola si aggiunge.
    [Fact]
    public void RomanNamesOverTime_MergesInfoboxAndIntroduction()
    {
        const string introduction = "Cesare Marco Aurelio Antonino Augusto (in latino Caesar Marcus Aurelius Antoninus Augustus; Roma, 26 aprile 121 – Sirmio, 17 marzo 180) è stato un imperatore. Nato Marco Annio Catilio Severo (Marcus Annius Catilius Severus), divenne Marco Annio Vero (Marcus Annius Verus) e assunse quindi il nome di Marco Aurelio Cesare figlio dell'Augusto (Marcus Aurelius Caesar Augusti filius).";
        const string infobox = "''Marcus Annius Catilius Severus'' alla nascita; ''Marcus Aurelius Caesar Augusti filius'' dopo l'adozione; ''Imperator Caesar Marcus Aurelius Antoninus Augustus'' dopo l'ascesa al potere imperiale";

        var names = WikipediaImportService.RomanNamesOverTime(introduction, new Dictionary<string, string>(), 121, 161, ruler: true, infobox);

        Assert.Equal(
            ["Marco Annio Catilio Severo", "Marco Annio Vero", "Marco Aurelio Cesare", "Imperatore Cesare Marco Aurelio Antonino Augusto"],
            names.Select(n => n.DisplayName));
        // Marco Annio Vero è solo nell'introduzione: va prima del nome che lo segue nel testo, quello dell'adozione.
        Assert.Equal(["Nome alla nascita", "Cambio di nome", "Adozione", "Assunzione del potere"], names.Select(n => n.Motivo));
        Assert.Equal([false, false, false, true], names.Select(n => n.Principale));
    }

    // Filiazione e tribù sono righe nel punto in cui si leggono (2026-11-14): fuori dal nome latino e italiano, abbreviate
    // nella forma epigrafica.
    [Fact]
    public void FiliationAndTribeRows_StayInPlaceOnlyInEpigraphicForm()
    {
        var cicero = new RomanNameRecord
        {
            Parti =
            [
                Part(TipiParteNome.Praenomen, "Marcus", "Marco"),
                Part(TipiParteNome.Nomen, "Tullius", "Tullio"),
                Part(TipiParteNome.Filiazione, "M. f.", ""),
                Part(TipiParteNome.Tribu, "Cornelia", ""),
                Part(TipiParteNome.Cognomen, "Cicero", "Cicerone"),
            ],
        };
        Assert.Equal("Marcus Tullius Cicero", cicero.LatinName);
        Assert.Equal("Marco Tullio Cicerone", cicero.DisplayName);
        Assert.Equal("M. Tullius M. f. Cor. Cicero", cicero.Epigraphic);

        var augustus = new RomanNameRecord
        {
            Parti =
            [
                Part(TipiParteNome.Titolo, "Imperator", "Imperatore"),
                Part(TipiParteNome.Titolo, "Caesar", "Cesare"),
                Part(TipiParteNome.Filiazione, "Divi filius", "figlio del divo"),
                Part(TipiParteNome.Titolo, "Augustus", "Augusto"),
            ],
        };
        Assert.Equal("Imperator Caesar Augustus", augustus.LatinName);
        Assert.Equal("Imperatore Cesare Augusto", augustus.DisplayName);
        Assert.Equal("DIVI f.", RomanNaming.AbbreviateFiliation("Divi filius"));

        // Il riassunto nei campi viene dalle righe; una riga tolta non torna dal campo.
        var saved = Assert.Single(RomanNaming.Normalize([cicero]));
        Assert.Equal(("M. f.", "Cornelia"), (saved.Filiazione, saved.Tribu));
        saved.Parti.RemoveAll(p => p.Tipo == TipiParteNome.Tribu);
        (saved.Filiazione, saved.Tribu) = (string.Empty, string.Empty);
        saved.FieldsFromParts();
        Assert.Equal(string.Empty, Assert.Single(RomanNaming.Normalize([saved])).Tribu);
    }

    // Un nome con solo i campi (persona senza nomi nel tempo) ha le righe nell'ordine consueto, filiazione e tribù dopo il
    // nomen; resta tutto nei campi della persona.
    [Fact]
    public void PartsFromFields_PutsFiliationAndTribeAfterNomen()
    {
        var name = new RomanNameRecord { Praenomen = "Gaius", Nomen = "Iulius", Cognomen = "Caesar", Filiazione = "C. f.", Tribu = "Fabia" };
        name.PartsFromFields();
        Assert.Equal([TipiParteNome.Praenomen, TipiParteNome.Nomen, TipiParteNome.Filiazione, TipiParteNome.Tribu, TipiParteNome.Cognomen], name.Parti.Select(p => p.Tipo));

        var person = new HistoricalPerson { Name = "Cesare" };
        RomanNaming.SetNames(person, [name]);
        Assert.Empty(person.NomiNelTempo);
        Assert.Equal(("C. f.", "Fabia"), (person.Filiazione, person.Tribu));
        Assert.Equal("C. Iulius C. f. Fab. Caesar", RomanNaming.Epigraphic(person));
    }

    // Il -ianus di un adottato è l'aggettivo dal suo nomen di nascita, non un agnomen (2026-11-14): tipo «Nomen d'origine»,
    // riconosciuto da un nomen noto della persona; dà la gens di nascita se manca. L'adozione per testamento ha il suo motivo.
    [Fact]
    public void OriginNomen_IsAdjectiveFromBirthNomen()
    {
        Assert.Equal("Octavius", RomanNaming.OriginNomen("Octavianus"));
        Assert.Equal("Aemilius", RomanNaming.OriginNomen("Aemilianus"));
        Assert.Equal(string.Empty, RomanNaming.OriginNomen("Caesar"));
        Assert.Equal("gens Ottavia", RomanNaming.GensOfOriginNomen("Octavianus"));
        Assert.Equal("gens Emilia", RomanNaming.GensOfOriginNomen("Aemilianus"));

        // Wikidata indica Octavius come nomen (abbandonato) e Octavianus come agnomen: vale il nomen d'origine.
        var known = new Dictionary<string, string> { ["Octavius"] = TipiParteNome.Nomen, ["Octavianus"] = TipiParteNome.Agnomen };
        var names = WikipediaImportService.RomanNamesOverTime(null, known, -63, -27, ruler: true,
            "''Gaius Octavius'' (alla nascita)<br />''Gaius Iulius Caesar Octavianus'' (dopo l'adozione testamentaria)<br />''Imperator Caesar Divi filius Augustus'' (dopo l'ascesa al principato)");
        var adopted = names[1];
        Assert.Equal([TipiParteNome.Praenomen, TipiParteNome.Nomen, TipiParteNome.Cognomen, TipiParteNome.NomenOrigine], adopted.Parti.Select(p => p.Tipo));
        Assert.Equal(("Gaio Giulio Cesare Ottaviano", "Adozione testamentaria"), (adopted.DisplayName, adopted.Motivo));
        Assert.Equal(string.Empty, adopted.Agnomen);
        Assert.Equal("gens Ottavia", WikipediaImportService.GensOfOriginNomen(names));

        // Un nome con il nomen d'origine non è «semplice»: resta fra i nomi nel tempo con la sua riga.
        var person = new HistoricalPerson { Name = "Ottaviano" };
        adopted.Motivo = string.Empty;
        RomanNaming.SetNames(person, [adopted]);
        Assert.Equal(TipiParteNome.NomenOrigine, Assert.Single(person.NomiNelTempo).Parti[^1].Tipo);
        Assert.Contains("Convenzione storiografica", RomanNameRecord.Reasons);
    }
}
