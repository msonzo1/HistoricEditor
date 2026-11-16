using System.IO;
using HistoricEditor;
using Xunit;

namespace HistoricEditor.Tests;

// Resti dei luoghi storici (2026-11-16): un sito che esiste ancora sta nel luogo attuale che contiene il luogo storico,
// con la nota «Resti di …»; la scheda resta nel luogo storico.
public class PlaceRemainsTests
{
    // Pompeo, morto a Pelusio: la scheda è nel luogo storico Pelusio; il Sito archeologico di Pelusio, in rovina, è nel
    // Governatorato di Porto Said, accanto a Pelusio e non dentro.
    [Fact]
    public async Task Import_PutsRemainsInTheCurrentPlace()
    {
        var import = await ImportFixtures.ImportAsync("Gneo Pompeo Magno");
        var dir = NewTempDir();
        try
        {
            using var store = new PeopleStore(dir);
            var (pompey, _) = ImportedPersonCreator.Create(store, import);
            var saved = store.GetPerson(pompey.Id)!;
            Assert.Null(saved.DeathLuogoSpecificoId);
            var pelusium = store.GetPlace(saved.DeathLuogoId!.Value)!;
            Assert.Equal(("Pelusio", true), (pelusium.Nome, pelusium.Storico));

            var site = Assert.Single(store.GetSpecificPlaces(), s => s.Nome == "Sito archeologico di Pelusio");
            Assert.Equal(pelusium.ParentId, site.LuogoId);
            Assert.Equal("Governatorato di Porto Said", store.GetPlace(site.LuogoId)!.Nome);
            Assert.Equal(("Resti di Pelusio.", CondizioniLuogo.InRovina), (site.Note, site.Condizione));
        }
        finally
        {
            Cleanup(dir);
        }
    }

    // Archivio di prima: il sito stava dentro il luogo storico e la scheda indicava il sito. Al caricamento il sito passa
    // al luogo attuale, con la nota, e la scheda al luogo storico; un luogo scomparso dentro il luogo storico resta lì.
    [Fact]
    public void Load_MovesOldRemainsOutOfTheHistoricPlace()
    {
        var dir = NewTempDir();
        try
        {
            int historicId, siteId, destroyedId, personId, governorateId;
            using (var store = new PeopleStore(dir))
            {
                governorateId = store.EnsurePlace([new GeoLevel("Stato", "Egitto"), new GeoLevel("Governatorato", "Governatorato di Porto Said")], null, null)!.Value;
                historicId = store.EnsureHistoricPlace("Pelusio", "Pelusium", "Città antica", governorateId, "Q1000", null, null);
                siteId = store.EnsureSpecificPlace("Sito archeologico di Pelusio", historicId, "", tipo: "Sito archeologico", condizione: CondizioniLuogo.InRovina);
                destroyedId = store.EnsureSpecificPlace("Tempio scomparso", historicId, "", condizione: CondizioniLuogo.Distrutto);
                var person = new HistoricalPerson { Name = "Pompeo", DeathLuogoSpecificoId = siteId };
                store.Add(person);
                personId = person.Id;
            }

            using (var store = new PeopleStore(dir))
            {
                store.LoadAll();
                var site = store.GetSpecificPlace(siteId)!;
                Assert.Equal((governorateId, "Resti di Pelusio."), (site.LuogoId, site.Note));
                Assert.Equal(historicId, store.GetSpecificPlace(destroyedId)!.LuogoId);
                var person = store.GetPerson(personId)!;
                Assert.Equal((historicId, (int?)null), (person.DeathLuogoId, person.DeathLuogoSpecificoId));
                Assert.StartsWith("Pelusio", person.DeathPlace);
            }
        }
        finally
        {
            Cleanup(dir);
        }
    }

    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"historic-remains-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Cleanup(string dir)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
