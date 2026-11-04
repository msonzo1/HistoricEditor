using System.IO;
using HistoricEditor;
using Xunit;

namespace HistoricEditor.Tests;

public class StoreContextTests
{
    [Fact]
    public void Store_DoesNotKeepStaleOrPendingEntities()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"historic-ctx-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            using var store = new PeopleStore(dir);
            var id = store.CreatePerson("Carlo", Sessi.Maschio);
            store.SetSex(id, Sessi.Maschio);

            // Modifica fatta da un altro contesto sullo stesso archivio: il negozio deve vederla.
            using (var other = new AppDbContext(Path.Combine(dir, "historic.db")))
            {
                other.People.Single(p => p.Id == id).Sesso = Sessi.Femmina;
                other.SaveChanges();
            }

            Assert.Equal(Sessi.Femmina, store.GetSex(id));
            store.SetSex(id, Sessi.Maschio);
            Assert.Equal(Sessi.Maschio, store.GetSex(id));

            // Un errore non lascia modifiche pendenti che blocchino i salvataggi successivi.
            Assert.Throws<InvalidOperationException>(() => store.SetParent(id, id, mother: false));
            var child = store.CreatePerson("Luigi", Sessi.Maschio);
            store.SetParent(child, id, mother: false);
            Assert.Equal((null, id), store.GetParents(child));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Update_PersistsTitleDateSources()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"historic-ctx-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            using var store = new PeopleStore(dir);
            var person = store.GetPerson(store.CreatePerson("Carlo", Sessi.Maschio))!;
            person.Titles = [new PersonTitle { Titolo = "Imperatore", FontiDate = [new() { Fonte = "Wikidata", Inizio = "800", Fine = "814" }] }];
            store.Update(person);

            var source = Assert.Single(Assert.Single(store.GetPerson(person.Id)!.Titles).FontiDate);
            Assert.Equal(("Wikidata", "800", "814"), (source.Fonte, source.Inizio, source.Fine));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Update_RejectsSexContradictingParentRole()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"historic-ctx-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            using var store = new PeopleStore(dir);
            var father = store.CreatePerson("Carlo", Sessi.Maschio);
            var child = store.CreatePerson("Luigi", Sessi.Maschio);
            store.SetParent(child, father, mother: false);

            var edited = store.GetPerson(father)!;
            edited.Sesso = Sessi.Femmina;
            edited.Name = "Carla";

            var error = Assert.Throws<InvalidOperationException>(() => store.Update(edited));
            Assert.Contains("padre", error.Message);
            var saved = store.GetPerson(father)!;
            Assert.Equal(Sessi.Maschio, saved.Sesso);
            Assert.Equal("Carlo", saved.Name);

            // Senza cambiare sesso, le altre modifiche vengono salvate.
            edited.Sesso = Sessi.Maschio;
            store.Update(edited);
            Assert.Equal("Carla", store.GetPerson(father)!.Name);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void PlaceTexts_FollowRenameAndDelete()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"historic-ctx-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            using var store = new PeopleStore(dir);
            var comune = store.EnsurePlace(
                [new GeoLevel("Stato", "Italia"), new GeoLevel("Regione", "Lombardia"), new GeoLevel("Provincia", "Milano"), new GeoLevel("Comune", "Milano")],
                null,
                null)!.Value;
            var provincia = store.GetAllPlaces().Single(l => l.Tipo == "Provincia" && l.Nome == "Milano").Id;
            var chiesa = store.EnsureSpecificPlace("Sant'Ambrogio", comune, string.Empty);
            store.Add(new HistoricalPerson { Name = "Ambrogio", BirthLuogoId = comune, BirthPlace = "Milano, Milano, Lombardia, Italia", BurialLuogoSpecificoId = chiesa, BurialPlace = "Sant'Ambrogio (Milano, Milano, Lombardia, Italia)" });
            store.Add(new HistoricalPerson { Name = "Libero", BirthPlace = "Mediolanum" });

            store.UpdatePlace(provincia, "Città metropolitana di Milano", null, null, false, string.Empty, string.Empty, false);
            var ambrogio = store.LoadAll().Single(p => p.Name == "Ambrogio");
            Assert.Equal("Milano, Città metropolitana di Milano, Lombardia, Italia", ambrogio.BirthPlace);
            Assert.Equal("Sant'Ambrogio (Milano, Città metropolitana di Milano, Lombardia, Italia)", ambrogio.BurialPlace);

