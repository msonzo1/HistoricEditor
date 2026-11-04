using System.IO;
using System.Text.Json;
using HistoricEditor;
using Xunit;

namespace HistoricEditor.Tests;

public class RelationTests
{
    [Theory]
    [InlineData("Maestro e allievo", true, Sessi.Maschio, "Maestro di")]
    [InlineData("Maestro e allievo", false, Sessi.Femmina, "Allieva di")]
    [InlineData("Tutela", true, Sessi.Femmina, "Tutrice di")]
    [InlineData("rivalità", false, "", "Rivale di")]
    [InlineData("Consigliere", true, Sessi.Maschio, "Consigliere:")]
    public void Etichetta_DependsOnDirectionAndSex(string tipo, bool attivo, string sesso, string expected)
        => Assert.Equal(expected, Relazioni.Etichetta(tipo, attivo, sesso));

    [Fact]
    public void Scelte_ListSymmetricTypesOnce_AndDirectionalTypesTwice()
    {
        var scelte = Relazioni.Scelte(Sessi.Maschio);

        Assert.Single(scelte, s => s.Tipo == "Alleanza");
        Assert.Contains(scelte, s => s is { Etichetta: "Allievo di", Tipo: "Maestro e allievo", Attivo: false });
        Assert.Contains(scelte, s => s is { Etichetta: "Mecenate di", Tipo: "Mecenatismo", Attivo: true });
    }

    [Fact]
    public void Interpreta_RecognizesChoicesInBothGenders_AndKeepsFreeTypes()
    {
        Assert.Equal(new RelazioneScelta("Allievo di", "Maestro e allievo", false), Relazioni.Interpreta("allievo di", Sessi.Femmina));
        Assert.Equal("Mecenatismo", Relazioni.Interpreta("Mecenatismo", Sessi.Maschio)?.Tipo);
        Assert.Equal(new RelazioneScelta("Consigliere", "Consigliere", true), Relazioni.Interpreta(" Consigliere ", Sessi.Maschio));
        Assert.Null(Relazioni.Interpreta("  ", Sessi.Maschio));
    }

    [Fact]
    public void Relations_ReadFromBothSides_DetectDuplicates_AndCleanUpOnDelete()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"historic-rel-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            using var store = new PeopleStore(dir);
            var verrocchio = store.CreatePerson("Andrea del Verrocchio", Sessi.Maschio);
            var leonardo = store.CreatePerson("Leonardo da Vinci", Sessi.Maschio);
            var isabella = store.CreatePerson("Isabella d'Este", Sessi.Femmina);

            Assert.Throws<InvalidOperationException>(() => store.SaveRelation(new Relazione { Persona1Id = leonardo, Persona2Id = leonardo, Tipo = "Amicizia" }));
            Assert.Throws<InvalidOperationException>(() => store.SaveRelation(new Relazione { Persona1Id = leonardo, Persona2Id = isabella, Tipo = " " }));

            store.SaveRelation(new Relazione { Persona1Id = verrocchio, Persona2Id = leonardo, Tipo = "maestro e allievo", Inizio = " 1466 ", Fine = "1476" });
            store.SaveRelation(new Relazione { Persona1Id = isabella, Persona2Id = leonardo, Tipo = "Mecenatismo" });

            var fromLeonardo = store.GetRelationsForPerson(leonardo);
            Assert.Equal(["Allievo di Andrea del Verrocchio · 1466–1476", "Protetto di Isabella d'Este"], fromLeonardo.Select(r => r.Descrizione));
            Assert.Equal("Mecenate di Leonardo da Vinci", Assert.Single(store.GetRelationsForPerson(isabella)).Descrizione);
            Assert.Equal("Maestro e allievo", store.GetRelation(fromLeonardo[0].Id)?.Tipo);

            // Verso significativo per maestro e allievo, indifferente per i tipi simmetrici.
            Assert.True(store.HasRelation(verrocchio, leonardo, "Maestro e allievo"));
            Assert.False(store.HasRelation(leonardo, verrocchio, "Maestro e allievo"));
            store.SaveRelation(new Relazione { Persona1Id = leonardo, Persona2Id = isabella, Tipo = "Amicizia" });
            Assert.True(store.HasRelation(isabella, leonardo, "amicizia"));

