using System.IO;
using System.Windows;
using HistoricEditor;
using Xunit;

namespace HistoricEditor.Tests;

public class RomanNamingTests
{
    private static HistoricalPerson Caesar() => new()
    {
        Name = "Gaio Giulio Cesare",
        Birth = "12 luglio 100 a.C.",
        Death = "15 marzo 44 a.C.",
        Sesso = Sessi.Maschio,
        Praenomen = "Gaius",
        Nomen = "Iulius",
        Cognomen = "Caesar",
        Filiazione = "C. f.",
        Tribu = "Fabia",
        Gens = "gens Iulia",
        Prenome = "Gaio",
        Gentilizio = "Giulio",
        CognomeRomano = "Cesare",
        LocalizedNames = [new LocalizedPersonName { Lingua = "en", LinguaNome = "inglese", Nome = "Julius Caesar", Prenome = "Gaius", Gentilizio = "Julius", CognomeRomano = "Caesar" }],
    };

    [Fact]
    public void LatinName_ComposesPartsAndPrefersFreeText()
    {
        Assert.Equal("Gaius Iulius Caesar", RomanNaming.LatinName("Gaius", " Iulius ", "Caesar", "", null));
        Assert.Equal("Publius Cornelius Scipio Africanus", RomanNaming.LatinName("Publius", "Cornelius", "Scipio", "Africanus", ""));
        Assert.Equal("Imperator Caesar Augustus", RomanNaming.LatinName("Gaius", "Octavius", null, null, " Imperator Caesar Augustus "));
        Assert.Equal(string.Empty, RomanNaming.LatinName(null, null, null, null, null));
    }

    [Fact]
    public void Epigraphic_AbbreviatesPraenomenAndTribe()
    {
        Assert.Equal("C. Iulius C. f. Fab. Caesar", RomanNaming.Epigraphic(Caesar()));
        Assert.Equal("M'. Curius Dentatus", RomanNaming.Epigraphic("Manius", "Curius", null, null, "Dentatus", null));

        // Abbreviazioni già scritte e praenomina sconosciuti restano come sono; senza praenomen e nomen non c'è forma epigrafica.
        Assert.Equal("C. Marius", RomanNaming.Epigraphic("C.", "Marius", null, null, null, null));
        Assert.Equal("Faustus Sulla", RomanNaming.Epigraphic("Faustus", null, null, null, "Sulla", null));
        Assert.Equal(string.Empty, RomanNaming.Epigraphic(null, null, "C. f.", "Fabia", "Caesar", null));
    }

    [Theory]
    [InlineData("Gaius", "C. f.")]
    [InlineData("gnaeus", "Cn. f.")]
    [InlineData("Tiberius", "Ti. f.")]
    [InlineData("", "")]
    public void Filiation_UsesFatherPraenomen(string father, string expected)
        => Assert.Equal(expected, RomanNaming.Filiation(father));

    [Theory]
    [InlineData("Iulius", "Iulia")]
    [InlineData("Antonius", "Antonia")]
    [InlineData("Livia", "Livia")]
    [InlineData("Giulio", "Giulia")]
    [InlineData("Julius", "Julia")]
    [InlineData("Julio", "Julia")]
    [InlineData("Mus", "Mus")]
    [InlineData("", "")]
    public void FeminineNomen_ChangesEnding(string nomen, string expected)
        => Assert.Equal(expected, RomanNaming.FeminineNomen(nomen));

    // Forma italiana dei nomi latini, per le parti di cui non si trova scritto il nome italiano (2026-11-13, da Marco Aurelio).
    [Theory]
    [InlineData("Marcus Aelius Aurelius Antoninus", "Marco Elio Aurelio Antonino")]
    [InlineData("Marcus Annius Catilius Severus", "Marco Annio Catilio Severo")]
    [InlineData("Gaius Iulius Caesar", "Gaio Giulio Cesare")]
    [InlineData("Julius", "Giulio")]
    [InlineData("Iulia", "Giulia")]
    [InlineData("Publius Aelius Hadrianus", "Publio Elio Adriano")]
    [InlineData("Lucius Septimius Severus", "Lucio Settimio Severo")]
    [InlineData("Gaius Octavius", "Gaio Ottavio")]
    [InlineData("Titus Flavius Domitianus", "Tito Flavio Domiziano")]
    [InlineData("Quintus Horatius Flaccus", "Quinto Orazio Flacco")]
    [InlineData("Marcus Tullius Cicero", "Marco Tullio Cicerone")]
    [InlineData("Publius Cornelius Scipio Africanus", "Publio Cornelio Scipione Africano")]
    [InlineData("Sextus Pompeius", "Sesto Pompeo")]
    [InlineData("Gnaeus Domitius", "Gneo Domizio")]
    [InlineData("Marcus Cocceius Nerva", "Marco Cocceio Nerva")]
    [InlineData("Lucius Annaeus Seneca", "Lucio Anneo Seneca")]
    [InlineData("Gaius Sallustius Crispus", "Gaio Sallustio Crispo")]
    [InlineData("Maximus", "Massimo")]
    [InlineData("Felix", "Felice")]
    [InlineData("Philippus", "Filippo")]
    [InlineData("Otho", "Otone")]
    [InlineData("Clemens", "Clemente")]
    [InlineData("Martialis", "Marziale")]
    [InlineData("Flavius Valerius Constantinus", "Flavio Valerio Costantino")]
    [InlineData("Constantius", "Costanzo")]
    [InlineData("Lucius Cornelius Sulla", "Lucio Cornelio Silla")]
    [InlineData("Pius", "Pio")]
    [InlineData("", "")]
    public void ItalianForm_AdaptsLatinNames(string latin, string expected)
        => Assert.Equal(expected, RomanNaming.ItalianForm(latin));

