using System.IO;
using System.Windows;
using HistoricEditor;
using Xunit;

namespace HistoricEditor.Tests;

// Gentes e rami (2026-11-15): entità separate dalle dinastie, con le forme latine salvate come sono scritte e le etichette
// composte ricavate. Gens di nascita e d'adozione in collegamenti separati (2026-11-14).
public class GentesTests
{
    private static GensRomana Iulia() => new() { Id = 1, Nome = "Iulia", Nomen = "Iulius", Plurale = "Iulii", NomeItaliano = "Giulia", PluraleItaliano = "Giulii" };

    [Fact]
    public void Labels_AreDerivedFromTheForms()
    {
        var iulia = Iulia();
        var caesares = new RamoGens { Id = 1, GensId = 1, Gens = iulia, Cognomen = "Caesar", Plurale = "Caesares", NomeItaliano = "Cesari" };
        Assert.Equal(("Gens Giulia", "Gens Iulia"), (iulia.Etichetta, iulia.EtichettaLatina));
        Assert.Equal(("Giulii Cesari", "Iulii Caesares"), (caesares.Etichetta, caesares.EtichettaLatina));
        Assert.Equal("Gens Giulia › Giulii Cesari", GensLabels.Path(iulia, caesares));
        Assert.Equal("Gens Giulia (Gens Iulia) › Giulii Cesari (Iulii Caesares)", GensLabels.PathWithLatin(iulia, caesares));

        // Senza forme italiane si usa il latino; senza il plurale della gens il ramo resta solo.
        var annia = new GensRomana { Nome = "Annia" };
        Assert.Equal("Gens Annia", GensLabels.PathWithLatin(annia, null));
        Assert.Equal("nomen, plurale", annia.DaCompletare);
        Assert.Equal("Veri", GensLabels.Branch(annia, new RamoGens { Plurale = "Veri" }));
        Assert.Equal(string.Empty, Iulia().DaCompletare);
    }

    private static HistoricalPerson MarcusAurelius()
    {
        var annia = new GensRomana { Id = 1, Nome = "Annia" };
        var aurelia = new GensRomana { Id = 2, Nome = "Aurelia" };
        return new HistoricalPerson
        {
            Id = 1, Name = "Marco Aurelio", Onomastica = Onomastiche.Romana, Family = "Dinastia Nerva-Antonina",
            GensId = 1, GensNascita = annia, GensAdozioneId = 2, GensAdottiva = aurelia,
        };
    }

    // Nell'elenco delle Persone il personaggio compare solo sotto la gens di nascita e il suo ramo, non sotto la dinastia.
    [Fact]
    public void Grouping_UsesBirthGensAndBranch()
    {
        var ulpia = new GensRomana { Id = 3, Nome = "Ulpia", Plurale = "Ulpii" };
        var traiani = new RamoGens { Id = 5, GensId = 3, Gens = ulpia, Plurale = "Traiani" };
        var traiano = new HistoricalPerson { Id = 2, Name = "Traiano", Family = "Dinastia Nerva-Antonina", GensId = 3, GensNascita = ulpia, RamoGensId = 5, RamoNascita = traiani };
        Dinastia[] dynasties = [new() { Id = 3, Nome = "Dinastia Nerva-Antonina" }];
        PersonaDinastia[] links = [new() { PersonaId = 1, DinastiaId = 3 }, new() { PersonaId = 2, DinastiaId = 3 }];

        var groups = PeopleGrouping.Build([MarcusAurelius(), traiano], dynasties, links);
        var placed = groups.SelectMany(g => g.SelfAndDescendants()).SelectMany(g => g.Persone.Select(p => (g.Famiglia, p.Nome))).ToList();
        Assert.Equal([("Gens Annia", "Marco Aurelio"), ("Ulpii Traiani", "Traiano")], placed.OrderBy(p => p.Nome));
        Assert.Equal(["Gens Annia", "Gens Ulpia"], groups.Select(g => g.Famiglia));
    }

    [Fact]
    public void Sheet_ShowsBothGens()
    {
        var fields = PersonSheet.MainFields(MarcusAurelius());
        Assert.Equal("Gens Annia", fields.Single(f => f.Etichetta == "Gens di nascita").Valore);
        Assert.Equal("Gens Aurelia", fields.Single(f => f.Etichetta == "Gens d'adozione").Valore);
    }