            store.Delete(leonardo);
            Assert.Empty(store.GetRelations());
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Backup_RoundTrip_RestoresRelations()
    {
        var sourceDir = Path.Combine(Path.GetTempPath(), $"historic-rel-{Guid.NewGuid():N}");
        var restoreDir = Path.Combine(Path.GetTempPath(), $"historic-rel-{Guid.NewGuid():N}");
        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(restoreDir);
        try
        {
            using (var store = new PeopleStore(sourceDir))
            {
                var carlo = store.CreatePerson("Carlo V", Sessi.Maschio);
                var francesco = store.CreatePerson("Francesco I", Sessi.Maschio);
                store.SaveRelation(new Relazione { Persona1Id = carlo, Persona2Id = francesco, Tipo = "Rivalità", Note = "Guerre d'Italia" });
                store.ExportJson(store.LoadAll());
            }

            File.Copy(Path.Combine(sourceDir, "people.json"), Path.Combine(restoreDir, "people.json"));

            using var restored = new PeopleStore(restoreDir);
            var francescoId = restored.LoadAll().Single(p => p.Name == "Francesco I").Id;
            var relation = Assert.Single(restored.GetRelationsForPerson(francescoId));
            Assert.Equal("Rivale di Carlo V", relation.Descrizione);
            Assert.Equal("Guerre d'Italia", relation.Note);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(sourceDir, recursive: true);
            Directory.Delete(restoreDir, recursive: true);
        }
    }

    [Fact]
    public void Windows_ShowRelationFromThePersonPointOfView()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"historic-rel-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            WpfTestThread.Run(() =>
            {
                using var store = new PeopleStore(dir);
                var verrocchio = store.CreatePerson("Andrea del Verrocchio", Sessi.Maschio);
                var leonardo = store.CreatePerson("Leonardo da Vinci", Sessi.Maschio);
                var id = store.SaveRelation(new Relazione { Persona1Id = verrocchio, Persona2Id = leonardo, Tipo = "Maestro e allievo" });

                var editor = new RelationEditorWindow(store, store.GetRelation(id)!, leonardo, "Leonardo da Vinci", "Andrea del Verrocchio");
                Assert.Equal("Allievo di", editor.TipoBox.Text);
                Assert.Equal("Si legge: Leonardo da Vinci — Allievo di Andrea del Verrocchio", editor.PreviewText.Text);
                editor.Close();

                var person = new PersonEditorWindow(store, store.GetPerson(verrocchio));
                person.Tabs.SelectedItem = person.FamilyTab;
                var view = Assert.IsType<RelationView>(Assert.Single(person.RelationsList.Items));
                Assert.Equal("Maestro di Leonardo da Vinci", view.Descrizione);
                Assert.Equal(System.Windows.Visibility.Collapsed, person.NoRelationsText.Visibility);
                person.Close();
            });
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ReadRelations_MapsTeachersStudentsAndInfluences()
    {
        const string json = """
            {
              "P1066":[{"rank":"normal","mainsnak":{"datavalue":{"value":{"id":"Q5669"}}}}],
              "P802":[{"rank":"normal","mainsnak":{"datavalue":{"value":{"id":"Q5592"}}}}],
              "P737":[{"rank":"deprecated","mainsnak":{"datavalue":{"value":{"id":"Q1"}}}},
                      {"rank":"normal","mainsnak":{"datavalue":{"value":{"id":"Q868"}}}}]
            }
            """;
        using var document = JsonDocument.Parse(json);

        var relations = WikipediaImportService.ReadRelations(document.RootElement);

        Assert.Equal(
            [
                new ImportedRelation("Q5669", "Maestro e allievo", false),
                new ImportedRelation("Q5592", "Maestro e allievo", true),
                new ImportedRelation("Q868", "Influenza", false),
            ],
            relations);
    }
}