    [Theory]
    [InlineData("", "100 a.C.", "", false, true)]
    [InlineData("", "", "476", false, true)]
    [InlineData("", "800 a.C.", "", false, false)]
    [InlineData("", "1500", "", false, false)]
    [InlineData("", "1500", "", true, true)]
    [InlineData(Onomastiche.Standard, "100 a.C.", "", true, false)]
    [InlineData(Onomastiche.Romana, "1500", "", false, true)]
    public void IsRoman_FollowsChoiceThenPartsThenEra(string choice, string birth, string death, bool hasParts, bool expected)
        => Assert.Equal(expected, RomanNaming.IsRoman(choice, birth, death, hasParts));

    [Fact]
    public void Normalize_TrimsAndDropsEmptyRows()
    {
        var names = RomanNaming.Normalize(
        [
            new RomanNameRecord { Nome = " Gaius Octavius ", Motivo = "Nome alla nascita" },
            new RomanNameRecord { Nome = " ", Dal = "", Al = " " },
        ]);

        var name = Assert.Single(names);
        Assert.Equal("Gaius Octavius", name.Nome);
    }

    [Fact]
    public void ImportedLatinName_KeepsOnlyLatinDifferentFromComposed()
    {
        Assert.Equal(string.Empty, WikipediaImportService.ImportedLatinName([("Gaius  Iulius Caesar", "la")], "Gaius Iulius Caesar"));
        Assert.Equal(string.Empty, WikipediaImportService.ImportedLatinName([("Caius Julius Caesar", "fr")], "Gaius Iulius Caesar"));
        Assert.Equal("Imperator Caesar Divi filius Augustus",
            WikipediaImportService.ImportedLatinName([("Imperator Caesar Divi filius Augustus", "la")], "Gaius Octavius"));
    }

    [Fact]
    public void Sheet_ShowsRomanFieldsAndGens()
    {
        var fields = PersonSheet.MainFields(Caesar());

        Assert.Equal(
            ["Nome italiano", "Prenome", "Nome gentilizio", "Cognome", "Nome latino", "Forma epigrafica", "Praenomen", "Nomen", "Cognomen", "Filiazione", "Tribù", "Gens", "Sesso"],
            fields.Select(f => f.Etichetta));
        Assert.Equal(["Gaio", "Giulio", "Cesare"], fields.Skip(1).Take(3).Select(f => f.Valore));
        Assert.Equal("Gaius Iulius Caesar", fields[4].Valore);
        Assert.Equal("C. Iulius C. f. Fab. Caesar", fields[5].Valore);
        Assert.Equal("Gaius Iulius Caesar", PersonSheet.LatinSubtitle(Caesar()));

        // Con «Standard» la scheda torna quella consueta.
        var greek = Caesar();
        greek.Onomastica = Onomastiche.Standard;
        Assert.Equal("Gens", PersonSheet.MainFields(greek).Single(f => f.Valore == "gens Iulia").Etichetta);
        Assert.Equal(string.Empty, PersonSheet.LatinSubtitle(greek));
    }