    // Le forme si salvano come sono scritte; le schede si collegano per identificativo, così rinominare un ramo in italiano
    // non crea doppioni: l'import lo ritrova dal codice Wikidata o dal plurale latino.
    [Fact]
    public void Store_SavesGentesAndFindsThemAgainOnImport()
    {
        var dir = NewTempDir();
        try
        {
            using var store = new PeopleStore(dir);
            var iulia = store.SaveGens(new GensRomana { Nome = "iulia", Nomen = "Iulius", NomeItaliano = "Giulia" });
            var caesares = store.SaveRamo(new RamoGens { GensId = iulia, Plurale = "Caesares" });
            Assert.Throws<InvalidOperationException>(() => store.SaveGens(new GensRomana { Nome = "Iulia" }));
            Assert.Throws<InvalidOperationException>(() => store.SaveRamo(new RamoGens { GensId = iulia, Plurale = "caesares" }));

            var cesare = new HistoricalPerson { Name = "Gaio Giulio Cesare", GensId = iulia, RamoGensId = caesares };
            store.Add(cesare);
            Assert.Equal("Gens Giulia › Caesares", store.GetPerson(cesare.Id)!.Gens);

            // Nome italiano del ramo cambiato a mano: «Cesari». L'import con «Iulii Caesares» lo ritrova e completa il resto.
            store.SaveRamo(new RamoGens { Id = caesares, GensId = iulia, Plurale = "Caesares", NomeItaliano = "Cesari" });
            var imported = new ImportedGens("Q510193", "Iulia", "Iulius", "Iulii", "Giulia", Ramo: new ImportedGensBranch("Q3268376", "Caesares"));
            Assert.Equal((iulia, (int?)caesares), store.EnsureImportedGens(imported));
            var gens = Assert.Single(store.GetGentes());
            Assert.Equal(("Iulii", "Q510193"), (gens.Plurale, gens.WikidataId));
            Assert.Equal("Q3268376", Assert.Single(gens.Rami).WikidataId);
            Assert.Equal("Gens Giulia › Iulii Cesari", store.GetPerson(cesare.Id)!.Gens);

            // Eliminando il ramo la scheda resta nella gens; eliminando la gens resta senza.
            store.DeleteRamo(caesares);
            Assert.Equal((iulia, (int?)null), (store.GetPerson(cesare.Id)!.GensId, store.GetPerson(cesare.Id)!.RamoGensId));
            store.DeleteGens(iulia);
            Assert.Null(store.GetPerson(cesare.Id)!.GensId);
        }
        finally
        {
            Cleanup(dir);
        }
    }

    // Il figlio senza gens prende quella del padre al momento della nascita: la gens d'adozione, se c'è (Commodo, figlio di
    // Marco Aurelio, è della Gens Aurelia).
    [Fact]
    public void Store_ChildInheritsAdoptiveGens()
    {
        var dir = NewTempDir();
        try
        {
            using var store = new PeopleStore(dir);
            var annia = store.SaveGens(new GensRomana { Nome = "Annia" });
            var aurelia = store.SaveGens(new GensRomana { Nome = "Aurelia" });
            var father = new HistoricalPerson { Name = "Marco Aurelio", GensId = annia, GensAdozioneId = aurelia };
            store.Add(father);
            var child = new HistoricalPerson { Name = "Commodo", FatherId = father.Id };
            store.Add(child);

            Assert.True(store.InheritFatherFamilyIfMissing(child.Id));
            Assert.Equal("Gens Aurelia", store.GetPerson(child.Id)!.Gens);
            Assert.Equal("Gens Aurelia", store.GetPerson(father.Id)!.GensAdozione);
        }
        finally
        {
            Cleanup(dir);
        }
    }