            store.SaveSpecificPlace(new LuogoSpecifico { Id = chiesa, Nome = "Basilica di Sant'Ambrogio", LuogoId = comune });
            Assert.StartsWith("Basilica di Sant'Ambrogio (", store.LoadAll().Single(p => p.Name == "Ambrogio").BurialPlace);

            // Eliminando la provincia spariscono anche comune e luogo specifico: le schede perdono legame e testo.
            store.DeletePlace(provincia);
            ambrogio = store.LoadAll().Single(p => p.Name == "Ambrogio");
            Assert.Null(ambrogio.BirthLuogoId);
            Assert.Null(ambrogio.BurialLuogoSpecificoId);
            Assert.Equal(string.Empty, ambrogio.BirthPlace);
            Assert.Equal(string.Empty, ambrogio.BurialPlace);
            // Il testo libero di una scheda senza luogo collegato resta invariato.
            Assert.Equal("Mediolanum", store.LoadAll().Single(p => p.Name == "Libero").BirthPlace);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void EnsureSpecificPlace_FillsTypeOnlyWhenMissing()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"historic-ctx-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            using var store = new PeopleStore(dir);
            var comune = store.EnsurePlace(
                [new GeoLevel("Stato", "Italia"), new GeoLevel("Regione", "Lombardia"), new GeoLevel("Provincia", "Como"), new GeoLevel("Comune", "Tremezzina")],
                null,
                null)!.Value;

            // Già presente senza tipo (import precedente): il nuovo import lo completa.
            var giulino = store.EnsureSpecificPlace("Giulino", comune, "Q3770150");
            Assert.Equal(giulino, store.EnsureSpecificPlace("Giulino", comune, "Q3770150", tipo: "Frazione"));
            Assert.Equal("Frazione", store.GetSpecificPlace(giulino)!.Tipo);

            // Un tipo scelto dall'utente non viene sovrascritto.
            store.SaveSpecificPlace(new LuogoSpecifico { Id = giulino, Nome = "Giulino", Tipo = "Borgo", LuogoId = comune, WikidataId = "Q3770150" });
            store.EnsureSpecificPlace("Giulino", comune, "Q3770150", tipo: "Frazione");
            Assert.Equal("Borgo", store.GetSpecificPlace(giulino)!.Tipo);

            var abbazia = store.EnsureSpecificPlace("Abbazia di Piona", comune, "Q1", tipo: SpecificPlaceTypes.Describe(["Q160742"]));
            Assert.Equal("Abbazia", store.GetSpecificPlace(abbazia)!.Tipo);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData("Q18413256", "", "Frazione")]
    [InlineData("Q5|Q44613", "", "Monastero")]
    [InlineData("Q999", "torre campanaria", "Torre campanaria")]
    [InlineData("Q999", "", "")]
    public void SpecificPlaceTypes_PreferKnownTypesThenItalianLabel(string typeIds, string label, string expected)
    {
        var labels = label.Length == 0 ? null : new Dictionary<string, string> { ["Q999"] = label };

        Assert.Equal(expected, SpecificPlaceTypes.Describe(typeIds.Split('|'), labels));
    }

    [Fact]
    public void WouldReplaceParent_DetectsDifferentExistingParent()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"historic-ctx-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            using var store = new PeopleStore(dir);
            var father = store.CreatePerson("Carlo", Sessi.Maschio);
            var otherMan = store.CreatePerson("Pietro", Sessi.Maschio);
            var mother = store.CreatePerson("Anna", Sessi.Femmina);
            var otherWoman = store.CreatePerson("Maria", Sessi.Femmina);
            var child = store.CreatePerson("Luigi", Sessi.Maschio);
            store.AssignChild(father, child, mother);

            Assert.False(store.WouldReplaceParent(father, child, null));
            Assert.False(store.WouldReplaceParent(mother, child, father));
            Assert.True(store.WouldReplaceParent(otherMan, child, null));
            Assert.True(store.WouldReplaceParent(father, child, otherWoman));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(dir, recursive: true);
        }
    }
}