    [Fact]
    public void Sheet_DescribesNamesOverTimeAndGens()
    {
        var names = PersonSheet.NamesOverTime(
        [
            new RomanNameRecord { Nome = "Gaius Octavius", Al = "44 a.C.", Motivo = "Nome alla nascita" },
            new RomanNameRecord { Nome = "Gaius Iulius Caesar", Dal = "44 a.C.", Al = "27 a.C.", Motivo = "Adozione testamentaria" },
            new RomanNameRecord { Nome = "Imperator Caesar Augustus", Dal = "27 a.C." },
        ]);

        Assert.Equal(["Nome alla nascita", "Adozione testamentaria", "Nome"], names.Select(n => n.Etichetta));
        Assert.Equal("Gaius Octavius (fino al 44 a.C.)", names[0].Valore);
        Assert.Equal("Gaius Iulius Caesar (dal 44 a.C. al 27 a.C.)", names[1].Valore);
        Assert.Equal("Imperator Caesar Augustus (dal 27 a.C.)", names[2].Valore);

        IReadOnlyList<Dinastia> dynasties =
        [
            new Dinastia { Id = 1, Nome = "gens Cornelia", Tipo = TipiDinastia.Gens },
            new Dinastia { Id = 2, Nome = "Cornelii Scipiones", ParentId = 1, Tipo = TipiDinastia.Gens },
        ];
        Assert.Equal("Gens romana", PersonSheet.DynastyPath("gens Cornelia", dynasties));
        Assert.Equal("Ramo di: gens Cornelia", PersonSheet.DynastyPath("Cornelii Scipiones", dynasties));
    }

    [Fact]
    public void PersonEntry_ShowsLatinNameOnlyWhenDifferent()
    {
        var entry = new PersonEntry(Caesar());
        Assert.True(entry.HasNomeLatino);
        Assert.Equal("Gaio Giulio Cesare\nGaius Iulius Caesar", entry.Suggerimento);

        var modern = new PersonEntry(new HistoricalPerson { Name = "Carlo", Suffix = "V", Birth = "1500" });
        Assert.False(modern.HasNomeLatino);
        Assert.Equal(modern.Nome, modern.Suggerimento);
    }

    [Fact]
    public void Store_KeepsRomanFieldsInUpdatesAndBackup()
    {
        var sourceDir = Path.Combine(Path.GetTempPath(), $"historic-roman-{Guid.NewGuid():N}");
        var restoreDir = Path.Combine(Path.GetTempPath(), $"historic-roman-{Guid.NewGuid():N}");
        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(restoreDir);
        try
        {
            using (var store = new PeopleStore(sourceDir))
            {
                var person = Caesar();
                store.Add(person);

                var edited = store.GetPerson(person.Id)!;
                edited.Onomastica = Onomastiche.Romana;
                edited.Agnomen = "Divus";
                edited.NomeLatino = "Divus Iulius";
                edited.NomiNelTempo = [new RomanNameRecord { Nome = "Divus Iulius", Dal = "42 a.C.", Motivo = "Titolo onorifico" }];
                store.Update(edited);

                var gens = store.EnsureDynasty("gens Iulia", "", null);
                store.SaveDynasty(new Dinastia { Id = gens, Nome = "gens Iulia", Tipo = TipiDinastia.Gens });
                store.ExportJson(store.LoadAll());
            }

            File.Copy(Path.Combine(sourceDir, "people.json"), Path.Combine(restoreDir, "people.json"));
            using var restored = new PeopleStore(restoreDir);
            var caesar = restored.LoadAll().Single();
            Assert.Equal(Onomastiche.Romana, caesar.Onomastica);
            Assert.Equal(("Gaius", "Iulius", "Caesar", "Divus"), (caesar.Praenomen, caesar.Nomen, caesar.Cognomen, caesar.Agnomen));
            Assert.Equal(("C. f.", "Fabia", "Divus Iulius"), (caesar.Filiazione, caesar.Tribu, caesar.NomeLatino));
            Assert.Equal(("Gaio", "Giulio", "Cesare"), (caesar.Prenome, caesar.Gentilizio, caesar.CognomeRomano));
            var english = Assert.Single(caesar.LocalizedNames);
            Assert.Equal(("Gaius", "Julius", "Caesar"), (english.Prenome, english.Gentilizio, english.CognomeRomano));
            var name = Assert.Single(caesar.NomiNelTempo);
            Assert.Equal(("Divus Iulius", "42 a.C.", "Titolo onorifico"), (name.Nome, name.Dal, name.Motivo));
            Assert.True(restored.GetDynasties().Single(d => d.Nome == "gens Iulia").IsGens);
            Assert.Equal("gens Iulia", caesar.Gens);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(sourceDir, recursive: true);
            Directory.Delete(restoreDir, recursive: true);
        }
    }

