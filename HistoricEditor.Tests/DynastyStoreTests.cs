using System.IO;
using HistoricEditor;
using Xunit;

namespace HistoricEditor.Tests;

public class DynastyStoreTests
{
    [Fact]
    public void Dynasties_EnsureRenameAndPreventCycles()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"historic-dyn-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            using var store = new PeopleStore(dir);
            store.Add(new HistoricalPerson { Name = "Filippo", Family = "Asburgo di Spagna" });

            var asburgo = store.EnsureDynasty("Asburgo", "Q153134", null);
            var spagna = store.EnsureDynasty("Asburgo di Spagna", "", asburgo);
            Assert.Equal(spagna, store.EnsureDynasty("ASBURGO DI SPAGNA", "", null));
            Assert.Equal(asburgo, store.GetDynasties().Single(d => d.Id == spagna).ParentId);

            // Una dinastia non può diventare ramo di un proprio ramo.
            Assert.Throws<InvalidOperationException>(() => store.SaveDynasty(new Dinastia { Id = asburgo, Nome = "Asburgo", ParentId = spagna }));

            // Rinominando la dinastia si aggiorna la famiglia delle schede collegate.
            store.SaveDynasty(new Dinastia { Id = spagna, Nome = "Asburgo-Spagna", ParentId = asburgo });
            Assert.Equal("Asburgo-Spagna", store.LoadAll().Single().Family);

            store.DeleteDynasty(asburgo);
            Assert.Null(store.GetDynasties().Single(d => d.Id == spagna).ParentId);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Dynasties_LinkPeopleByIdAndFollowRenames()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"historic-dyn-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            using var store = new PeopleStore(dir);
            var asburgo = store.EnsureDynasty("Asburgo", "", null);
            var maria = new HistoricalPerson { Name = "Maria Teresa", Family = "asburgo ; Lorena" };
            store.Add(maria);
            Assert.Equal([asburgo], store.GetPersonDynastyIds(maria.Id));

            // La dinastia registrata dopo il salvataggio si collega alle schede che la indicano.
            var lorena = store.EnsureDynasty("Lorena", "", null);
            Assert.Equal([asburgo, lorena], store.GetPersonDynastyIds(maria.Id).Order());

            // La rinomina segue il collegamento e lascia invariate le altre famiglie della scheda.
            store.SaveDynasty(new Dinastia { Id = lorena, Nome = "Casa di Lorena" });
            Assert.Equal("asburgo; Casa di Lorena", store.GetPerson(maria.Id)!.Family);
            Assert.Equal([asburgo, lorena], store.GetPersonDynastyIds(maria.Id).Order());

            // Togliendo una famiglia dalla scheda si toglie il collegamento.
            var copy = store.GetPerson(maria.Id)!;
            copy.Family = "Casa di Lorena";
            store.Update(copy);
            Assert.Equal([lorena], store.GetPersonDynastyIds(maria.Id));

            store.DeleteDynasty(lorena);
            Assert.Empty(store.GetDynastyLinks());
            Assert.Equal("Casa di Lorena", store.GetPerson(maria.Id)!.Family);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(dir, recursive: true);
        }
    }
}