    // Backup precedente: le gens erano dinastie («Gens Giulia» con il ramo «Iulii Caesares») e la scheda le scriveva come
    // testo. Al ripristino diventano gentes e rami collegati alla scheda.
    [Fact]
    public void Backup_ConvertsOldGensDynasties()
    {
        var dir = NewTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "people.json"), """
                {"People":[{"Id":1,"Name":"Augusto","Gens":"Gens Ottavia","GensAdozione":"Gens Giulia; Iulii Caesares","Family":"Dinastia Giulio-Claudia"}],
                 "Dynasties":[
                   {"Id":1,"Nome":"Gens Giulia","NomeLatino":"Gens Iulia","Tipo":"gens","WikidataId":"Q510193"},
                   {"Id":2,"Nome":"Iulii Caesares","NomeLatino":"Iulii Caesares","ParentId":1,"Tipo":"gens"},
                   {"Id":3,"Nome":"Gens Ottavia","NomeLatino":"Gens Octavia","Tipo":"gens"},
                   {"Id":4,"Nome":"Dinastia Giulio-Claudia","NomeLatino":"Domus Iulio-Claudia"}]}
                """);
            using var store = new PeopleStore(dir);
            var augusto = Assert.Single(store.LoadAll());
            Assert.Equal("Gens Ottavia (Gens Octavia)", GensLabels.PathWithLatin(augusto.GensNascita, augusto.RamoNascita));
            Assert.Equal("Gens Giulia (Gens Iulia) › Iulii Caesares", GensLabels.PathWithLatin(augusto.GensAdottiva, augusto.RamoAdottivo));
            var iulia = store.GetGentes().Single(g => g.Nome == "Iulia");
            Assert.Equal(("Giulia", "Iulii", "Q510193"), (iulia.NomeItaliano, iulia.Plurale, iulia.WikidataId));
            Assert.Equal("Caesares", Assert.Single(iulia.Rami).Plurale);
            Assert.Equal(["Dinastia Giulio-Claudia"], store.GetDynasties().Select(d => d.Nome));
        }
        finally
        {
            Cleanup(dir);
        }
    }

    // La gens letta dall'import resta in memoria: nella finestra «Gentes e rami» compare come nuova, con il ramo, e si
    // corregge; si crea solo al salvataggio della scheda.
    [Fact]
    public void ImportedGens_IsShownAsNewAndCreatedOnlyWhenSaved()
    {
        var dir = NewTempDir();
        try
        {
            WpfTestThread.Run(() =>
            {
                using var store = new PeopleStore(dir);
                var imported = new ImportedGens("Q510193", "Iulia", "Iulius", "Iulii", "Giulia", Ramo: new ImportedGensBranch("Q3268376", "Caesares"));
                var pending = new PendingGens(imported);
                var window = new GentesWindow(store, new GensChoice(null, null, pending), [pending], pick: true)
                {
                    ShowInTaskbar = false,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = -10000,
                };
                window.Show();
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                var root = Assert.Single((IEnumerable<GentesNode>)window.GensTree.ItemsSource);
                Assert.Equal(("Gens Giulia", "nuova, sarà creata al salvataggio"), (root.Etichetta, root.Nota));
                var branch = Assert.Single(root.Rami);
                Assert.True(branch.IsSelected);
                Assert.Equal("Iulii Caesares", branch.Etichetta);

                // Correzione del ramo in memoria: niente nell'archivio.
                window.RamoItalianBox.Text = "Cesari";
                typeof(GentesWindow).GetMethod("Save_Click", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(window, [window, new RoutedEventArgs()]);
                Assert.Equal("Cesari", pending.Data.Ramo?.NomeItaliano);
                Assert.Empty(store.GetGentes());
                window.Close();

                var editor = new PersonEditorWindow(store, null, new WikipediaImportResult { Name = "Gaio Giulio Cesare", GensNascita = pending.Data })
                {
                    ShowInTaskbar = false,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = -10000,
                };
                editor.Show();
                editor.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                Assert.Empty(store.GetGentes());
                var saved = (bool)typeof(PersonEditorWindow).GetMethod("EnsureSaved", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(editor, null)!;
                Assert.True(saved);
                editor.Close();

                var person = Assert.Single(store.LoadAll());
                Assert.Equal("Gens Giulia › Iulii Cesari", person.Gens);
                Assert.Equal("Caesares", Assert.Single(Assert.Single(store.GetGentes()).Rami).Plurale);
            });
        }
        finally
        {
            Cleanup(dir);
        }
    }

    // Nome latino delle dinastie (2026-11-14): l'import lo scrive solo se manca, il modulo dell'archivio lo cambia.
    [Fact]
    public void Store_KeepsLatinNameOfDynasties()
    {
        var dir = NewTempDir();
        try
        {
            using var store = new PeopleStore(dir);
            var claudia = store.EnsureDynasty("Dinastia Giulio-Claudia", "Q192841", null, nomeLatino: "Domus Iulio-Claudia");
            Assert.Equal(claudia, store.EnsureDynasty("dinastia giulio-claudia", "Q192841", null, nomeLatino: "Altro"));
            Assert.Equal("Domus Iulio-Claudia", store.GetDynasties().Single(d => d.Id == claudia).NomeLatino);

            store.SaveDynasty(new Dinastia { Id = claudia, Nome = "Dinastia Giulio-Claudia", NomeLatino = " domus Iulio-Claudia " });
            Assert.Equal("Dinastia Giulio-Claudia (Domus Iulio-Claudia)", store.GetDynasties().Single(d => d.Id == claudia).NomeCompleto);
        }
        finally
        {
            Cleanup(dir);
        }
    }

    // Archivio di prima della separazione (2026-11-16, com'era quello dell'utente): tra le dinastie le gens Ottavia e Giulia
    // con i loro rami, scritti a mano («Giulii Cesari») e importati («Iulii Caesares», tipo gens), e la «dinastia
    // Giulio-Claudia» con la minuscola. Al caricamento le gens escono dalle Dinastie senza doppioni tra le gentes, e la
    // dinastia prende la maiuscola anche nel campo Famiglia della scheda.
    [Fact]
    public void Load_RemovesOldGensDynastiesAndCapitalizes()
    {
        var dir = NewTempDir();
        try
        {
            using (var store = new PeopleStore(dir))
            {
                var iulia = store.SaveGens(new GensRomana { Nome = "Iulia", Plurale = "Iulii", WikidataId = "Q510193" });
                store.SaveRamo(new RamoGens { GensId = iulia, Plurale = "Caesares", WikidataId = "Q3268376" });
                store.Add(new HistoricalPerson { Name = "Augusto", Family = "dinastia Giulio-Claudia" });
            }

            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(dir, "historic.db")};Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = """
                    INSERT INTO Dinastie (Id, Nome, NomeLatino, Tipo, WikidataId, ParentId, Note) VALUES
                    (1, 'Gens Ottavia', 'Gens Octavia', 'gens', 'Q1759265', NULL, ''),
                    (2, 'Ottavi Rufi', 'Octavii Rufi', '', '', 1, ''),
                    (3, 'Gens Giulia', 'Gens Iulia', 'gens', 'Q510193', NULL, ''),
                    (4, 'Giulii Cesari', 'Iulii Caesares', '', '', 3, ''),
                    (5, 'dinastia Giulio-Claudia', 'domus Iulio-Claudia', '', 'Q192841', NULL, ''),
                    (6, 'Iulii Caesares', 'Iulii Caesares', 'gens', 'Q3268376', 3, ''),
                    (7, 'Octavii Rufi', 'Octavii Rufi', 'gens', 'Q105335230', 1, '');
                    INSERT INTO PersoneDinastie (PersonaId, DinastiaId) SELECT Id, 5 FROM People;
                    """;
                command.ExecuteNonQuery();
            }

            using (var store = new PeopleStore(dir))
            {
                var people = store.LoadAll();
                var dynasty = Assert.Single(store.GetDynasties());
                Assert.Equal(("Dinastia Giulio-Claudia", "Domus Iulio-Claudia"), (dynasty.Nome, dynasty.NomeLatino));
                Assert.Equal("Dinastia Giulio-Claudia", Assert.Single(people).Family);

                // La Gens Iulia e il ramo Caesares c'erano già: nessun doppione; la Gens Octavia è nuova, con il ramo Rufi
                // (scritto due volte, a mano e importato) una volta sola.
                var gentes = store.GetGentes();
                Assert.Equal(["Gens Iulia", "Gens Ottavia"], gentes.Select(g => g.Etichetta));
                Assert.Equal(["Caesares"], gentes.Single(g => g.Nome == "Iulia").Rami.Select(r => r.Plurale));
                var octavia = gentes.Single(g => g.Nome == "Octavia");
                Assert.Equal(("Octavii", "Q1759265"), (octavia.Plurale, octavia.WikidataId));
                Assert.Equal(["Rufi"], octavia.Rami.Select(r => r.Plurale));

                // Al secondo caricamento non cambia più nulla.
                store.LoadAll();
                Assert.Equal(2, store.GetGentes().Count);
            }
        }
        finally
        {
            Cleanup(dir);
        }
    }

    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"historic-gentes-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Cleanup(string dir)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(dir, recursive: true);
    }
}