    [Fact]
    public void Dynasties_BecomeGensOnlyWhenNewOrConfirmedByWikidata()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"historic-roman-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            using var store = new PeopleStore(dir);
            var claudia = store.EnsureDynasty("gens Claudia", "", null, TipiDinastia.Gens);
            Assert.True(store.GetDynasties().Single(d => d.Id == claudia).IsGens);

            // Una dinastia già registrata non diventa gens per un nome scritto a mano…
            var giulio = store.EnsureDynasty("Giulio-Claudia", "", null);
            store.EnsureDynasty("Giulio-Claudia", "", null, TipiDinastia.Gens);
            Assert.False(store.GetDynasties().Single(d => d.Id == giulio).IsGens);

            // …ma sì se lo indica Wikidata.
            store.EnsureDynasty("Giulio-Claudia", "Q173322", null, TipiDinastia.Gens);
            Assert.True(store.GetDynasties().Single(d => d.Id == giulio).IsGens);

            // All'import le gens di Wikidata (con il ramo) vengono prima delle famiglie scritte.
            var import = new WikipediaImportResult
            {
                Dynasties = [new ImportedDynasty("Cornelii Scipiones", "Q1", "gens Cornelia", "Q2", Gens: true)],
            };
            Assert.True(ImportedPersonCreator.RegisterDynasties(store, "Cornelii Scipiones; Scipioni", import));
            var dynasties = store.GetDynasties();
            var cornelia = dynasties.Single(d => d.Nome == "gens Cornelia");
            var scipiones = dynasties.Single(d => d.Nome == "Cornelii Scipiones");
            Assert.True(cornelia.IsGens && scipiones.IsGens);
            Assert.Equal(cornelia.Id, scipiones.ParentId);
            Assert.False(dynasties.Single(d => d.Nome == "Scipioni").IsGens);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ImportAddsRomanPartsToEveryLanguage()
    {
        List<LocalizedPersonName> names = [new LocalizedPersonName { Lingua = "de", LinguaNome = "tedesco", Nome = "Gaius Iulius Caesar" }];

        var result = WikipediaImportService.AddRomanParts(names,
        [
            ("en", "inglese", new WikipediaImportService.RomanParts("Gaius", "Julius", "Caesar", "")),
            ("de", "tedesco", new WikipediaImportService.RomanParts("Gaius", "Iulius", "Caesar", "")),
            ("fr", "francese", new WikipediaImportService.RomanParts("", "", "", "")),
        ]);

        // Ordine delle lingue d'importazione; il tedesco conserva il nome, l'inglese viene creato, il francese vuoto no.
        Assert.Equal(["en", "de"], result.Select(n => n.Lingua));
        Assert.Equal(("Gaius", "Julius", "Caesar"), (result[0].Prenome, result[0].Gentilizio, result[0].CognomeRomano));
        Assert.Equal("Gaius Iulius Caesar", result[1].Nome);
        Assert.Equal("Iulius", result[1].Gentilizio);
    }

    [Fact]
    public void LocalRomanParts_ComeFromThePersonNameWhenWordsMatch()
    {
        var latin = new WikipediaImportService.RomanParts("Gaius", "Iulius", "Caesar", "");
        // Etichette italiane degli elementi rimaste in latino su Wikidata.
        var italianLabels = new WikipediaImportService.RomanParts("Gaius", "Iulius", "Caesar", "");

        Assert.Equal(new WikipediaImportService.RomanParts("Gaio", "Giulio", "Cesare", ""),
            WikipediaImportService.LocalRomanParts("Gaio Giulio Cesare", latin, italianLabels));

        // «Julius Caesar» non ha tutte le parti: restano le etichette degli elementi.
        var englishLabels = new WikipediaImportService.RomanParts("Gaius", "Julius", "Caesar", "");
        Assert.Equal(englishLabels, WikipediaImportService.LocalRomanParts("Julius Caesar", latin, englishLabels));

        // Più cognomina in una sola parte.
        Assert.Equal(new WikipediaImportService.RomanParts("Publio", "Cornelio", "Scipione Africano", ""),
            WikipediaImportService.LocalRomanParts("Publio Cornelio Scipione Africano",
                new WikipediaImportService.RomanParts("Publius", "Cornelius", "Scipio Africanus", ""),
                new WikipediaImportService.RomanParts("", "", "", "")));
    }

