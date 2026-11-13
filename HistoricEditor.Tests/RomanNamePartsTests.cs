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
        Assert.Equal(6, principal.Parti.Count);
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
}
