using System.IO;
using HistoricEditor;
using Xunit;

namespace HistoricEditor.Tests;

// Resti dei luoghi storici (2026-11-16): un sito che esiste ancora sta nel luogo attuale che contiene il luogo storico,
// con la nota «Resti di …»; la scheda indica il luogo storico e, come luogo specifico, i resti (2026-11-17).
public class PlaceRemainsTests
{
    // Pompeo, morto a Pelusio: la scheda è nel luogo storico Pelusio, con il Sito archeologico di Pelusio come luogo
    // specifico; il sito, in rovina, è nel Governatorato di Porto Said, accanto a Pelusio e non dentro.
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
            var pelusium = store.GetPlace(saved.DeathLuogoId!.Value)!;
            Assert.Equal(("Pelusio", true), (pelusium.Nome, pelusium.Storico));

            var site = Assert.Single(store.GetSpecificPlaces(), s => s.Nome == "Sito archeologico di Pelusio");
            Assert.Equal(pelusium.ParentId, site.LuogoId);
            Assert.Equal("Governatorato di Porto Said", store.GetPlace(site.LuogoId)!.Nome);
            Assert.Equal(("Resti di Pelusio.", CondizioniLuogo.InRovina), (site.Note, site.Condizione));
            Assert.Equal(site.Id, saved.DeathLuogoSpecificoId);
            Assert.Equal("Pelusio, Governatorato di Porto Said, Egitto (Sito archeologico di Pelusio)", saved.DeathPlace);
        }
        finally
        {
            Cleanup(dir);
        }
    }

    // Archivio di prima: il sito stava dentro il luogo storico e la scheda indicava il sito. Al caricamento il sito passa
    // al luogo attuale, con la nota, e la scheda indica il luogo storico e il sito; un luogo scomparso dentro il luogo
    // storico resta lì.
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
                Assert.Equal((historicId, (int?)siteId), (person.DeathLuogoId, person.DeathLuogoSpecificoId));
                Assert.Equal("Pelusio, Governatorato di Porto Said, Egitto (Sito archeologico di Pelusio)", person.DeathPlace);
            }
        }
        finally
        {
            Cleanup(dir);
        }
    }

    // Luogo specifico separato dal luogo: si scrive solo la parte di percorso diversa, fino al primo luogo in comune.
    [Fact]
    public void DescribeRole_SeparateSpecific_ShowsOnlyTheDifferentPart()
    {
        var dir = NewTempDir();
        try
        {
            using var store = new PeopleStore(dir);
            GeoLevel[] rieti = [new("Stato", "Italia"), new("Regione", "Lazio"), new("Provincia", "Rieti")];
            var cittareale = store.EnsurePlace([.. rieti, new("Comune", "Cittareale")], null, null)!.Value;
            var amatrice = store.EnsurePlace([.. rieti, new("Comune", "Amatrice")], null, null)!.Value;
            var falacrine = store.EnsureHistoricPlace("Falacrine", "Falacrinae", "Città antica", cittareale, "", null, null);
            var near = store.EnsureSpecificPlace("Sito di Falacrine", cittareale, "");
            var far = store.EnsureSpecificPlace("Sito X", amatrice, "");
            var spain = store.EnsurePlace([new("Stato", "Spagna")], null, null)!.Value;
            var abroad = store.EnsureSpecificPlace("Sito Y", spain, "");
            var inside = store.EnsureSpecificPlace("Tempio scomparso", falacrine, "", condizione: CondizioniLuogo.Distrutto);

            Assert.Equal("Falacrine, Cittareale, Rieti, Lazio, Italia (Sito di Falacrine)", store.DescribeRole(falacrine, near).DisplayText);
            Assert.Equal("Falacrine, Cittareale, Rieti, Lazio, Italia (Sito X, Amatrice, Rieti)", store.DescribeRole(falacrine, far).DisplayText);
            Assert.Equal("Falacrine, Cittareale, Rieti, Lazio, Italia (Sito Y, Spagna)", store.DescribeRole(falacrine, abroad).DisplayText);
            Assert.Equal("Tempio scomparso (Falacrine, Cittareale, Rieti, Lazio, Italia)", store.DescribeRole(falacrine, inside).DisplayText);
            Assert.Equal("Sito X (Amatrice, Rieti, Lazio, Italia)", store.DescribeRole(null, far).DisplayText);
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