    [Fact]
    public void LocalRomanParts_UseAliasesWhenTheNameIsShort()
    {
        // Tito (Q1421): etichetta «Tito», alias «Tito Flavio Vespasiano»; le etichette degli elementi sono in latino.
        var latin = new WikipediaImportService.RomanParts("Titus", "Flavius", "Vespasianus", "");

        Assert.Equal(new WikipediaImportService.RomanParts("Tito", "Flavio", "Vespasiano", ""),
            WikipediaImportService.LocalRomanParts("Tito", ["Tito Flavio Vespasiano"], latin, latin));

        // In inglese vince l'alias più somigliante alle parti latine.
        Assert.Equal(latin, WikipediaImportService.LocalRomanParts("Titus",
            ["Titus Caesar Vespasianus", "Titus Flavius Vespasianus", "Titus Vespasianus Augustus", "Flavius Vespasianus Titus"],
            latin, latin));

        // Un alias senza parole somiglianti non vale: restano le etichette.
        var labels = new WikipediaImportService.RomanParts("Tito", "Flavio", "Vespasiano", "");
        Assert.Equal(labels, WikipediaImportService.LocalRomanParts("Tito", ["Imperatore dei Romani"], latin, labels));

        // Il nome principale con le parole giuste ha la precedenza a parità.
        Assert.Equal(new WikipediaImportService.RomanParts("Gaio", "Giulio", "Cesare", ""),
            WikipediaImportService.LocalRomanParts("Gaio Giulio Cesare", ["Caio Giulio Cesare"],
                new WikipediaImportService.RomanParts("Gaius", "Iulius", "Caesar", ""),
                new WikipediaImportService.RomanParts("Gaius", "Iulius", "Caesar", "")));
    }

    [Fact]
    public void LocalizedNamesWithOnlyRomanPartsAreKept()
    {
        var kept = LocalizedPersonName.Normalize([new LocalizedPersonName { Lingua = "en", Prenome = " Gaius " }]);

        Assert.Equal("Gaius", Assert.Single(kept).Prenome);
    }

    [Fact]
    public void OldLocalizedNamesWithoutRomanPartsStillLoad()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"historic-roman-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            int id;
            using (var store = new PeopleStore(dir))
            {
                var person = new HistoricalPerson { Name = "Carlo", Suffix = "V" };
                store.Add(person);
                id = person.Id;
            }

            // Nomi localizzati salvati prima dell'onomastica per lingua: mancano Prenome, Gentilizio, CognomeRomano e Agnome.
            using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(dir, "historic.db")}"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "UPDATE People SET LocalizedNames = '[{\"Lingua\":\"en\",\"LinguaNome\":\"inglese\",\"Nome\":\"Charles\",\"Suffisso\":\"V\",\"Soprannome\":\"\",\"TitoloPrincipale\":\"\"}]' WHERE Id = $id";
                command.Parameters.AddWithValue("$id", id);
                Assert.Equal(1, command.ExecuteNonQuery());
            }

            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            using var reopened = new PeopleStore(dir);
            var english = Assert.Single(reopened.GetPerson(id)!.LocalizedNames);
            Assert.Equal("Charles", english.Nome);
            Assert.Equal(string.Empty, english.Prenome ?? "null");
            Assert.Single(LocalizedPersonName.Normalize(reopened.GetPerson(id)!.LocalizedNames));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void EditorIsLockedWhileImportingFromWikipedia()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"historic-roman-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            WpfTestThread.Run(() =>
            {
                using var store = new PeopleStore(dir);
                var person = Caesar();
                store.Add(person);
                var editor = new PersonEditorWindow(store, store.GetPerson(person.Id));
                var setBusy = typeof(PersonEditorWindow).GetMethod("SetBusy", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

                // Import o aggiornamento: campi, altre tab, Salva bloccati; l'avanzamento con «Annulla» e «Chiudi» no.
                setBusy.Invoke(editor, [true, true]);
                Assert.False(editor.NameInput.IsEnabled);
                Assert.False(editor.PrenomeInput.IsEnabled);
                Assert.False(editor.LanguageSelector.IsEnabled);
                Assert.False(editor.SaveButton.IsEnabled);
                Assert.False(((UIElement)editor.FamilyTab.Content).IsEnabled);
                Assert.True(editor.RefreshPanel.IsEnabled);
                Assert.True(editor.LoadingPanel.IsEnabled);
                Assert.True(editor.CloseButton.IsEnabled);
                Assert.Equal(Visibility.Visible, editor.FormLockedNotice.Visibility);

                setBusy.Invoke(editor, [false, false]);
                Assert.True(editor.NameInput.IsEnabled);
                Assert.True(editor.SaveButton.IsEnabled);
                Assert.Equal(Visibility.Collapsed, editor.FormLockedNotice.Visibility);

                // La sola ricerca non blocca la scheda.
                setBusy.Invoke(editor, [true, false]);
                Assert.True(editor.NameInput.IsEnabled);
                editor.Close();
            });
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(dir, recursive: true);
        }
    }
}
